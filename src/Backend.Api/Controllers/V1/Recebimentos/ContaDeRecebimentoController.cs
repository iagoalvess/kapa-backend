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
/// A conta para onde o formando paga: os meios que a turma aceita, o PIX de teste e a conferência do titular.
/// </summary>
/// <remarks>
/// A tesouraria vê a conta; só o Presidente grava, troca, gera o PIX de teste e confere (P3 de 21/09/2026) —
/// escolher por onde entra o dinheiro da turma não é decisão delegável. O Kapa não recebe nem repassa: monta o
/// texto do PIX a partir da chave, mostra o resto como instrução, e não vê o pagamento.
/// </remarks>
/// <param name="contaService">Os meios de recebimento da turma.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Pix)]
[Route("api/v{version:apiVersion}/recebimentos")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class ContaDeRecebimentoController(IContaDeRecebimentoService contaService, IUsuarioAtual usuarioAtual) : MainController
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
    /// Mudar o PIX ou a transferência só pede: <c>confirmacao_enviada_para</c> vem preenchido, a conta segue como
    /// estava, e a troca vale quando o presidente abrir o link do e-mail (<c>POST conta/confirmar</c>). Mudar só o
    /// dinheiro vale na hora, e o campo vem nulo.
    /// </para>
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
    [ProducesResponseType(typeof(GravacaoDaContaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Gravar([FromBody] MeiosDaContaDTO requisicao, CancellationToken ct)
    {
        var resultado = await contaService.Gravar(FormaturaId, usuarioAtual.Id, ParaModelo(requisicao), ct);

        return Responder(resultado.Map(gravacao => gravacao.Adapt<GravacaoDaContaDTO>()));
    }

    /// <summary>Aplica a troca dos meios pedida no <c>PUT</c>, pelo link que chegou ao e-mail de quem pediu.</summary>
    /// <remarks>
    /// 400 <c>recebimento.confirmacao_invalida</c> se o link venceu, já foi usado, é de outra pessoa ou a conta mudou
    /// depois do pedido.
    /// </remarks>
    /// <param name="requisicao">O token do link.</param>
    [HttpPost("conta/confirmar")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("recebimento.troca_confirmada")]
    [ProducesResponseType(typeof(ContaDeRecebimentoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirmar([FromBody] ConfirmacaoPorEmailDTO requisicao, CancellationToken ct) =>
        Responder(
            (await contaService.Confirmar(FormaturaId, usuarioAtual.Id, requisicao.Token, ct)).Map(conta => conta.Adapt<ContaDeRecebimentoDTO>())
        );

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
