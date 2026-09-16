using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Pagamentos;
using Backend.Api.DTOs.Recebimentos;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Recebimentos;

/// <summary>
/// O dinheiro que entra: a conta para onde o formando paga, e as baixas que não fecharam com o
/// valor devido.
/// </summary>
/// <remarks>
/// A tesouraria vê a conta e as divergências; só o Presidente grava, troca, gera o PIX de teste e
/// confere — escolher para qual conta vai o dinheiro da turma não é decisão delegável. O Kapa não
/// recebe nem repassa: monta o texto do PIX a partir da chave e não vê o pagamento.
/// </remarks>
/// <param name="contaService">A conta da turma.</param>
/// <param name="pagamentoService">As baixas, de onde saem as divergências.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/recebimentos")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class RecebimentoController(IContaDeRecebimentoService contaService, IPagamentoService pagamentoService, IUsuarioAtual usuarioAtual)
    : MainController
{
    /// <summary>A conta da turma. Sem chave cadastrada, <c>conta</c> vem ausente.</summary>
    [HttpGet("conta")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [ProducesResponseType(typeof(ContaDeRecebimentoDaTurmaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Obter(CancellationToken ct) =>
        Responder((await contaService.Obter(ct)).Map(conta => conta.Adapt<ContaDeRecebimentoDaTurmaDTO>()));

    /// <summary>Cadastra ou troca a chave. A conta volta a não conferida; a troca avisa a comissão por e-mail.</summary>
    /// <remarks>400 com o motivo por campo (CPF com dígito errado, celular sem DDD); 409 <c>recebimento.conta_sem_mudanca</c> se nada mudou.</remarks>
    /// <param name="requisicao">Chave, titular e cidade.</param>
    [HttpPut("conta")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(ContaDeRecebimentoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Gravar([FromBody] ContaDeRecebimentoRequestDTO requisicao, CancellationToken ct)
    {
        var dados = new DadosDaConta(
            requisicao.TipoDeChave,
            requisicao.Chave ?? string.Empty,
            requisicao.NomeDoTitular ?? string.Empty,
            requisicao.Cidade ?? string.Empty
        );

        var resultado = await contaService.Gravar(FormaturaId, usuarioAtual.Id, dados, ct);

        return Responder(resultado.Map(conta => conta.Adapt<ContaDeRecebimentoDTO>()));
    }

    /// <summary>O copia-e-cola de R$ 1,00 para a chave gravada. O QR é desenhado no navegador.</summary>
    [HttpGet("conta/pix-de-teste")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [ProducesResponseType(typeof(PixDeTesteDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GerarPixDeTeste(CancellationToken ct) =>
        Responder((await contaService.GerarPixDeTeste(ct)).Map(pix => pix.Adapt<PixDeTesteDTO>()));

    /// <summary>Registra que, no PIX de teste, o banco mostrou o titular cadastrado.</summary>
    [HttpPost("conta/conferir")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("recebimento.conta_conferida")]
    [ProducesResponseType(typeof(ContaDeRecebimentoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Conferir(CancellationToken ct) =>
        Responder((await contaService.Conferir(usuarioAtual.Id, ct)).Map(conta => conta.Adapt<ContaDeRecebimentoDTO>()));

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
        var resultado = await pagamentoService.ListarDivergencias(paginacao.ParaModelo(), busca, ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(divergencia => divergencia.Adapt<DivergenciaDTO>())));
    }
}
