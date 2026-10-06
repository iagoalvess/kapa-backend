using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Backend.Api.DTOs.Pagamentos;
using Backend.Api.DTOs.Recebimentos;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Backend.IntegrationTests.Recebimentos;

/// <summary>
/// A Sprint 25 de ponta a ponta, com o Mercado Pago falso no lugar da rede: conectar por OAuth, o PIX
/// dinâmico na tela do formando, o aviso que baixa a parcela — e as portas que não se abrem.
/// </summary>
[Collection(ColecaoDeApi.Nome)]
public sealed class MercadoPagoEndpointsTests(ApiFactory fabrica)
{
    private const string Conta = "/api/v1/recebimentos/conta/mercado-pago";

    private const string Aviso = "/api/v1/webhooks/cobranca/mercadopago";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Conectar, pagar e ver a parcela paga sem ninguém da tesouraria: o caminho inteiro da Parte A e da C.
    /// O token nunca volta pela API.
    /// </summary>
    [Fact]
    public async Task Conectar_emitir_e_o_aviso_baixa_a_parcela_sozinho()
    {
        // Arrange
        var falso = new MercadoPagoFalso();
        await using var api = falso.Na(fabrica);
        var turma = await fabrica.TurmaComPlano();
        var formando = await fabrica.FormandoComAdesao(turma.FormaturaId);
        var presidente = Cliente(api, turma.Presidente.Cliente);
        var anonimo = Cliente(api, null);

        // Act — conectar (a chave PIX vem antes: é o chão da P7)
        await presidente.CadastrarChavePix(fabrica, Ct);
        var state = HttpUtility.ParseQueryString(new Uri(await presidente.UrlDeAutorizacao(fabrica, Ct)).Query)["state"];
        var retorno = await anonimo.GetAsync($"/api/v1/mercado-pago/retorno?code=codigo-do-oauth&state={Uri.EscapeDataString(state!)}", Ct);

        // Assert — conectado, e o token fica no banco
        retorno.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        retorno.Headers.Location!.ToString().ShouldContain("mercado_pago=conectado");
        var lida = await presidente.GetAsync(Conta, Ct);
        var corpo = await lida.Content.ReadAsStringAsync(Ct);
        corpo.ShouldNotContain(MercadoPagoFalso.AccessToken);
        JsonSerializer.Deserialize<ProvedorDaTurmaDTO>(corpo, Json)!.Provedor!.ContaNoProvedor.ShouldBe(MercadoPagoFalso.Conta);

        // Act — a turma passa a cobrar pelo Mercado Pago, e o formando abre a parcela
        await LigarCobrancaAutomatica(presidente);
        var aluno = Cliente(api, formando.Cliente);
        var extrato = await Ler<ExtratoDTO>(await aluno.GetAsync("/api/v1/extrato/eu", Ct));
        var parcela = extrato.Proxima!.Id;
        var cobranca = await Ler<CobrancaDaParcelaDTO>(await aluno.GetAsync($"/api/v1/parcelas/{parcela}/cobranca", Ct));
        var denovo = await Ler<CobrancaDaParcelaDTO>(await aluno.GetAsync($"/api/v1/parcelas/{parcela}/cobranca", Ct));

        // Assert — o PIX do Mercado Pago vem à parte dos meios da comissão, e a segunda leitura reaproveita o mesmo
        var dinamico = cobranca.PeloMercadoPago.ShouldHaveSingleItem();
        dinamico.Meio.ShouldBe(MeioDePagamento.Pix);
        denovo.PeloMercadoPago[0].Pix!.CopiaECola.ShouldBe(dinamico.Pix!.CopiaECola);
        var pedido = falso.Pedidos.ShouldHaveSingleItem();
        pedido.Value.Valor.ShouldBe(cobranca.ValorEmCentavos);

        // Act — o formando paga, e o Mercado Pago avisa
        falso.Pagar(pedido.Key);
        var aviso = new HttpRequestMessage(HttpMethod.Post, $"{Aviso}?data.id={pedido.Key.ToLowerInvariant()}&type=order");
        aviso.Headers.Add("x-signature", MercadoPagoFalso.AssinaturaValida);
        aviso.Headers.Add("x-request-id", "req-1");
        var entregue = await anonimo.SendAsync(aviso, Ct);

        // Assert — a parcela está paga, sem aviso do formando nem conferência
        entregue.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        (await contexto.Parcelas.SingleAsync(p => p.Id == parcela, Ct)).Status.ShouldBe(StatusDaParcela.Paga);
        (await contexto.CobrancasBancarias.SingleAsync(Ct)).Status.ShouldBe(StatusDaCobrancaBancaria.Paga);
        (await contexto.Recebimentos.SingleAsync(r => r.ParcelaId == parcela, Ct)).ValorEmCentavos.ShouldBe(cobranca.ValorEmCentavos);
    }

