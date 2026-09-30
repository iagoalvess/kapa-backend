using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Pagamentos;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Pagamentos.Interfaces;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Pagamentos;

/// <summary>
/// O dinheiro que já entrou: as baixas que não fecharam com o valor devido, e o recibo de cada uma.
/// </summary>
/// <remarks>
/// Divide o prefixo <c>recebimentos</c> com a conta da turma, mas o assunto é a baixa, não a conta — por isso
/// mora em Pagamentos.
/// </remarks>
/// <param name="tesourariaService">As baixas, de onde saem as divergências.</param>
/// <param name="reciboService">O recibo de cada baixa.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Pix)]
[Route("api/v{version:apiVersion}/recebimentos")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class RecebimentoController(ITesourariaService tesourariaService, IReciboService reciboService, IUsuarioAtual usuarioAtual)
    : MainController
{
    /// <summary>As baixas com valor recebido diferente do devido, das mais recentes.</summary>
    /// <remarks>
    /// Mora aqui, e não junto das parcelas: a divergência é uma propriedade do dinheiro que entrou —
    /// alguém pagou a mais ou a menos — e é conferindo o extrato do banco que a tesouraria a resolve.
    /// </remarks>
    /// <param name="paginacao">Página e tamanho; o teto é aplicado no servidor.</param>
    /// <param name="busca">Trecho do nome do formando.</param>
    [HttpGet("divergencias")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [ProducesResponseType(typeof(PaginaDTO<DivergenciaDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListarDivergencias([FromQuery] PaginacaoRequestDTO paginacao, [FromQuery] string? busca, CancellationToken ct)
    {
        var resultado = await tesourariaService.ListarDivergencias(paginacao.ParaModelo(), busca, ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(divergencia => divergencia.Adapt<DivergenciaDTO>())));
    }

    /// <summary>O recibo de uma baixa em PDF, gerado na hora. O próprio formando, ou a gestão; para os demais, 404.</summary>
    /// <remarks>
    /// Um recibo por recebimento, e o número dele é o id do recebimento (Sprint 22). O mesmo recebimento
    /// dá sempre o mesmo arquivo. A gestão vê o CPF mascarado; o formando, inteiro. 409
    /// <c>pagamento.recebimento_estornado</c> se a baixa foi desfeita.
    /// <para>Aceita o desligado: o recibo é a prova do que ele pagou, e não some com a saída.</para>
    /// </remarks>
    /// <param name="id">Recebimento.</param>
    [HttpGet("{id:guid}/recibo")]
    [Authorize(Policy = Politicas.TitularDoProprioHistorico)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> BaixarRecibo(Guid id, CancellationToken ct) =>
        Arquivo(await reciboService.Obter(FormaturaId, usuarioAtual.Id, id, ct), inline: true);
}
