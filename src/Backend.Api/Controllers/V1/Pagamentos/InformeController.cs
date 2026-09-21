using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Pagamentos;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Pagamentos;

/// <summary>
/// A fila da tesouraria: os avisos de "já paguei" esperando conferência, e o que se decide sobre
/// cada um — confirmar em lote ou recusar com motivo.
/// </summary>
/// <remarks>
/// Tudo aqui é da Tesouraria: o aviso é do formando, mas conferi-lo é o ato que move dinheiro no
/// caixa da turma. Escrita exige a turma ativa.
/// </remarks>
/// <param name="pagamentoService">Regras do pagamento.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Pix)]
[Route("api/v{version:apiVersion}/informes")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class InformeController(IPagamentoService pagamentoService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>A fila da conferência: pendentes do mais antigo ao mais novo, com o devido de cada um.</summary>
    /// <param name="paginacao">Página e tamanho; o teto é aplicado no servidor.</param>
    /// <param name="filtro">Situação, o recorte do dia, o período do pagamento informado e a busca.</param>
    [HttpGet]
    [Authorize(Policy = Politicas.Tesouraria)]
    [ProducesResponseType(typeof(PaginaDTO<InformeDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListarInformes(
        [FromQuery] PaginacaoRequestDTO paginacao,
        [FromQuery] FiltroDeInformes filtro,
        CancellationToken ct = default
    )
    {
        var resultado = await pagamentoService.ListarInformes(paginacao.ParaModelo(), filtro, ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(informe => informe.Adapt<InformeDTO>())));
    }

    /// <summary>O comprovante de um informe.</summary>
    /// <remarks>Não usa os helpers do <c>MainController</c>: o sucesso é o arquivo. Vai <c>inline</c>, para abrir numa aba.</remarks>
    /// <param name="id">Informe.</param>
    [HttpGet("{id:guid}/comprovante")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BaixarComprovante(Guid id, CancellationToken ct)
    {
        var resultado = await pagamentoService.BaixarComprovante(id, ct);

        if (resultado.Falhou)
            return Responder(resultado.Map(_ => 0));

        return File(resultado.Valor.Conteudo, resultado.Valor.ContentType);
    }

    /// <summary>Confirma o lote numa transação: cada informe baixa a parcela com o valor recebido.</summary>
    /// <remarks>Informe já conferido é ignorado, não erro — é o clique duplo. Grava autor, IP e hora.</remarks>
    /// <param name="requisicao">Informes e valores recebidos.</param>
    [HttpPost("confirmar")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaRecebendo)]
    [RegistrarEvento("pagamento.lote_confirmado")]
    [ProducesResponseType(typeof(ResultadoDaConferenciaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Confirmar([FromBody] ConfirmarInformesRequestDTO requisicao, CancellationToken ct)
    {
        var lote = new ConfirmarInformes(requisicao.Itens?.Adapt<List<ConfirmacaoDeInforme>>() ?? []);

        var resultado = await pagamentoService.Confirmar(FormaturaId, usuarioAtual.Id, usuarioAtual.EnderecoIp, lote, ct);

        return Responder(resultado.Map(conferencia => conferencia.Adapt<ResultadoDaConferenciaDTO>()));
    }

    /// <summary>Recusa um informe, com motivo, e avisa o formando por e-mail. A parcela continua aberta.</summary>
    /// <param name="id">Informe.</param>
    /// <param name="requisicao">Motivo.</param>
    [HttpPost("{id:guid}/recusar")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaRecebendo)]
    [RegistrarEvento("pagamento.informe_recusado", CamposDaRota = ["id"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Recusar(Guid id, [FromBody] RecusarInformeRequestDTO requisicao, CancellationToken ct) =>
        Responder(await pagamentoService.Recusar(FormaturaId, usuarioAtual.Id, id, new RecusarInforme(requisicao.Motivo ?? string.Empty), ct));
}
