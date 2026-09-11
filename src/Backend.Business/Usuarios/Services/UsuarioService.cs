using Backend.Business.Abstractions;
using Backend.Business.Admin.Interfaces;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Usuarios.Interfaces;
using Backend.Business.Usuarios.Models;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Usuarios.Services;

/// <summary>
/// Regras de gestão de usuários.
/// </summary>
/// <remarks>
/// Este service é o exemplo de referência do projeto: injeção por construtor primário, validação
/// pelo validator, persistência decidida pelo <see cref="IUnitOfWork"/> e todo retorno em
/// <see cref="Result"/>. Copie a forma dele ao criar uma feature nova.
/// </remarks>
/// <param name="usuarioRepository">Acesso a dados de usuário.</param>
/// <param name="refreshTokenRepository">Sessões abertas, derrubadas ao desativar a conta.</param>
/// <param name="adminRepository">Contagem de administradores, usada nas travas de segurança.</param>
/// <param name="userManager">API do Identity para vínculo de perfis.</param>
/// <param name="atualizarValidator">Validador dos dados de alteração.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class UsuarioService(
    IUsuarioRepository usuarioRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IAdminRepository adminRepository,
    UserManager<Usuario> userManager,
    IValidator<AtualizarUsuario> atualizarValidator,
    IUnitOfWork unitOfWork,
    ILogger<UsuarioService> logger
) : IUsuarioService
{
    private static readonly Erro UltimoAdministrador = Erro.Conflito(
        "usuario.ultimo_administrador",
        "Este é o único administrador ativo. Promova outro usuário antes de remover o acesso deste."
    );

    /// <inheritdoc />
    public async Task<Result<PaginaDe<UsuarioResumo>>> Listar(PaginacaoRequest paginacao, string? busca, CancellationToken ct = default)
    {
        var pagina = await usuarioRepository.Listar(paginacao.Normalizar(), busca, ct);

        return Result.Ok(pagina);
    }

    /// <inheritdoc />
    public async Task<Result<UsuarioDetalhe>> ObterPorId(Guid id, CancellationToken ct = default)
    {
        var usuario = await usuarioRepository.ObterDetalhe(id, ct);

        return usuario is null ? Erro.NaoEncontrado("usuario.nao_encontrado", "Usuário não encontrado.") : Result.Ok(usuario);
    }

    /// <inheritdoc />
    public async Task<Result<UsuarioDetalhe>> Atualizar(Guid id, AtualizarUsuario dados, CancellationToken ct = default)
    {
        var validacao = atualizarValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<UsuarioDetalhe>(validacao.Erros);

        var usuario = await usuarioRepository.ObterParaEdicao(id, ct);
        if (usuario is null)
            return Erro.NaoEncontrado("usuario.nao_encontrado", "Usuário não encontrado.");

        usuario.Nome = dados.Nome.Trim();
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Usuário {UsuarioId} atualizado.", id);

        var detalhe = await usuarioRepository.ObterDetalhe(id, ct);

        return detalhe is null ? Erro.NaoEncontrado("usuario.nao_encontrado", "Usuário não encontrado.") : Result.Ok(detalhe);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Desativar derruba as sessões: os refresh tokens do usuário são revogados na mesma
    /// transação, então ele não consegue renovar. O access token que já está na mão dele
    /// continua válido até expirar — no máximo os minutos configurados em
    /// <c>Jwt:MinutosDeValidadeDoAccessToken</c>. É o custo de o JWT ser validado por
    /// assinatura e não por consulta; fechar essa janela exigiria ir ao banco a cada
    /// requisição, o que é caro demais para o ganho.
    /// </remarks>
    public async Task<Result> AlterarAtivacao(Guid id, bool ativo, Guid idDoSolicitante, CancellationToken ct = default)
    {
        if (!ativo && id == idDoSolicitante)
            return Result.Falha(Erro.Conflito("usuario.autodesativacao", "Você não pode desativar o próprio acesso."));

        var usuario = await usuarioRepository.ObterParaEdicao(id, ct);
        if (usuario is null)
            return Result.Falha(Erro.NaoEncontrado("usuario.nao_encontrado", "Usuário não encontrado."));

        if (usuario.Ativo == ativo)
            return Result.Ok();

        if (!ativo && await EhOUltimoAdministrador(usuario, ct))
            return Result.Falha(UltimoAdministrador);

        usuario.Ativo = ativo;
        usuario.SecurityStamp = Guid.CreateVersion7().ToString();

        if (!ativo)
            await refreshTokenRepository.RevogarTodosDoUsuario(id, DateTime.UtcNow, ct);

        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Usuário {UsuarioId} teve o acesso alterado para ativo={Ativo} por {SolicitanteId}.", id, ativo, idDoSolicitante);

        return Result.Ok();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Três travas, todas contra o mesmo desastre — um sistema sem ninguém capaz de administrá-lo,
    /// cuja recuperação exige acesso direto ao banco:
    /// <list type="number">
    /// <item>perfil desconhecido é recusado, para um erro de digitação não silenciar o acesso;</item>
    /// <item>ninguém remove o próprio perfil de administrador;</item>
    /// <item>o último administrador ativo não pode ser rebaixado.</item>
    /// </list>
    /// </remarks>
    public async Task<Result<UsuarioDetalhe>> AlterarPerfis(Guid id, AlterarPerfis dados, Guid idDoSolicitante, CancellationToken ct = default)
    {
        var solicitados = dados
            .Perfis.Where(perfil => !string.IsNullOrWhiteSpace(perfil))
            .Select(perfil => perfil.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var desconhecidos = solicitados.Where(perfil => !PerfisPadrao.Todos.Contains(perfil, StringComparer.OrdinalIgnoreCase)).ToList();

        if (desconhecidos.Count > 0)
        {
            return Result.Falha<UsuarioDetalhe>(
                Erro.Validacao("usuario.perfil_desconhecido", $"Perfil não reconhecido: {string.Join(", ", desconhecidos)}.", "perfis")
            );
        }

        var usuario = await usuarioRepository.ObterParaEdicao(id, ct);
        if (usuario is null)
            return Erro.NaoEncontrado("usuario.nao_encontrado", "Usuário não encontrado.");

        var atuais = await userManager.GetRolesAsync(usuario);
        var eraAdministrador = atuais.Contains(PerfisPadrao.Administrador, StringComparer.OrdinalIgnoreCase);
        var seraAdministrador = solicitados.Contains(PerfisPadrao.Administrador, StringComparer.OrdinalIgnoreCase);

        if (eraAdministrador && !seraAdministrador)
        {
            if (id == idDoSolicitante)
                return Erro.Conflito("usuario.autorrebaixamento", "Você não pode remover o próprio perfil de administrador.");

            if (await EhOUltimoAdministrador(usuario, ct))
                return Result.Falha<UsuarioDetalhe>(UltimoAdministrador);
        }

        var remover = atuais.Except(solicitados, StringComparer.OrdinalIgnoreCase).ToList();
        var adicionar = solicitados.Except(atuais, StringComparer.OrdinalIgnoreCase).ToList();

        if (remover.Count == 0 && adicionar.Count == 0)
            return await ObterPorId(id, ct);

        if (remover.Count > 0)
            await userManager.RemoveFromRolesAsync(usuario, remover);

        if (adicionar.Count > 0)
            await userManager.AddToRolesAsync(usuario, adicionar);

        logger.LogInformation(
            "Perfis do usuário {UsuarioId} passaram a ser [{Perfis}], alterados por {SolicitanteId}.",
            id,
            string.Join(", ", solicitados),
            idDoSolicitante
        );

        return await ObterPorId(id, ct);
    }

    private async Task<bool> EhOUltimoAdministrador(Usuario usuario, CancellationToken ct)
    {
        if (!usuario.Ativo || !await userManager.IsInRoleAsync(usuario, PerfisPadrao.Administrador))
            return false;

        return await adminRepository.ContarAdministradoresAtivos(ct) <= 1;
    }
}
