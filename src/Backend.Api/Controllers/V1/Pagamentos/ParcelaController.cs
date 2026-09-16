using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Pagamentos;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Pagamentos.Services;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Pagamentos;

/// <summary>
/// Uma parcela e o que se faz com ela: consultar, gerar o PIX, avisar que pagou, baixar na mão e
/// estornar a baixa.
/// </summary>
/// <remarks>
/// Consulta, PIX e "já paguei" entram pela política de membro: "é o dono, ou a gestão" é regra do
/// service, e parcela de outro formando responde 404, não 403. Baixar na mão é da Tesouraria;
/// estornar, só do Presidente. Toda escrita exige a turma ativa.
/// </remarks>
/// <param name="pagamentoService">Regras do pagamento.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/parcelas")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class ParcelaController(IPagamentoService pagamentoService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Uma parcela, com o valor de hoje. O dono, ou a gestão; para os demais, 404.</summary>
    /// <param name="id">Parcela.</param>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(ParcelaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterParcela(Guid id, CancellationToken ct) =>
        Responder((await pagamentoService.ObterParcela(FormaturaId, usuarioAtual.Id, id, ct)).Map(parcela => parcela.Adapt<ParcelaDTO>()));

    /// <summary>O PIX da parcela: a chave vigente, o valor de hoje e o identificador. O dono, ou a tesouraria.</summary>
    /// <remarks>409 <c>pagamento.sem_conta</c> se a turma ainda não cadastrou a chave; <c>pagamento.parcela_paga</c> se já está paga.</remarks>
    /// <param name="id">Parcela.</param>
    [HttpGet("{id:guid}/pix")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(PixDaParcelaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GerarPix(Guid id, CancellationToken ct) =>
        Responder((await pagamentoService.GerarPix(FormaturaId, usuarioAtual.Id, id, ct)).Map(pix => pix.Adapt<PixDaParcelaDTO>()));

    /// <summary>O "já paguei": avisa a tesouraria. A parcela não muda até a tesouraria conferir.</summary>
    /// <remarks>
    /// Multipart, com o comprovante opcional. 409 <c>pagamento.informe_pendente</c> se já há aviso esperando,
    /// <c>pagamento.parcela_paga</c> se a parcela já foi paga.
    /// </remarks>
    /// <param name="id">Parcela.</param>
    /// <param name="pagoEm">Dia do pagamento, <c>aaaa-mm-dd</c>.</param>
    /// <param name="valorEmCentavos">Valor pago, em centavos.</param>
    /// <param name="comprovante">PDF ou imagem, opcional.</param>
    [HttpPost("{id:guid}/informes")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [Consumes("multipart/form-data")]
    [RegistrarEvento("pagamento.informado", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(ParcelaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Informar(
        Guid id,
        [FromForm] DateOnly pagoEm,
        [FromForm] long valorEmCentavos,
        IFormFile? comprovante,
        CancellationToken ct
    )
    {
        await using var conteudo = comprovante?.OpenReadStream() ?? Stream.Null;

        var resultado = await pagamentoService.Informar(
            FormaturaId,
            usuarioAtual.Id,
            id,
            new NovoInforme(pagoEm, valorEmCentavos),
            Comprovante(comprovante, conteudo),
            ct
        );

        return Responder(resultado.Map(parcela => parcela.Adapt<ParcelaDTO>()));
    }

    /// <summary>Baixa a parcela sem informe — dinheiro, TED, quem pagou e não avisou. Grava autor, IP e hora.</summary>
    /// <remarks>409 <c>pagamento.informe_pendente</c> se o formando já avisou: confirme ou recuse o aviso.</remarks>
    /// <param name="id">Parcela.</param>
    /// <param name="forma"><c>Pix</c>, <c>Dinheiro</c>, <c>Transferencia</c> ou <c>Outro</c>.</param>
    /// <param name="pagoEm">Dia em que o dinheiro entrou.</param>
    /// <param name="valorEmCentavos">Quanto entrou, em centavos.</param>
    /// <param name="comprovante">PDF ou imagem, opcional.</param>
    [HttpPost("{id:guid}/baixa-manual")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [Consumes("multipart/form-data")]
    [RegistrarEvento("pagamento.baixa_manual", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(ParcelaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> BaixarManualmente(
        Guid id,
        [FromForm] FormaDePagamento forma,
        [FromForm] DateOnly pagoEm,
        [FromForm] long valorEmCentavos,
        IFormFile? comprovante,
        CancellationToken ct
    )
    {
        await using var conteudo = comprovante?.OpenReadStream() ?? Stream.Null;

        var resultado = await pagamentoService.BaixarManualmente(
            FormaturaId,
            usuarioAtual.Id,
            usuarioAtual.EnderecoIp,
            id,
            new BaixaManual(forma, pagoEm, valorEmCentavos),
            Comprovante(comprovante, conteudo),
            ct
        );

        return Responder(resultado.Map(parcela => parcela.Adapt<ParcelaDTO>()));
    }

    /// <summary>Desfaz a baixa, com justificativa. Só o Presidente. O recebimento fica, marcado como estornado.</summary>
    /// <param name="id">Parcela.</param>
    /// <param name="requisicao">Justificativa.</param>
    [HttpPost("{id:guid}/estornar-baixa")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("pagamento.estorno", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(ParcelaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Estornar(Guid id, [FromBody] EstornarBaixaRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await pagamentoService.Estornar(
            FormaturaId,
            usuarioAtual.Id,
            usuarioAtual.EnderecoIp,
            id,
            new EstornarBaixa(requisicao.Justificativa ?? string.Empty),
            ct
        );

        return Responder(resultado.Map(parcela => parcela.Adapt<ParcelaDTO>()));
    }

    /// <summary>O comprovante do multipart como pedido de envio; ausente ou vazio, nenhum.</summary>
    private static NovoArquivo? Comprovante(IFormFile? arquivo, Stream conteudo) =>
        arquivo is { Length: > 0 } ? new NovoArquivo(arquivo.FileName, arquivo.Length, conteudo, PagamentoService.CategoriaDoComprovante) : null;
}
