using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Recebimentos;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Recebimentos;

/// <summary>
/// O Mercado Pago da turma (Sprint 25): a conexão por OAuth, o cartão (Sprint 39) e o modo de cobrança.
/// </summary>
/// <remarks>
/// A tesouraria vê a conexão e configura o cartão e o modo de cobrança; só o Presidente conecta e desconecta. O
/// retorno da autorização, anônimo, fica no <see cref="RetornoDoMercadoPagoController"/>.
/// </remarks>
/// <param name="provedorService">O Mercado Pago da turma.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Pix)]
[Route("api/v{version:apiVersion}/recebimentos")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class ProvedorDaTurmaController(IProvedorDaTurmaService provedorService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>O Mercado Pago da turma: a conta conectada, quando e por quem. Nunca o token.</summary>
    [HttpGet("conta/mercado-pago")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [ProducesResponseType(typeof(ProvedorDaTurmaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ObterMercadoPago(CancellationToken ct) =>
        Responder((await provedorService.Obter(ct)).Map(provedor => provedor.Adapt<ProvedorDaTurmaDTO>()));

    /// <summary>
    /// Começa a conexão: manda ao e-mail do presidente o link da página do Mercado Pago onde ele autoriza o Kapa na
    /// conta da turma, e devolve o e-mail mascarado.
    /// </summary>
    /// <remarks>
    /// O link vai por e-mail para a sessão sozinha não bastar (revisão de 05/10/2026); o Mercado Pago o devolve ao retorno da API, que grava a
    /// conexão e volta para a tela da turma. Conectar de novo troca a conta. 409
    /// <c>recebimento.provedor_desligado</c> se a aplicação do Kapa não estiver configurada; 409
    /// <c>recebimento.chave_pix_obrigatoria</c> se a turma ainda não tem chave PIX (P7).
    /// </remarks>
    [HttpPost("conta/mercado-pago/autorizacao")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(AutorizacaoDoProvedorDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AutorizarMercadoPago(CancellationToken ct) =>
        Responder(
            (await provedorService.IniciarConexao(FormaturaId, usuarioAtual.Id, ct)).Map(autorizacao => autorizacao.Adapt<AutorizacaoDoProvedorDTO>())
        );

    /// <summary>Liga ou desliga o cartão da turma, com a taxa repassada ou absorvida (Sprint 39, P2 e P7).</summary>
    /// <remarks>
    /// Tesouraria e Presidente (P7). 404 <c>recebimento.provedor_nao_conectado</c> sem conexão; 409
    /// <c>recebimento.reconectar_para_cartao</c> quando a conexão é anterior ao cartão; 400 com a taxa fora de 1 a 1500.
    /// </remarks>
    /// <param name="requisicao">Ligado e a taxa repassada.</param>
    [HttpPut("conta/mercado-pago/cartao")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("recebimento.cartao_configurado")]
    [ProducesResponseType(typeof(ProvedorDaTurmaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfigurarCartao([FromBody] ConfiguracaoDoCartaoRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            (
                await provedorService.ConfigurarCartao(
                    FormaturaId,
                    usuarioAtual.Id,
                    new ConfiguracaoDoCartao(requisicao.Ligado ?? false, requisicao.TaxaRepassada),
                    ct
                )
            ).Map(provedor => provedor.Adapt<ProvedorDaTurmaDTO>())
        );

    /// <summary>Troca entre a cobrança manual e a automática pelo Mercado Pago — parcelas e opcionais; a loja é sempre Mercado Pago.</summary>
    /// <remarks>
    /// Tesouraria e Presidente, como o cartão. 404 <c>recebimento.provedor_nao_conectado</c> sem conexão; 409
    /// <c>recebimento.avisos_pendentes</c> ou <c>recebimento.pix_em_aberto</c> enquanto há algo no meio do caminho.
    /// </remarks>
    /// <param name="requisicao">O modo novo.</param>
    [HttpPut("conta/mercado-pago/cobranca")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("recebimento.modo_de_cobranca")]
    [ProducesResponseType(typeof(ProvedorDaTurmaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfigurarCobranca([FromBody] ModoDeCobrancaRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            (await provedorService.ConfigurarCobranca(FormaturaId, usuarioAtual.Id, new ModoDeCobranca(requisicao.Automatica ?? false), ct)).Map(
                provedor => provedor.Adapt<ProvedorDaTurmaDTO>()
            )
        );

    /// <summary>Desconecta o Mercado Pago. O PIX do Mercado Pago sai da tela; os outros meios continuam. Avisa a comissão.</summary>
    /// <remarks>
    /// 404 <c>recebimento.provedor_nao_conectado</c> se não houver conexão; 409 <c>recebimento.cobranca_automatica_ligada</c>
    /// enquanto a turma cobra pelo Mercado Pago, e 409 <c>recebimento.loja_aberta</c> com item à venda na loja pública.
    /// </remarks>
    [HttpDelete("conta/mercado-pago")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DesconectarMercadoPago(CancellationToken ct) =>
        Responder(await provedorService.Desconectar(FormaturaId, usuarioAtual.Id, ct));
}
