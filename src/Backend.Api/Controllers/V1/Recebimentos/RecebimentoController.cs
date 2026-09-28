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
/// <param name="tesourariaService">As baixas, de onde saem as divergências.</param>
/// <param name="pagamentoService">O recibo de cada baixa.</param>
/// <param name="provedorService">O Mercado Pago da turma (Sprint 25).</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Pix)]
[Route("api/v{version:apiVersion}/recebimentos")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class RecebimentoController(
    IContaDeRecebimentoService contaService,
    ITesourariaService tesourariaService,
    IPagamentoService pagamentoService,
    IProvedorDaTurmaService provedorService,
    IUsuarioAtual usuarioAtual
) : MainController
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

    /// <summary>O Mercado Pago da turma: a conta conectada, quando e por quem. Nunca o token.</summary>
    [HttpGet("conta/mercado-pago")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [ProducesResponseType(typeof(ProvedorDaTurmaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ObterMercadoPago(CancellationToken ct) =>
        Responder((await provedorService.Obter(ct)).Map(provedor => provedor.Adapt<ProvedorDaTurmaDTO>()));

    /// <summary>
    /// Começa a conexão: devolve a página do Mercado Pago onde o presidente autoriza o Kapa na conta da turma.
    /// </summary>
    /// <remarks>
    /// O front manda o navegador para a <c>url</c>; o Mercado Pago o devolve ao retorno da API, que grava a
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

    /// <summary>Desconecta o Mercado Pago. O PIX do Mercado Pago sai da tela; os outros meios continuam. Avisa a comissão.</summary>
    /// <remarks>404 <c>recebimento.provedor_nao_conectado</c> se não houver conexão.</remarks>
    [HttpDelete("conta/mercado-pago")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DesconectarMercadoPago(CancellationToken ct) =>
        Responder(await provedorService.Desconectar(FormaturaId, usuarioAtual.Id, ct));

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
        Arquivo(await pagamentoService.ObterRecibo(FormaturaId, usuarioAtual.Id, id, ct), inline: true);
}
