using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Pagamentos;
using Backend.Api.DTOs.Recebimentos;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
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
/// confere (P3 de 21/09/2026) — escolher por onde entra o dinheiro da turma não é decisão delegável.
/// O Kapa não recebe nem repassa: monta o texto do PIX a partir da chave, mostra o resto como
/// instrução, e não vê o pagamento.
/// </remarks>
/// <param name="contaService">Os meios de recebimento da turma.</param>
/// <param name="pagamentoService">As baixas, de onde saem as divergências.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Pix)]
[Route("api/v{version:apiVersion}/recebimentos")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class RecebimentoController(IContaDeRecebimentoService contaService, IPagamentoService pagamentoService, IUsuarioAtual usuarioAtual)
    : MainController
{
    /// <summary>A conta da turma. Sem meio nenhum cadastrado, <c>conta</c> vem nula.</summary>
    [HttpGet("conta")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [ProducesResponseType(typeof(ContaDeRecebimentoDaTurmaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Obter(CancellationToken ct) =>
        Responder((await contaService.Obter(ct)).Map(conta => conta.Adapt<ContaDeRecebimentoDaTurmaDTO>()));

    /// <summary>Cadastra ou troca os meios que a turma aceita. Qualquer mudança avisa a comissão por e-mail.</summary>
    /// <remarks>
    /// Meio nulo é meio desligado, e o que sobra é o que o formando vê na tela de pagamento. Mexer no
    /// PIX desfaz a conferência do titular; mexer nos demais, não.
    /// <para>
    /// 400 com o motivo no caminho do campo (<c>pix.chave</c>, <c>transferencia.banco</c>) ou
    /// <c>recebimento.sem_meio</c> se nenhum meio vier; 409 <c>recebimento.conta_sem_mudanca</c> se
    /// nada mudou.
    /// </para>
    /// </remarks>
    /// <param name="requisicao">Os meios que a turma passa a aceitar.</param>
    [HttpPut("conta")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(ContaDeRecebimentoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Gravar([FromBody] MeiosDaContaDTO requisicao, CancellationToken ct)
    {
        var resultado = await contaService.Gravar(FormaturaId, usuarioAtual.Id, ParaModelo(requisicao), ct);

        return Responder(resultado.Map(conta => conta.Adapt<ContaDeRecebimentoDTO>()));
    }

    /// <summary>O copia-e-cola de R$ 1,00 para a chave gravada. O QR é desenhado no navegador.</summary>
    /// <remarks>409 <c>recebimento.sem_chave_pix</c> se a turma não aceita PIX: não há chave para testar.</remarks>
    [HttpGet("conta/pix-de-teste")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [ProducesResponseType(typeof(PixDeTesteDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
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

    /// <summary>
    /// O corpo como o domínio o espera: texto ausente vira vazio, para o validator acusar o campo.
    /// </summary>
    /// <remarks>
    /// Na mão, e não pelo Mapster: o nulo de um <b>grupo</b> quer dizer "meio desligado" e precisa
    /// chegar nulo, enquanto o nulo de um <b>campo</b> quer dizer "não preenchido" e precisa chegar
    /// vazio. São duas leituras do mesmo <c>null</c>, e a conversão automática só faria uma delas.
    /// </remarks>
    /// <param name="requisicao">Corpo recebido.</param>
    private static MeiosDaConta ParaModelo(MeiosDaContaDTO requisicao) =>
        new(
            requisicao.Pix is { } pix
                ? new ChavePixDaConta(pix.TipoDeChave, pix.Chave ?? string.Empty, pix.NomeDoTitular ?? string.Empty, pix.Cidade ?? string.Empty)
                : null,
            requisicao.Transferencia is { } conta
                ? new DadosBancarios(
                    conta.Banco ?? string.Empty,
                    conta.Agencia ?? string.Empty,
                    conta.Conta ?? string.Empty,
                    conta.TipoDeConta ?? string.Empty,
                    conta.Titular ?? string.Empty
                )
                : null,
            requisicao.Dinheiro is { } dinheiro ? new DinheiroComAlguem(dinheiro.Nome ?? string.Empty, dinheiro.Onde) : null
        );
}
