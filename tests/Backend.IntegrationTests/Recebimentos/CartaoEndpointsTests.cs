using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Backend.Api.DTOs.Pagamentos;
using Backend.Api.DTOs.Recebimentos;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.MercadoPago.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Backend.IntegrationTests.Recebimentos;

/// <summary>
/// A Sprint 39 de ponta a ponta nas parcelas, com o Mercado Pago falso no lugar da rede: ligar o cartão, pagar
/// várias parcelas parcelado com a taxa repassada, a tarifa no caixa, a contestação que estorna — e as portas.
/// </summary>
[Collection(ColecaoDeApi.Nome)]
public sealed class CartaoEndpointsTests(ApiFactory fabrica)
{
    private const string Conta = "/api/v1/recebimentos/conta/mercado-pago";

    private const string Aviso = "/api/v1/webhooks/cobranca/mercadopago";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// P2, P3 e P6: a turma liga o cartão repassando 5%, o formando paga três parcelas de uma vez em 3×, as três
    /// baixam como cartão pelo valor do dia, o acréscimo vira receita e a tarifa vira despesa paga.
    /// </summary>
    [Fact]
    public async Task Formando_paga_varias_parcelas_no_cartao_e_o_caixa_fecha_com_o_extrato()
    {
        // Arrange
        var falso = new MercadoPagoFalso { Tarifa = 498 };
        await using var api = falso.Na(fabrica);
        var (turma, presidente, aluno) = await Conectada(api);
        (await presidente.PutAsJsonAsync($"{Conta}/cartao", new ConfiguracaoDoCartaoRequestDTO(true, 500), Json, Ct)).EnsureSuccessStatusCode();
        var extrato = await Ler<ExtratoDTO>(await aluno.GetAsync("/api/v1/extrato/eu", Ct));
        var tres = extrato.Parcelas.Take(3).Select(p => p.Id).ToList();

        // Act — a cobrança das três, e o cartão com o valor que ela mostrou
        var cobranca = await Ler<CobrancaDaParcelaDTO>(
            await aluno.GetAsync($"/api/v1/parcelas/cobranca?{string.Join('&', tres.Select(id => $"parcela_ids={id}"))}", Ct)
        );
        var cartao = cobranca.PeloMercadoPago.Single(m => m.Meio == MeioDePagamento.Cartao).Cartao!;
        var pago = await Ler<PagamentoNoCartaoDTO>(
            await aluno.PostAsJsonAsync(
                "/api/v1/parcelas/cartao",
                new PagamentoNoCartaoRequestDTO(tres, "tok-visa", "visa", 3, cartao.ValorEmCentavos),
                Json,
                Ct
            )
        );

        // Assert — o que a tela mostrou
        cartao.ChavePublica.ShouldBe(MercadoPagoFalso.ChavePublica);
        cartao.ValorEmCentavos.ShouldBe((long)Math.Ceiling(cobranca.ValorEmCentavos / 0.95m));
        cartao.MaximoDeParcelas.ShouldBe(12);
        pago.Situacao.ShouldBe(SituacaoDoCartao.Pago);

        // Assert — as três pagas como cartão pelo valor do dia, o acréscimo como receita, a tarifa como despesa
        await using var contexto = fabrica.ContextoDe(turma);
        (await contexto.Parcelas.Where(p => tres.Contains(p.Id)).Select(p => p.Status).ToListAsync(Ct)).ShouldAllBe(s => s == StatusDaParcela.Paga);
        var recebimentos = await contexto.Recebimentos.Where(r => tres.Contains(r.ParcelaId)).ToListAsync(Ct);
        recebimentos.ShouldAllBe(r => r.Forma == FormaDePagamento.Cartao);
        recebimentos.Sum(r => r.ValorEmCentavos).ShouldBe(cobranca.ValorEmCentavos);
        var acrescimo = await contexto.OutrasReceitas.SingleAsync(Ct);
        acrescimo.ValorEmCentavos.ShouldBe(cartao.AcrescimoEmCentavos);
        var tarifa = await contexto.Despesas.SingleAsync(Ct);
        tarifa.Status.ShouldBe(StatusDaDespesa.Paga);
        tarifa.Categoria.ShouldBe(CategoriaDeDespesa.Taxas);
        tarifa.ValorEmCentavos.ShouldBe(cartao.ValorEmCentavos * 498 / 10_000);
    }