    /// <summary>
    /// Sprint 35: o recebedor é um só. O aviso da conta do Kapa (<c>user_id</c> = <c>MercadoPago:ContaDoKapa</c>)
    /// vai para os planos e não toca a cobrança da turma; o mesmo pedido avisado pela conta da turma baixa a parcela.
    /// </summary>
    [Fact]
    public async Task O_aviso_vai_para_o_fluxo_da_conta_que_o_mandou()
    {
        // Arrange — a turma conectada, o PIX emitido e pago
        var falso = new MercadoPagoFalso();
        await using var api = falso.Na(fabrica);
        var turma = await fabrica.TurmaComPlano();
        var formando = await fabrica.FormandoComAdesao(turma.FormaturaId);
        var anonimo = Cliente(api, null);
        var presidente = Cliente(api, turma.Presidente.Cliente);
        await Conectar(presidente, anonimo);
        await LigarCobrancaAutomatica(presidente);
        var aluno = Cliente(api, formando.Cliente);
        var parcela = (await Ler<ExtratoDTO>(await aluno.GetAsync("/api/v1/extrato/eu", Ct))).Proxima!.Id;
        var cobranca = await Ler<CobrancaDaParcelaDTO>(await aluno.GetAsync($"/api/v1/parcelas/{parcela}/cobranca", Ct));
        var pedido = falso.Pedidos.ShouldHaveSingleItem().Key;
        falso.Pagar(pedido);

        // Act — o aviso chega como se fosse da conta do Kapa
        var doKapa = await Avisar(anonimo, pedido, MercadoPagoFalso.ContaDoKapa);

        // Assert — respondido, e nada da turma foi consultado nem baixado
        doKapa.StatusCode.ShouldBe(HttpStatusCode.OK);
        falso.Consultados.ShouldBeEmpty();
        await using (var antes = fabrica.ContextoDe(turma.FormaturaId))
            (await antes.Parcelas.SingleAsync(p => p.Id == parcela, Ct)).Status.ShouldNotBe(StatusDaParcela.Paga);

        // Act — o mesmo pedido, avisado pela conta da turma
        var daTurma = await Avisar(anonimo, pedido, 42);

        // Assert — a parcela baixa como PIX
        daTurma.StatusCode.ShouldBe(HttpStatusCode.OK);
        cobranca.PeloMercadoPago.ShouldHaveSingleItem().Meio.ShouldBe(MeioDePagamento.Pix);
        await using var depois = fabrica.ContextoDe(turma.FormaturaId);
        (await depois.Parcelas.SingleAsync(p => p.Id == parcela, Ct)).Status.ShouldBe(StatusDaParcela.Paga);
        (await depois.Recebimentos.SingleAsync(r => r.ParcelaId == parcela, Ct)).Forma.ShouldBe(FormaDePagamento.Pix);
    }

    /// <summary>Decisão 12: aviso sem a assinatura do Mercado Pago é recusado antes de qualquer coisa.</summary>
    [Fact]
    public async Task Aviso_com_assinatura_invalida_e_401()
    {
        await using var api = new MercadoPagoFalso().Na(fabrica);
        var aviso = new HttpRequestMessage(HttpMethod.Post, $"{Aviso}?data.id=ord1&type=order");
        aviso.Headers.Add("x-signature", "ts=1,v1=forjada");

        var resposta = await Cliente(api, null).SendAsync(aviso, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>O retorno sem o <c>state</c> que o Kapa assinou não conecta conta nenhuma.</summary>
    [Fact]
    public async Task Retorno_com_state_forjado_volta_com_erro_e_nao_conecta()
    {
        await using var api = new MercadoPagoFalso().Na(fabrica);
        var turma = await fabrica.TurmaComPlano();

        var retorno = await Cliente(api, null).GetAsync("/api/v1/mercado-pago/retorno?code=x&state=forjado.forjado", Ct);

        retorno.Headers.Location!.ToString().ShouldContain("mercado_pago=erro");
        retorno.Headers.Location!.ToString().ShouldContain("codigo=recebimento.retorno_invalido");
        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        (await contexto.CredenciaisDeProvedor.AnyAsync(Ct)).ShouldBeFalse();
    }

    /// <summary>P3: só o Presidente conecta ou desconecta; a tesouraria vê a conta conectada.</summary>
    [Theory]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.OK, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    public async Task So_o_presidente_conecta_e_a_tesouraria_le(string papel, HttpStatusCode leitura, HttpStatusCode escrita)
    {
        await using var api = new MercadoPagoFalso().Na(fabrica);
        var membro = Cliente(api, (await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), papel, Ct)).Cliente);

        (await membro.GetAsync(Conta, Ct)).StatusCode.ShouldBe(leitura);
        (await membro.PostAsync($"{Conta}/autorizacao", null, Ct)).StatusCode.ShouldBe(escrita);
        (await membro.DeleteAsync(Conta, Ct)).StatusCode.ShouldBe(escrita);
    }

