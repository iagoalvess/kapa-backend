using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Pagamentos;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Pagamentos;

/// <summary>
/// Uma parcela e o que se faz com ela: consultar, gerar a cobrança, avisar que pagou, baixar na mão e
/// estornar a baixa.
/// </summary>
/// <remarks>
/// Consulta, cobrança e "já paguei" entram pela política de membro: "é o dono, ou a gestão" é regra do
/// service, e parcela de outro formando responde 404, não 403. Baixar na mão é da Tesouraria;
/// estornar, só do Presidente. Toda escrita exige a turma ativa.
/// </remarks>
/// <param name="pagamentoService">Regras do pagamento.</param>
/// <param name="tesourariaService">Baixa manual e estorno.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Cobrancas)]
[Route("api/v{version:apiVersion}/parcelas")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class ParcelaController(IPagamentoService pagamentoService, ITesourariaService tesourariaService, IUsuarioAtual usuarioAtual)
    : MainController
{
    /// <summary>Uma parcela, com o valor de hoje. O dono, ou a gestão; para os demais, 404.</summary>
    /// <param name="id">Parcela.</param>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Politicas.TitularDoProprioHistorico)]
    [ProducesResponseType(typeof(ParcelaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterParcela(Guid id, CancellationToken ct) =>
        Responder((await pagamentoService.ObterParcela(FormaturaId, usuarioAtual.Id, id, ct)).Map(parcela => parcela.Adapt<ParcelaDTO>()));

    /// <summary>A cobrança da parcela: o valor de hoje e os meios que a turma aceita. O dono, ou a tesouraria.</summary>
    /// <remarks>
    /// Cada meio vem com o que a tela precisa mostrar — o PIX com o BR Code, a transferência com os
    /// dados bancários, o dinheiro com quem procurar. Com um meio só a tela não tem seletor.
    /// <para>
    /// 409 <c>pagamento.sem_conta</c> se a turma ainda não habilitou meio nenhum;
    /// <c>pagamento.parcela_paga</c> se já está paga.
    /// </para>
    /// </remarks>
    /// <param name="id">Parcela.</param>
    [HttpGet("{id:guid}/cobranca")]
    [Authorize(Policy = Politicas.TitularDoProprioHistorico)]
    [ProducesResponseType(typeof(CobrancaDaParcelaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GerarCobranca(Guid id, CancellationToken ct) =>
        Responder(
            (await pagamentoService.GerarCobranca(FormaturaId, usuarioAtual.Id, id, ct)).Map(cobranca => cobranca.Adapt<CobrancaDaParcelaDTO>())
        );

    /// <summary>A cobrança de várias parcelas: a soma do que elas cobram hoje, pelos mesmos meios. Só o dono.</summary>
    /// <remarks>
    /// É o passo 1 do "paguei vários meses de uma vez"; o passo 2 é <c>POST /parcelas/informes</c>, com a
    /// mesma lista. 400 <c>pagamento.parcelas_do_informe</c> fora de 1 a 24 parcelas; 409
    /// <c>pagamento.sem_conta</c>, <c>pagamento.informe_pendente</c> ou <c>pagamento.parcela_paga</c> se
    /// qualquer uma delas não aceitar o pagamento.
    /// </remarks>
    /// <param name="parcelaIds">Parcelas que o pagamento vai cobrir.</param>
    [HttpGet("cobranca")]
    [Authorize(Policy = Politicas.TitularDoProprioHistorico)]
    [ProducesResponseType(typeof(CobrancaDaParcelaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GerarCobrancaDeVarias([FromQuery] IReadOnlyList<Guid> parcelaIds, CancellationToken ct) =>
        Responder(
            (await pagamentoService.GerarCobrancaDeVarias(FormaturaId, usuarioAtual.Id, parcelaIds ?? [], ct)).Map(cobranca =>
                cobranca.Adapt<CobrancaDaParcelaDTO>()
            )
        );

    /// <summary>O "já paguei" de uma parcela: avisa a tesouraria. A parcela não muda até ela conferir.</summary>
    /// <remarks>
    /// Multipart, com o comprovante opcional. 409 <c>pagamento.informe_pendente</c> se já há aviso esperando,
    /// <c>pagamento.parcela_paga</c> se a parcela já foi paga.
    /// <para>Um PIX que cobriu vários meses vai em <c>POST /parcelas/informes</c>, com a lista.</para>
    /// </remarks>
    /// <param name="id">Parcela.</param>
    /// <param name="pagoEm">Dia do pagamento, <c>aaaa-mm-dd</c>.</param>
    /// <param name="valorEmCentavos">Valor pago, em centavos.</param>
    /// <param name="meio">Como pagou: <c>Pix</c>, <c>Transferencia</c>, <c>Dinheiro</c> ou <c>Outro</c>.</param>
    /// <param name="comprovante">PDF ou imagem, opcional em qualquer meio.</param>
    [HttpPost("{id:guid}/informes")]
    [Authorize(Policy = Politicas.TitularDoProprioHistorico)]
    [Authorize(Policy = Politicas.ExigeFormaturaRecebendo)]
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
        [FromForm] MeioDeRecebimento meio,
        IFormFile? comprovante,
        CancellationToken ct
    )
    {
        await using var conteudo = comprovante?.OpenReadStream() ?? Stream.Null;

        var resultado = await pagamentoService.Informar(
            FormaturaId,
            usuarioAtual.Id,
            [id],
            new NovoInforme(pagoEm, valorEmCentavos, meio),
            comprovante.ParaNovoArquivo(conteudo),
            ct
        );

        return Responder(resultado.Map(parcelas => parcelas[0].Adapt<ParcelaDTO>()));
    }

    /// <summary>
    /// O "já paguei" de um PIX que cobriu várias parcelas — os meses atrasados de uma vez.
    /// </summary>
    /// <remarks>
    /// Multipart, com o comprovante opcional e compartilhado pelas parcelas: é um pagamento só. O
    /// valor é distribuído da parcela mais antiga para a mais nova, cada uma até o que ela cobra, e o
    /// que sobrar fica na última — a tesouraria vê a sobra em Divergências.
    /// <para>
    /// 400 <c>pagamento.parcelas_do_informe</c> fora de 1 a 24 parcelas; 409
    /// <c>pagamento.informe_pendente</c> ou <c>pagamento.parcela_paga</c> se qualquer uma delas não
    /// aceitar o aviso — nada é gravado.
    /// </para>
    /// </remarks>
    /// <param name="parcelaIds">Parcelas cobertas pelo pagamento.</param>
    /// <param name="pagoEm">Dia do pagamento, <c>aaaa-mm-dd</c>.</param>
    /// <param name="valorEmCentavos">Valor total pago, em centavos.</param>
    /// <param name="meio">Como pagou: <c>Pix</c>, <c>Transferencia</c>, <c>Dinheiro</c> ou <c>Outro</c>.</param>
    /// <param name="comprovante">PDF ou imagem, opcional em qualquer meio.</param>
    [HttpPost("informes")]
    [Authorize(Policy = Politicas.TitularDoProprioHistorico)]
    [Authorize(Policy = Politicas.ExigeFormaturaRecebendo)]
    [Consumes("multipart/form-data")]
    [RegistrarEvento("pagamento.informado")]
    [ProducesResponseType(typeof(IReadOnlyList<ParcelaDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> InformarVarias(
        [FromForm] IReadOnlyList<Guid> parcelaIds,
        [FromForm] DateOnly pagoEm,
        [FromForm] long valorEmCentavos,
        [FromForm] MeioDeRecebimento meio,
        IFormFile? comprovante,
        CancellationToken ct
    )
    {
        await using var conteudo = comprovante?.OpenReadStream() ?? Stream.Null;

        var resultado = await pagamentoService.Informar(
            FormaturaId,
            usuarioAtual.Id,
            parcelaIds ?? [],
            new NovoInforme(pagoEm, valorEmCentavos, meio),
            comprovante.ParaNovoArquivo(conteudo),
            ct
        );

        return Responder(resultado.Map(parcelas => parcelas.Adapt<List<ParcelaDTO>>()));
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
    [Authorize(Policy = Politicas.ExigeFormaturaRecebendo)]
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

        var resultado = await tesourariaService.BaixarManualmente(
            FormaturaId,
            usuarioAtual.Id,
            usuarioAtual.EnderecoIp,
            id,
            new BaixaManual(forma, pagoEm, valorEmCentavos),
            comprovante.ParaNovoArquivo(conteudo),
            ct
        );

        return Responder(resultado.Map(parcela => parcela.Adapt<ParcelaDTO>()));
    }

    /// <summary>Desfaz a baixa, com justificativa. Só o Presidente. O recebimento fica, marcado como estornado.</summary>
    /// <param name="id">Parcela.</param>
    /// <param name="requisicao">Justificativa.</param>
    [HttpPost("{id:guid}/estornar-baixa")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaRecebendo)]
    [RegistrarEvento("pagamento.estorno", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(ParcelaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Estornar(Guid id, [FromBody] EstornarBaixaRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await tesourariaService.Estornar(
            FormaturaId,
            usuarioAtual.Id,
            usuarioAtual.EnderecoIp,
            id,
            new EstornarBaixa(requisicao.Justificativa ?? string.Empty),
            ct
        );

        return Responder(resultado.Map(parcela => parcela.Adapt<ParcelaDTO>()));
    }
}
