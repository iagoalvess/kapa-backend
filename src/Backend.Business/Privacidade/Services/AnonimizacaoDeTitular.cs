using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Privacidade.Interfaces;
using Backend.Business.Usuarios.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Privacidade.Services;

/// <summary>Limpeza cadastral executada internamente após análise do pedido pelo suporte.</summary>
/// <remarks>
/// Encerra o acesso e limpa conta, perfis, foto e respostas às enquetes. Preserva vínculos,
/// lançamentos e provas de adesão/aceite. Como esses registros permitem reassociação,
/// não há promessa de anonimização integral nem de eliminação de todos os dados.
/// O suporte deve avaliar separadamente arquivos, compras, registros técnicos e fornecedores.
/// </remarks>
/// <param name="privacidadeRepository">Cadastros do titular em todas as turmas.</param>
/// <param name="userManager">A conta do Identity.</param>
/// <param name="arquivoService">Onde a foto mora.</param>
/// <param name="eventos">Trilha de auditoria.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class AnonimizacaoDeTitular(
    IPrivacidadeRepository privacidadeRepository,
    UserManager<Usuario> userManager,
    IArquivoService arquivoService,
    IEventoRepository eventos,
    IUnitOfWork unitOfWork,
    ILogger<AnonimizacaoDeTitular> logger
) : IAnonimizacaoDeTitular
{
    /// <summary>Domínio reservado que nunca resolve (RFC 2606), para o e-mail anonimizado.</summary>
    /// <remarks>
    /// O Identity exige e-mail único, então não dá para deixá-lo vazio. Um domínio reservado garante
    /// que a caixa não existe em lugar nenhum — e que ninguém registre depois o endereço de alguém
    /// que pediu para ser esquecido.
    /// </remarks>
    private const string DominioAnonimo = "anonimizado.invalid";

    /// <summary>
    /// O marcador que substitui o nome nos lançamentos.
    /// </summary>
    /// <remarks>
    /// Derivado do id, e não de um contador: um contador por turma exigiria coluna nova e daria
    /// números diferentes à mesma pessoa em duas turmas, o que é o oposto do que a decisão 2 pede.
    /// Seis dígitos hexadecimais bastam para a assembleia distinguir duas pessoas na mesma lista, e
    /// não voltam ao id — o id inteiro continua na chave estrangeira, que é o que amarra o dinheiro.
    /// </remarks>
    /// <param name="usuarioId">Titular.</param>
    public static string Marcador(Guid usuarioId) => $"Formando #{usuarioId.ToString("N")[..6]}";

    /// <inheritdoc />
    public async Task<Result<string>> Anonimizar(Guid usuarioId, CancellationToken ct = default)
    {
        var usuario = await userManager.FindByIdAsync(usuarioId.ToString());

        if (usuario is null)
            return Erro.NaoEncontrado("privacidade.titular_nao_encontrado", "Titular não encontrado.");

        var marcador = Marcador(usuarioId);

        if (usuario.AnonimizadoEm is not null)
            return Result.Ok(marcador);

        var emailOriginal = usuario.Email;

        await AnonimizarCadastros(usuarioId, ct);

        usuario.Nome = marcador;
        usuario.Email = $"{usuarioId:N}@{DominioAnonimo}";
        usuario.NormalizedEmail = userManager.NormalizeEmail(usuario.Email);
        usuario.UserName = usuario.Email;
        usuario.NormalizedUserName = userManager.NormalizeName(usuario.UserName);
        usuario.PhoneNumber = null;
        usuario.PhoneNumberConfirmed = false;
        usuario.EmailConfirmed = false;
        usuario.Ativo = false;
        usuario.ReceberComunicacaoDoKapa = false;
        usuario.PasswordHash = null;
        usuario.AnonimizadoEm = DateTime.UtcNow;

        await userManager.UpdateSecurityStampAsync(usuario);

        var atualizacao = await userManager.UpdateAsync(usuario);

        if (!atualizacao.Succeeded)
            return Erro.Conflito("privacidade.anonimizacao_recusada", "Não foi possível anonimizar a conta. Tente de novo.");

        await eventos.Auditar(
            NomesDeAuditoria.TitularAnonimizado,
            usuarioId,
            new
            {
                titularUsuarioId = usuarioId,
                marcador,
                emailOriginalMascarado = Mascarar(emailOriginal),
            },
            ct
        );

        await unitOfWork.SalvarAsync(ct);

        logger.LogWarning("Titular {UsuarioId} anonimizado por pedido de eliminação; passou a ser {Marcador}.", usuarioId, marcador);

        return Result.Ok(marcador);
    }

    /// <summary>Esvazia os cadastros de turma e apaga os bytes das fotos.</summary>
    /// <remarks>
    /// Os bytes primeiro, a coluna depois: o contrário deixaria a foto no provedor sem ninguém que
    /// a referencie, e ela não seria apagada nunca mais.
    /// </remarks>
    /// <param name="usuarioId">Titular.</param>
    private async Task AnonimizarCadastros(Guid usuarioId, CancellationToken ct)
    {
        var respostas = await privacidadeRepository.ListarRespostasParaAnonimizarDeTodasAsFormaturas(usuarioId, ct);
        foreach (var resposta in respostas)
            resposta.Anonimizar();

        var perfis = await privacidadeRepository.ListarPerfisParaAnonimizarDeTodasAsFormaturas(usuarioId, ct);

        foreach (var perfil in perfis)
        {
            if (perfil.Anonimizar() is not { } foto)
                continue;

            var remocao = await arquivoService.Remover(foto, new SolicitanteDeArquivo(usuarioId, PeloSistema: true), ct);

            if (remocao.Falhou)
                throw new IOException(
                    $"Não foi possível remover a foto {foto}: {remocao.PrimeiroErro.Codigo}. Repita a operação após corrigir o armazenamento."
                );
        }
    }

    /// <summary>
    /// O e-mail original reduzido ao que não identifica: <c>j***@g***</c>.
    /// </summary>
    /// <remarks>
    /// Guardar o e-mail inteiro no evento desfaria a anonimização com um <c>SELECT</c>. Guardar nada
    /// tira da trilha a única pista de que a conta certa foi atendida — a mascarada é o meio-termo
    /// que serve para conferir e não serve para encontrar.
    /// </remarks>
    /// <param name="email">E-mail antes da anonimização.</param>
    private static string Mascarar(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.IndexOf('@', StringComparison.Ordinal) is var arroba && arroba <= 0)
            return "***";

        var dominio = email[(arroba + 1)..];

        return $"{email[0]}***@{(dominio.Length > 0 ? dominio[0] : '*')}***";
    }
}
