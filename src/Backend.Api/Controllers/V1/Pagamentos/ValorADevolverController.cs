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
/// A lista "a devolver" da tesouraria (Sprint 42): crédito de pedido cancelado, parcial de parcela cancelada e
/// pagamento do Mercado Pago sem parcela.
/// </summary>
/// <remarks>
/// O Kapa não devolve dinheiro (decisão 1): a comissão devolve fora e registra aqui. Tudo é da Tesouraria; registrar
/// passa na turma suspensa, como a baixa — é escrituração do caixa da própria turma.
/// </remarks>
/// <param name="servico">A lista e o que tira dela.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Cobrancas)]
[Route("api/v{version:apiVersion}/valores-a-devolver")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class ValorADevolverController(IValoresADevolverService servico, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Os que esperam a comissão, dos mais antigos; ou os já resolvidos, dos mais recentes.</summary>
    /// <param name="paginacao">Página e tamanho; o teto é aplicado no servidor.</param>
    /// <param name="resolvidos">Os devolvidos e fechados, em vez dos que esperam.</param>
    /// <param name="busca">Trecho do nome do formando.</param>
    [HttpGet]
    [Authorize(Policy = Politicas.Tesouraria)]
    [ProducesResponseType(typeof(PaginaDTO<ValorADevolverDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(
        [FromQuery] PaginacaoRequestDTO paginacao,
        [FromQuery] bool resolvidos,
        [FromQuery] string? busca,
        CancellationToken ct
    )
    {
        var resultado = await servico.Listar(paginacao.ParaModelo(), new FiltroDeValoresADevolver(resolvidos, busca), ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(valor => valor.Adapt<ValorADevolverDTO>())));
    }

    /// <summary>A comissão fez o PIX de volta: com o comprovante, sai da lista e a saída entra no caixa como despesa paga.</summary>
    /// <remarks>
    /// Multipart. Sem comprovante, 400 <c>pagamento.comprovante_obrigatorio</c>; fora da lista, 409
    /// <c>pagamento.valor_nao_a_devolver</c>; pago sem parcela, 409 <c>pagamento.pago_sem_parcela_nao_devolve</c> — ele se
    /// fecha em <c>/fechar</c>.
    /// </remarks>
    /// <param name="id">Valor a devolver.</param>
    /// <param name="comprovante">PDF ou imagem; obrigatório.</param>
    [HttpPost("{id:guid}/devolucao")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaRecebendo)]
    [Consumes("multipart/form-data")]
    [RegistrarEvento("pagamento.devolucao_registrada", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(ValorADevolverDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Devolver(Guid id, IFormFile? comprovante, CancellationToken ct)
    {
        await using var conteudo = comprovante?.OpenReadStream() ?? Stream.Null;

        var resultado = await servico.Devolver(usuarioAtual.Id, id, comprovante.ParaNovoArquivo(conteudo), ct);

        return Responder(resultado.Map(valor => valor.Adapt<ValorADevolverDTO>()));
    }

    /// <summary>
    /// A comissão resolveu o pago sem parcela — devolveu no painel do Mercado Pago ou lançou como outra receita —, e diz
    /// o que fez.
    /// </summary>
    /// <remarks>Crédito e parcial não fecham assim: 409 <c>pagamento.devolucao_exige_comprovante</c>.</remarks>
    /// <param name="id">Valor a devolver.</param>
    /// <param name="requisicao">O que foi feito.</param>
    [HttpPost("{id:guid}/fechar")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaRecebendo)]
    [RegistrarEvento("pagamento.pago_sem_parcela_fechado", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(ValorADevolverDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Fechar(Guid id, [FromBody] FecharValorADevolverRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await servico.Fechar(usuarioAtual.Id, id, new FecharValorADevolver(requisicao.Observacao ?? string.Empty), ct);

        return Responder(resultado.Map(valor => valor.Adapt<ValorADevolverDTO>()));
    }
}