    /// <summary>P7: sem chave PIX, a turma não conecta — seria cobrança sem o PIX estático de reserva.</summary>
    [Fact]
    public async Task Conectar_sem_chave_pix_e_recusado()
    {
        await using var api = new MercadoPagoFalso().Na(fabrica);
        var turma = await fabrica.TurmaComPlano();
        var presidente = Cliente(api, turma.Presidente.Cliente);

        var resposta = await presidente.PostAsync($"{Conta}/autorizacao", null, Ct);

        await ProblemaCom(resposta, "recebimento.chave_pix_obrigatoria");
    }

    /// <summary>P7: com o Mercado Pago conectado, a comissão não tira a chave PIX da conta; desconectado, tira.</summary>
    [Fact]
    public async Task Com_o_mercado_pago_conectado_a_chave_pix_nao_sai_da_conta()
    {
        await using var api = new MercadoPagoFalso().Na(fabrica);
        var turma = await fabrica.TurmaComPlano();
        var presidente = Cliente(api, turma.Presidente.Cliente);
        await Conectar(presidente, Cliente(api, null));
        var soDinheiro = new MeiosDaContaDTO(null, null, new DinheiroDTO("Ana Souza", "nas reuniões"));

        var conectada = await presidente.PutAsJsonAsync("/api/v1/recebimentos/conta", soDinheiro, Json, Ct);
        (await presidente.DeleteAsync(Conta, Ct)).EnsureSuccessStatusCode();
        var desconectada = await presidente.PutAsJsonAsync("/api/v1/recebimentos/conta", soDinheiro, Json, Ct);

        await ProblemaCom(conectada, "recebimento.chave_pix_obrigatoria");
        desconectada.StatusCode.ShouldBe(HttpStatusCode.OK, await desconectada.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>
    /// 29/09/2026: um modo ou o outro. Conectar não muda o modo; o manual mostra só a comissão e aceita aviso; ir para o
    /// automático espera a fila de avisos esvaziar; no automático não há aviso nem desconexão; voltar ao manual espera
    /// o PIX do Mercado Pago de hoje vencer.
    /// </summary>
    [Fact]
    public async Task Modo_de_cobranca_troca_so_sem_nada_no_meio_do_caminho()
    {
        // Arrange — conectada, ainda no manual
        await using var api = new MercadoPagoFalso().Na(fabrica);
        var turma = await fabrica.TurmaComPlano();
        var formando = await fabrica.FormandoComAdesao(turma.FormaturaId);
        var presidente = Cliente(api, turma.Presidente.Cliente);
        var aluno = Cliente(api, formando.Cliente);
        await Conectar(presidente, Cliente(api, null));
        var parcelas = (await Ler<ExtratoDTO>(await aluno.GetAsync("/api/v1/extrato/eu", Ct))).Parcelas.Select(p => p.Id).ToList();

        // Act / Assert — no manual, só a comissão, e o aviso entra na fila
        var manual = await Ler<CobrancaDaParcelaDTO>(await aluno.GetAsync($"/api/v1/parcelas/{parcelas[0]}/cobranca", Ct));
        manual.PeloMercadoPago.ShouldBeEmpty();
        manual.Meios.ShouldNotBeEmpty();
        (await aluno.PostAsync($"/api/v1/parcelas/{parcelas[0]}/informes", Informe(), Ct)).EnsureSuccessStatusCode();

        await ProblemaCom(await Modo(presidente, automatica: true), "recebimento.avisos_pendentes");

        await using (var contexto = fabrica.ContextoDe(turma.FormaturaId))
        {
            var informe = await contexto.Informes.SingleAsync(Ct);
            (
                await presidente.PostAsJsonAsync($"/api/v1/informes/{informe.Id}/recusar", new RecusarInformeRequestDTO("Não achei."), Json, Ct)
            ).EnsureSuccessStatusCode();
        }

        // Act / Assert — fila vazia, troca; no automático, sem aviso e sem desconectar
        var automatica = await Modo(presidente, automatica: true);
        automatica.StatusCode.ShouldBe(HttpStatusCode.OK, await automatica.Content.ReadAsStringAsync(Ct));
        (await Ler<ProvedorDaTurmaDTO>(automatica)).Provedor!.CobrancaAutomaticaEm.ShouldNotBeNull();

        await ProblemaCom(await aluno.PostAsync($"/api/v1/parcelas/{parcelas[1]}/informes", Informe(), Ct), "pagamento.aviso_desligado");
        await ProblemaCom(await presidente.DeleteAsync(Conta, Ct), "recebimento.cobranca_automatica_ligada");

        var pelo = await Ler<CobrancaDaParcelaDTO>(await aluno.GetAsync($"/api/v1/parcelas/{parcelas[1]}/cobranca", Ct));
        pelo.Meios.ShouldBeEmpty();
        pelo.PeloMercadoPago.ShouldHaveSingleItem().Meio.ShouldBe(MeioDePagamento.Pix);

        // Act / Assert — o PIX de hoje segura a volta ao manual; vencido, ela passa
        await ProblemaCom(await Modo(presidente, automatica: false), "recebimento.pix_em_aberto");

        await using (var contexto = fabrica.ContextoDe(turma.FormaturaId))
            await contexto
                .CobrancasBancarias.IgnoreQueryFilters()
                .Where(c => c.FormaturaId == turma.FormaturaId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiraEm, DateTime.UtcNow.AddMinutes(-1)), Ct);

        var devolta = await Modo(presidente, automatica: false);
        devolta.StatusCode.ShouldBe(HttpStatusCode.OK, await devolta.Content.ReadAsStringAsync(Ct));
        (await Ler<ProvedorDaTurmaDTO>(devolta)).Provedor!.CobrancaAutomaticaEm.ShouldBeNull();
        (await presidente.DeleteAsync(Conta, Ct)).EnsureSuccessStatusCode();
    }

    /// <summary>Troca o modo de cobrança da turma.</summary>
    private static Task<HttpResponseMessage> Modo(HttpClient cliente, bool automatica) =>
        cliente.PutAsJsonAsync($"{Conta}/cobranca", new ModoDeCobrancaRequestDTO(automatica), Json, Ct);

    /// <summary>Liga a cobrança automática — conectar sozinho deixa a turma no manual.</summary>
    private static async Task LigarCobrancaAutomatica(HttpClient presidente) => (await Modo(presidente, automatica: true)).EnsureSuccessStatusCode();

    /// <summary>Um "já paguei" de hoje, em PIX, sem comprovante.</summary>
    private static MultipartFormDataContent Informe() =>
        new()
        {
            { new StringContent(DataUtils.Hoje().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "pagoEm" },
            { new StringContent("100"), "valorEmCentavos" },
            { new StringContent(nameof(MeioDeRecebimento.Pix)), "meio" },
        };

    /// <summary>O presidente cadastra a chave, clica em conectar e o navegador volta do Mercado Pago com o código.</summary>
    private async Task Conectar(HttpClient presidente, HttpClient anonimo)
    {
        await presidente.CadastrarChavePix(fabrica, Ct);
        var state = HttpUtility.ParseQueryString(new Uri(await presidente.UrlDeAutorizacao(fabrica, Ct)).Query)["state"];
        var retorno = await anonimo.GetAsync($"/api/v1/mercado-pago/retorno?code=codigo&state={Uri.EscapeDataString(state!)}", Ct);
        retorno.Headers.Location!.ToString().ShouldContain("mercado_pago=conectado");
    }

    /// <summary>O aviso do Mercado Pago de que o pedido mudou, assinado, com a conta dona no corpo.</summary>
    private static Task<HttpResponseMessage> Avisar(HttpClient anonimo, string idDoPedido, long conta)
    {
        var aviso = new HttpRequestMessage(HttpMethod.Post, $"{Aviso}?data.id={idDoPedido.ToLowerInvariant()}&type=order")
        {
            Content = new StringContent($$$"""{"action":"order.processed","type":"order","user_id":{{{conta}}},"data":{"id":"{{{idDoPedido}}}"}}"""),
        };
        aviso.Headers.Add("x-signature", MercadoPagoFalso.AssinaturaValida);
        aviso.Headers.Add("x-request-id", "req-1");

        return anonimo.SendAsync(aviso, Ct);
    }

    private static async Task ProblemaCom(HttpResponseMessage resposta, string codigo)
    {
        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict, await resposta.Content.ReadAsStringAsync(Ct));
        (await resposta.Content.ReadAsStringAsync(Ct)).ShouldContain(codigo);
    }

    /// <summary>Um cliente da fábrica com o falso, sem seguir redirecionamento, com a sessão de quem já entrou.</summary>
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