    /// <summary>P4: a contestação no cartão chega pelo aviso, estorna as baixas, reabre as parcelas e avisa a comissão.</summary>
    [Fact]
    public async Task Contestacao_no_cartao_estorna_a_baixa_e_avisa_a_comissao()
    {
        // Arrange — a parcela paga no cartão
        var falso = new MercadoPagoFalso();
        await using var api = falso.Na(fabrica);
        var (turma, presidente, aluno) = await Conectada(api);
        (await presidente.PutAsJsonAsync($"{Conta}/cartao", new ConfiguracaoDoCartaoRequestDTO(true, 500), Json, Ct)).EnsureSuccessStatusCode();
        var parcela = (await Ler<ExtratoDTO>(await aluno.GetAsync("/api/v1/extrato/eu", Ct))).Proxima!.Id;
        var cobranca = await Ler<CobrancaDaParcelaDTO>(await aluno.GetAsync($"/api/v1/parcelas/{parcela}/cobranca", Ct));
        var cartao = cobranca.PeloMercadoPago.Single(m => m.Meio == MeioDePagamento.Cartao).Cartao!;
        await Ler<PagamentoNoCartaoDTO>(
            await aluno.PostAsJsonAsync(
                "/api/v1/parcelas/cartao",
                new PagamentoNoCartaoRequestDTO([parcela], "tok-visa", "visa", 1, cartao.ValorEmCentavos),
                Json,
                Ct
            )
        );
        string pedido;
        await using (var antes = fabrica.ContextoDe(turma))
            pedido = (await antes.CobrancasBancarias.SingleAsync(c => c.Meio == MeioDePagamento.Cartao, Ct)).IdExterno!;

        // Act — o pagador contesta, e o Mercado Pago avisa
        falso.Devolver(pedido, SituacaoDoPedido.Contestado);
        var entregue = await Avisar(Cliente(api, null), pedido);

        // Assert
        entregue.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var contexto = fabrica.ContextoDe(turma);
        (await contexto.Parcelas.SingleAsync(p => p.Id == parcela, Ct)).Status.ShouldNotBe(StatusDaParcela.Paga);
        var baixa = await contexto.Recebimentos.SingleAsync(r => r.ParcelaId == parcela, Ct);
        baixa.EstornadoEm.ShouldNotBeNull();
        baixa.JustificativaDoEstorno.ShouldBe("contestação no cartão");
        (await contexto.CobrancasBancarias.SingleAsync(c => c.Meio == MeioDePagamento.Cartao, Ct)).Status.ShouldBe(
            StatusDaCobrancaBancaria.Estornada
        );
        (await contexto.EmailsFila.AnyAsync(e => e.Assunto.Contains("estornado pelo Mercado Pago"), Ct)).ShouldBeTrue();
        (await contexto.OutrasReceitas.SumAsync(r => r.ValorEmCentavos, Ct)).ShouldBe(0);

        // Act — o aviso repetido não estorna de novo
        (await Avisar(Cliente(api, null), pedido)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await contexto.Recebimentos.CountAsync(r => r.ParcelaId == parcela, Ct)).ShouldBe(1);
    }

