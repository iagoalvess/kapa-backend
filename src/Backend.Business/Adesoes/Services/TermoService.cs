using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Adesoes.Models;
using Backend.Business.Cobrancas.Interfaces;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Adesoes.Services;

/// <summary>
/// O termo de adesão da turma: versões publicadas pela comissão e o conteúdo que o formando aceita.
/// </summary>
/// <remarks>
/// Publicar é inserir a versão seguinte, vigente na hora. A corrida — dois presidentes publicando ao
/// mesmo tempo — fica com o índice único <c>(formatura_id, versao)</c>, que recusa a segunda.
/// </remarks>
/// <param name="adesaoRepository">Termos e adesões.</param>
/// <param name="planoRepository">Plano vigente.</param>
/// <param name="validator">Forma do texto.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class TermoService(
    IAdesaoRepository adesaoRepository,
    IPlanoDeCobrancaRepository planoRepository,
    IValidator<PublicarTermo> validator,
    IUnitOfWork unitOfWork,
    ILogger<TermoService> logger
) : ITermoService
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<TermoPublicado>>> Listar(CancellationToken ct = default) =>
        Result.Ok(await adesaoRepository.ListarTermos(ct));

    /// <inheritdoc />
    public async Task<Result<VersaoDoTermo>> Publicar(Guid usuarioId, PublicarTermo dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<VersaoDoTermo>(validacao.Erros);

        var vigente = await adesaoRepository.ObterTermoVigente(ct);
        var conteudo = dados.Conteudo.Trim();

        if (vigente?.Conteudo == conteudo)
            return Erro.Conflito("adesao.termo_sem_mudanca", "Este texto é igual ao da versão vigente.");

        var termo = new TermoDaFormatura
        {
            Versao = (vigente?.Versao ?? 0) + 1,
            Conteudo = conteudo,
            VigenteDesde = DateTime.UtcNow,
            PublicadoPorUsuarioId = usuarioId,
        };

        await adesaoRepository.AdicionarTermo(termo, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Termo de adesão versão {Versao} publicado por {UsuarioId}.", termo.Versao, usuarioId);

        return new VersaoDoTermo(termo.Id, termo.Versao, termo.Conteudo, termo.VigenteDesde);
    }

    /// <inheritdoc />
    /// <remarks>
    /// O plano é congelado agora, como seria no aceite: o hash devolvido é o mesmo que a adesão vai
    /// recalcular. Se o termo ou o plano mudar enquanto o formando lê, os dois deixam de bater.
    /// </remarks>
    public async Task<Result<ConteudoParaAdesao>> ObterParaAdesao(CancellationToken ct = default)
    {
        var termo = await adesaoRepository.ObterTermoVigente(ct);
        var plano = await planoRepository.ObterVigente(ct) is { } vigente ? SnapshotDoPlano.De(vigente) : null;
        var hash = termo is null || plano is null ? null : AdesaoDoFormando.CalcularHash(termo.Conteudo, plano.ParaJson());

        return new ConteudoParaAdesao(termo, plano, hash);
    }
}
