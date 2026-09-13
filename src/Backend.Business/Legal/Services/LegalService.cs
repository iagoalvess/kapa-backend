using Backend.Business.Abstractions;
using Backend.Business.Common.Texto;
using Backend.Business.Legal.Interfaces;
using Backend.Business.Legal.Models;
using FluentValidation;

namespace Backend.Business.Legal.Services;

/// <summary>
/// Documentos legais da plataforma e consentimento auditável.
/// </summary>
/// <param name="legalRepository">Documentos e registros de consentimento.</param>
/// <param name="aceiteValidator">Validador do pedido de aceite.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
public sealed class LegalService(ILegalRepository legalRepository, IValidator<RegistrarAceites> aceiteValidator, IUnitOfWork unitOfWork)
    : ILegalService
{
    /// <summary>Tamanho da coluna de User-Agent; o cabeçalho é escrito pelo cliente e não tem teto.</summary>
    private const int TamanhoMaximoDoUserAgent = 512;

    private static readonly Erro DocumentoNaoEncontrado = Erro.NaoEncontrado("legal.documento_nao_encontrado", "Documento não encontrado.");

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<VersaoDeDocumento>>> ListarVigentes(CancellationToken ct = default) =>
        Result.Ok(await legalRepository.ListarVigentes(DateTime.UtcNow, ct));

    /// <inheritdoc />
    public async Task<Result<VersaoDeDocumento>> ObterVersao(string tipo, string versao, CancellationToken ct = default)
    {
        var oficial = TipoDeDocumento.Normalizar(tipo);

        if (oficial is null)
            return DocumentoNaoEncontrado;

        return await legalRepository.ObterVersao(oficial, versao, ct) is { } documento ? documento : DocumentoNaoEncontrado;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Confere <b>todas</b> as versões antes de marcar qualquer linha: falhar no segundo documento
    /// depois de marcar o primeiro deixaria meio aceite no rastreador, esperando o próximo
    /// <c>SalvarAsync</c> de quem quer que fosse.
    /// </remarks>
    public async Task<Result> RegistrarAceites(
        Guid usuarioId,
        IReadOnlyList<AceiteDeDocumento> aceites,
        OrigemDoAceite origem,
        CancellationToken ct = default
    )
    {
        var validacao = aceiteValidator.Validar(new RegistrarAceites(aceites));
        if (validacao.Falhou)
            return validacao;

        var agora = DateTime.UtcNow;
        var vigentes = await legalRepository.ListarVigentes(agora, ct);
        var aceitos = new List<VersaoDeDocumento>();

        foreach (var aceite in aceites.DistinctBy(a => (TipoDeDocumento.Normalizar(a.Tipo), a.Versao)))
        {
            var conferido = await ConferirVigente(aceite, vigentes, ct);
            if (conferido.Falhou)
                return conferido;

            aceitos.Add(conferido.Valor);
        }

        foreach (var documento in aceitos)
        {
            await legalRepository.Adicionar(
                new ConsentimentoRegistrado
                {
                    UsuarioId = usuarioId,
                    DocumentoLegalId = documento.Id,
                    Versao = documento.Versao,
                    AceitoEm = agora,
                    EnderecoIp = origem.EnderecoIp ?? string.Empty,
                    UserAgent = TextoUtils.Truncar(origem.UserAgent, TamanhoMaximoDoUserAgent) ?? string.Empty,
                },
                ct
            );
        }

        await unitOfWork.SalvarAsync(ct);

        return Result.Ok();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Vale a linha <b>mais recente</b> de cada versão: aceite seguido de revogação é pendência,
    /// e revogação seguida de novo aceite não é.
    /// </remarks>
    public async Task<Result<MeusAceites>> ObterMeusAceites(Guid usuarioId, CancellationToken ct = default)
    {
        var vigentes = await legalRepository.ListarVigentes(DateTime.UtcNow, ct);
        var historico = await legalRepository.ListarConsentimentos(usuarioId, ct);

        var pendencias = vigentes
            .Where(vigente => historico.FirstOrDefault(h => h.Tipo == vigente.Tipo && h.Versao == vigente.Versao) is not { Revogado: false })
            .Select(vigente => new AceitePendente(vigente.Tipo, vigente.Versao))
            .ToList();

        return new MeusAceites(historico, pendencias);
    }

    /// <summary>
    /// Confirma que o aceite aponta para a versão vigente do documento.
    /// </summary>
    /// <remarks>
    /// Versão que existe mas não é a vigente é <c>legal.versao_desatualizada</c>, e não "não
    /// encontrado": é o caso de quem abriu o cadastro antes de uma publicação, e o front o usa
    /// para recarregar os documentos sem perder o que já foi digitado.
    /// </remarks>
    /// <param name="aceite">Versão informada pelo usuário.</param>
    /// <param name="vigentes">Versões vigentes agora.</param>
    /// <param name="ct">Token de cancelamento.</param>
    private async Task<Result<VersaoDeDocumento>> ConferirVigente(
        AceiteDeDocumento aceite,
        IReadOnlyList<VersaoDeDocumento> vigentes,
        CancellationToken ct
    )
    {
        var tipo = TipoDeDocumento.Normalizar(aceite.Tipo);
        var vigente = vigentes.FirstOrDefault(v => v.Tipo == tipo);

        if (tipo is null || vigente is null)
            return DocumentoNaoEncontrado;

        if (vigente.Versao == aceite.Versao)
            return vigente;

        if (await legalRepository.ObterVersao(tipo, aceite.Versao, ct) is null)
            return DocumentoNaoEncontrado;

        return Erro.Conflito("legal.versao_desatualizada", "Este documento foi atualizado. Leia e aceite a versão vigente.");
    }
}