    /// <summary>P7: com o cartão desligado, a opção não aparece e o pagamento é recusado antes de cobrar.</summary>
    [Fact]
    public async Task Cartao_desligado_nao_aparece_e_nao_cobra()
    {
        var falso = new MercadoPagoFalso();
        await using var api = falso.Na(fabrica);
        var (_, _, aluno) = await Conectada(api);
        var parcela = (await Ler<ExtratoDTO>(await aluno.GetAsync("/api/v1/extrato/eu", Ct))).Proxima!.Id;

        var cobranca = await Ler<CobrancaDaParcelaDTO>(await aluno.GetAsync($"/api/v1/parcelas/{parcela}/cobranca", Ct));
        var pago = await aluno.PostAsJsonAsync(
            "/api/v1/parcelas/cartao",
            new PagamentoNoCartaoRequestDTO([parcela], "tok-visa", "visa", 1, cobranca.ValorEmCentavos),
            Json,
            Ct
        );

        cobranca.PeloMercadoPago.ShouldAllBe(m => m.Meio != MeioDePagamento.Cartao);
        await ProblemaCom(pago, HttpStatusCode.Conflict, "pagamento.cartao_desligado");
    }

    /// <summary>O cartão recusado não baixa nada, e o valor que mudou não chega a cobrar.</summary>
    [Fact]
    public async Task Cartao_recusado_e_valor_que_mudou_nao_baixam()
    {
        var falso = new MercadoPagoFalso();
        await using var api = falso.Na(fabrica);
        var (turma, presidente, aluno) = await Conectada(api);
        (await presidente.PutAsJsonAsync($"{Conta}/cartao", new ConfiguracaoDoCartaoRequestDTO(true, null), Json, Ct)).EnsureSuccessStatusCode();
        var parcela = (await Ler<ExtratoDTO>(await aluno.GetAsync("/api/v1/extrato/eu", Ct))).Proxima!.Id;
        var cobranca = await Ler<CobrancaDaParcelaDTO>(await aluno.GetAsync($"/api/v1/parcelas/{parcela}/cobranca", Ct));

        var recusado = await aluno.PostAsJsonAsync(
            "/api/v1/parcelas/cartao",
            new PagamentoNoCartaoRequestDTO([parcela], MercadoPagoFalso.CartaoRecusado, "visa", 1, cobranca.ValorEmCentavos),
            Json,
            Ct
        );
        var mudou = await aluno.PostAsJsonAsync(
            "/api/v1/parcelas/cartao",
            new PagamentoNoCartaoRequestDTO([parcela], "tok-visa", "visa", 1, cobranca.ValorEmCentavos - 1),
            Json,
            Ct
        );

        await ProblemaCom(recusado, HttpStatusCode.Conflict, "pagamento.cartao_recusado");
        await ProblemaCom(mudou, HttpStatusCode.Conflict, "pagamento.valor_mudou");
        await using var contexto = fabrica.ContextoDe(turma);
        (await contexto.Parcelas.SingleAsync(p => p.Id == parcela, Ct)).Status.ShouldNotBe(StatusDaParcela.Paga);
        (
            await contexto.CobrancasBancarias.AnyAsync(c => c.Meio == MeioDePagamento.Cartao && c.Status == StatusDaCobrancaBancaria.Emitida, Ct)
        ).ShouldBeFalse();
    }

