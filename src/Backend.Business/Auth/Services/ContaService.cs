using Backend.Business.Abstractions;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Business.Common.Texto;
using Backend.Business.Usuarios.Models;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Auth.Services;

/// <summary>
/// Confirmação de e-mail e ciclo de vida da senha.
/// </summary>
/// <param name="userManager">API do Identity para conta, senha e tokens.</param>
/// <param name="refreshTokenRepository">Sessões abertas, derrubadas quando a senha muda.</param>
/// <param name="emailsDeConta">Montagem e envio das mensagens.</param>
/// <param name="redefinirValidator">Validador da redefinição de senha.</param>
/// <param name="alterarValidator">Validador da troca de senha.</param>
/// <param name="pedidoValidator">Validador dos pedidos por e-mail.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ContaService(
    UserManager<Usuario> userManager,
    IRefreshTokenRepository refreshTokenRepository,
    IEmailsDeConta emailsDeConta,
    IValidator<RedefinirSenha> redefinirValidator,
    IValidator<AlterarSenha> alterarValidator,
    IValidator<PedidoPorEmail> pedidoValidator,
    IUnitOfWork unitOfWork,
    ILogger<ContaService> logger
) : IContaService
{
    private static readonly Erro LinkInvalido = Erro.Validacao("conta.link_invalido", "Este link é inválido ou expirou. Solicite um novo.", "token");

    /// <inheritdoc />
    public async Task<Result> SolicitarRedefinicaoDeSenha(PedidoPorEmail dados, CancellationToken ct = default)
    {
        var validacao = pedidoValidator.Validar(dados);
        if (validacao.Falhou)
            return validacao;

        var mascarado = TextoUtils.MascararEmail(dados.Email);
        var usuario = await userManager.FindByEmailAsync(dados.Email.Trim());

        if (usuario is null || !usuario.Ativo)
        {
            logger.LogInformation("Redefinição de senha pedida para {Email}: conta inexistente ou inativa. Nenhum e-mail enviado.", mascarado);

            return Result.Ok();
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(usuario);

        await emailsDeConta.EnfileirarRedefinicaoDeSenha(usuario, token, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("E-mail de redefinição de senha enfileirado para {Email}.", mascarado);

        return Result.Ok();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Derruba todas as sessões em caso de sucesso. Se a redefinição aconteceu porque a conta foi
    /// comprometida, deixar os refresh tokens do atacante vivos anularia a troca de senha — ele
    /// continuaria renovando o acesso indefinidamente.
    /// </remarks>
    public async Task<Result> RedefinirSenha(RedefinirSenha dados, CancellationToken ct = default)
    {
        var validacao = redefinirValidator.Validar(dados);
        if (validacao.Falhou)
            return validacao;

        var token = CodificadorDeToken.Decodificar(dados.Token);
        if (token is null)
            return Result.Falha(LinkInvalido);

        var usuario = await userManager.FindByEmailAsync(dados.Email.Trim());
        if (usuario is null || !usuario.Ativo)
            return Result.Falha(LinkInvalido);

        var redefinicao = await userManager.ResetPasswordAsync(usuario, token, dados.NovaSenha);
        if (!redefinicao.Succeeded)
            return Result.Falha(TraduzirFalha(redefinicao));

        await EncerrarSessoesEAvisar(usuario, ct);

        logger.LogInformation("Senha redefinida para o usuário {UsuarioId}. Sessões encerradas.", usuario.Id);

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<Result> ConfirmarEmail(ConfirmarEmail dados, CancellationToken ct = default)
    {
        var token = CodificadorDeToken.Decodificar(dados.Token);
        if (token is null || string.IsNullOrWhiteSpace(dados.Email))
            return Result.Falha(LinkInvalido);

        var usuario = await userManager.FindByEmailAsync(dados.Email.Trim());
        if (usuario is null)
            return Result.Falha(LinkInvalido);

        if (usuario.EmailConfirmed)
            return Result.Ok();

        var confirmacao = await userManager.ConfirmEmailAsync(usuario, token);
        if (!confirmacao.Succeeded)
            return Result.Falha(LinkInvalido);

        logger.LogInformation("E-mail confirmado para o usuário {UsuarioId}.", usuario.Id);

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<Result> ReenviarConfirmacao(PedidoPorEmail dados, CancellationToken ct = default)
    {
        var validacao = pedidoValidator.Validar(dados);
        if (validacao.Falhou)
            return validacao;

        var usuario = await userManager.FindByEmailAsync(dados.Email.Trim());

        if (usuario is null || usuario.EmailConfirmed || !usuario.Ativo)
            return Result.Ok();

        var token = await userManager.GenerateEmailConfirmationTokenAsync(usuario);

        await emailsDeConta.EnfileirarConfirmacao(usuario, token, ct);
        await unitOfWork.SalvarAsync(ct);

        return Result.Ok();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Exige a senha atual mesmo com o usuário autenticado: um access token esquecido numa máquina
    /// aberta não deve bastar para trocar a senha e tomar a conta.
    /// <para>
    /// Errar a senha atual conta para o bloqueio por tentativas, como no login — senão este
    /// endpoint seria o caminho sem lockout para adivinhar a senha de quem deixou a sessão aberta.
    /// Só a senha errada conta: senha nova fraca é erro do próprio dono e não pode bloqueá-lo.
    /// </para>
    /// </remarks>
    public async Task<Result> AlterarSenha(Guid usuarioId, AlterarSenha dados, CancellationToken ct = default)
    {
        var validacao = alterarValidator.Validar(dados);
        if (validacao.Falhou)
            return validacao;

        var usuario = await userManager.FindByIdAsync(usuarioId.ToString());
        if (usuario is null)
            return Result.Falha(Erro.NaoEncontrado("usuario.nao_encontrado", "Usuário não encontrado."));

        var troca = await userManager.ChangePasswordAsync(usuario, dados.SenhaAtual, dados.NovaSenha);

        if (!troca.Succeeded)
        {
            if (troca.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
                await userManager.AccessFailedAsync(usuario);

            logger.LogInformation("Troca de senha recusada para o usuário {UsuarioId}.", usuarioId);

            return Result.Falha(TraduzirFalha(troca));
        }

        await EncerrarSessoesEAvisar(usuario, ct);

        logger.LogInformation("Senha alterada pelo próprio usuário {UsuarioId}. Sessões encerradas.", usuarioId);

        return Result.Ok();
    }

    private async Task EncerrarSessoesEAvisar(Usuario usuario, CancellationToken ct)
    {
        await refreshTokenRepository.RevogarTodosDoUsuario(usuario.Id, DateTime.UtcNow, ct);
        await emailsDeConta.EnfileirarAvisoDeSenhaAlterada(usuario, ct);
        await unitOfWork.SalvarAsync(ct);
    }

    /// <summary>
    /// Traduz a falha do Identity, distinguindo token inválido de senha fraca.
    /// </summary>
    /// <remarks>
    /// São dois problemas com soluções opostas: token inválido significa "peça um link novo";
    /// senha fraca significa "escolha outra senha". Devolver a mesma mensagem para os dois deixa
    /// o usuário tentando a coisa errada.
    /// </remarks>
    private static IReadOnlyList<Erro> TraduzirFalha(IdentityResult resultado)
    {
        if (resultado.Errors.Any(e => e.Code.Contains("Token", StringComparison.Ordinal)))
            return [LinkInvalido];

        return [.. resultado.Errors.Select(erro => Erro.Validacao($"identity.{erro.Code}", erro.Description, "novaSenha"))];
    }
}