    /// <summary>
    /// P7: Tesouraria e Presidente ligam o cartão e trocam o modo de cobrança; Comissão e Formando não. A Tesouraria
    /// passa da política e cai no 404 da turma sem Mercado Pago — é a prova de que a porta é dela.
    /// </summary>
    [Theory]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.NotFound)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task So_a_tesouraria_liga_o_cartao(string papel, HttpStatusCode esperado)
    {
        await using var api = new MercadoPagoFalso().Na(fabrica);
        var membro = Cliente(api, (await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), papel, Ct)).Cliente);

        var resposta = await membro.PutAsJsonAsync($"{Conta}/cartao", new ConfiguracaoDoCartaoRequestDTO(true, null), Json, Ct);
        var modo = await membro.PutAsJsonAsync($"{Conta}/cobranca", new ModoDeCobrancaRequestDTO(true), Json, Ct);

        resposta.StatusCode.ShouldBe(esperado);
        modo.StatusCode.ShouldBe(esperado);
    }

    /// <summary>A taxa repassada fica entre 0,01% e 15%; o ligado aparece na conexão com quem ligou.</summary>
    [Fact]
    public async Task Taxa_fora_do_limite_e_recusada_e_a_conexao_mostra_quem_ligou()
    {
        await using var api = new MercadoPagoFalso().Na(fabrica);
        var (_, presidente, _) = await Conectada(api);

        var alta = await presidente.PutAsJsonAsync($"{Conta}/cartao", new ConfiguracaoDoCartaoRequestDTO(true, 2000), Json, Ct);
        var ligado = await Ler<ProvedorDaTurmaDTO>(
            await presidente.PutAsJsonAsync($"{Conta}/cartao", new ConfiguracaoDoCartaoRequestDTO(true, 498), Json, Ct)
        );

        alta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ligado.Provedor!.Cartao.LigadoEm.ShouldNotBeNull();
        ligado.Provedor.Cartao.LigadoPor.ShouldNotBeNullOrWhiteSpace();
        ligado.Provedor.Cartao.TaxaRepassada.ShouldBe(498);
        ligado.Provedor.Cartao.Disponivel.ShouldBeTrue();
    }

    /// <summary>Uma turma com plano, Mercado Pago conectado e um formando com adesão.</summary>
    private async Task<(Guid Turma, HttpClient Presidente, HttpClient Aluno)> Conectada(WebApplicationFactory<Program> api)
    {
        var turma = await fabrica.TurmaComPlano();
        var formando = await fabrica.FormandoComAdesao(turma.FormaturaId);
        var presidente = Cliente(api, turma.Presidente.Cliente);
        var anonimo = Cliente(api, null);

        await presidente.CadastrarChavePix(fabrica, Ct);
        var state = HttpUtility.ParseQueryString(new Uri(await presidente.UrlDeAutorizacao(fabrica, Ct)).Query)["state"];
        var retorno = await anonimo.GetAsync($"/api/v1/mercado-pago/retorno?code=codigo&state={Uri.EscapeDataString(state!)}", Ct);
        retorno.Headers.Location!.ToString().ShouldContain("mercado_pago=conectado");
        (await presidente.PutAsJsonAsync($"{Conta}/cobranca", new ModoDeCobrancaRequestDTO(true), Json, Ct)).EnsureSuccessStatusCode();

        return (turma.FormaturaId, presidente, Cliente(api, formando.Cliente));
    }

    private static Task<HttpResponseMessage> Avisar(HttpClient anonimo, string idDoPedido)
    {
        var aviso = new HttpRequestMessage(HttpMethod.Post, $"{Aviso}?data.id={idDoPedido.ToLowerInvariant()}&type=order");
        aviso.Headers.Add("x-signature", MercadoPagoFalso.AssinaturaValida);
        aviso.Headers.Add("x-request-id", "req-1");

        return anonimo.SendAsync(aviso, Ct);
    }

    private static async Task ProblemaCom(HttpResponseMessage resposta, HttpStatusCode status, string codigo)
    {
        var corpo = await resposta.Content.ReadAsStringAsync(Ct);
        resposta.StatusCode.ShouldBe(status, corpo);
        corpo.ShouldContain(codigo);
    }

    private static HttpClient Cliente(WebApplicationFactory<Program> api, HttpClient? sessao)
    {
        var cliente = api.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false }
        );
        cliente.DefaultRequestHeaders.Authorization = sessao?.DefaultRequestHeaders.Authorization;

        return cliente;
    }

    private static async Task<T> Ler<T>(HttpResponseMessage resposta)
    {
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync(Ct));

        return (await resposta.Content.ReadFromJsonAsync<T>(Json, Ct))!;
    }
}
