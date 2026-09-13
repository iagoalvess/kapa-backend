using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Api.DTOs.Assinaturas;
using Backend.Api.DTOs.Formaturas;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Formaturas.Models;
using Backend.Data.Provedores;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Backend.IntegrationTests.Assinaturas;

/// <summary>
/// Checkout, webhook e conciliação de ponta a ponta, com o <see cref="ProvedorFake"/> — sem rede.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class AssinaturaEndpointsTests(ApiFactory fabrica)
{
    private const string Webhook = "/api/v1/webhooks/assinaturas";

    private const string Assinatura = "/api/v1/formaturas/atual/assinatura";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ProvedorFake Fake => fabrica.Services.GetRequiredService<ProvedorFake>();

    [Fact]
    public async Task Planos_sao_publicos_e_trazem_o_preco_em_centavos()
    {
        // Act
        var planos = await fabrica.CreateClient().GetFromJsonAsync<PlanoDTO[]>("/api/v1/planos", Json, Ct);

        // Assert
        planos.ShouldNotBeNull();
        planos.Single(p => p.Codigo == "completo").PrecoEmCentavos.ShouldBe(34990);
        planos.Select(p => p.PrecoEmCentavos).ShouldBeInOrder();
    }

    [Theory]
    [InlineData(PapelNaFormatura.Tesoureiro)]
    [InlineData(PapelNaFormatura.Comissao)]
    [InlineData(PapelNaFormatura.Formando)]
    public async Task Checkout_so_o_presidente_inicia(string papel)
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Rascunho, Ct);
        var membro = await fabrica.NovoMembro(formaturaId, papel, Ct);

        var resposta = await Checkout(membro);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await StatusDa(formaturaId)).ShouldBe(StatusDaFormatura.Rascunho);
    }

    [Fact]
    public async Task Checkout_do_presidente_cria_pendente_e_devolve_a_url_do_provedor()
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Rascunho, Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        var resposta = await Checkout(presidente);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var checkout = (await resposta.Content.ReadFromJsonAsync<CheckoutDTO>(Ct))!;
        checkout.Url.ShouldContain("/api/v1/provedor-fake/checkout/fake_");
        (await StatusDa(formaturaId)).ShouldBe(StatusDaFormatura.AguardandoPagamento);

        var assinatura = await presidente.Cliente.GetFromJsonAsync<AssinaturaDTO>(Assinatura, Json, Ct);
        assinatura!.Status.ShouldBe(StatusDaAssinatura.Pendente);
        assinatura.ProximaCobrancaEm.ShouldBeNull();
    }

    /// <summary>R$ 349,90 ida e volta: catálogo, provedor e leitura carregam 34990, nunca 349.9.</summary>
    [Fact]
    public async Task Valores_em_centavos_ponta_a_ponta()
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Rascunho, Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        var checkout = (await (await Checkout(presidente)).Content.ReadFromJsonAsync<CheckoutDTO>(Ct))!;

        Fake.ObterSessao(checkout.Url.Split('/')[^1])!.Pedido.PrecoEmCentavos.ShouldBe(34990);
        (await presidente.Cliente.GetFromJsonAsync<AssinaturaDTO>(Assinatura, Json, Ct))!.Plano.PrecoEmCentavos.ShouldBe(34990);
    }

    [Fact]
    public async Task Formatura_ativa_nao_inicia_novo_checkout()
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        var resposta = await Checkout(presidente);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("assinatura.ja_ativa");
    }

    [Fact]
    public async Task Webhook_com_hmac_invalido_devolve_401_e_nao_grava_nada()
    {
        var (_, assinaturaId) = await FormaturaComCheckout(StatusDaFormatura.Rascunho);
        var eventoId = $"evt_{Guid.CreateVersion7():N}";
        var corpo = Corpo(new EventoDoProvedor(eventoId, TiposDeEvento.PagamentoConfirmado, assinaturaId, null));

        var resposta = await EnviarWebhook(corpo, ProvedorFake.Assinar(corpo, "segredo-errado"));

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await using var contexto = fabrica.ContextoDe(null);
        (await contexto.EventosDeCobranca.AnyAsync(e => e.IdExterno == eventoId, Ct)).ShouldBeFalse();
        (await contexto.Assinaturas.IgnoreQueryFilters().SingleAsync(a => a.Id == assinaturaId, Ct)).Status.ShouldBe(StatusDaAssinatura.Pendente);
    }

    [Fact]
    public async Task Pagamento_confirmado_ativa_assinatura_e_formatura()
    {
        var (formaturaId, assinaturaId) = await FormaturaComCheckout(StatusDaFormatura.Rascunho);

        var resposta = await EnviarEvento(TiposDeEvento.PagamentoConfirmado, assinaturaId);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StatusDa(formaturaId)).ShouldBe(StatusDaFormatura.Ativa);
        var assinatura = await AssinaturaDe(assinaturaId);
        assinatura.Status.ShouldBe(StatusDaAssinatura.Ativa);
        assinatura.VigenteAte.ShouldNotBeNull();
    }

    /// <summary>O provedor reentrega em timeout; processar duas vezes é problema nosso.</summary>
    [Fact]
    public async Task Webhook_repetido_responde_200_e_nao_reprocessa()
    {
        var (formaturaId, assinaturaId) = await FormaturaComCheckout(StatusDaFormatura.Rascunho);
        var corpo = Corpo(new EventoDoProvedor($"evt_{Guid.CreateVersion7():N}", TiposDeEvento.PagamentoConfirmado, assinaturaId, null));
        var hmac = ProvedorFake.Assinar(corpo, ApiFactory.SegredoDoWebhook);

        var primeira = await EnviarWebhook(corpo, hmac);
        var vigenciaDepoisDaPrimeira = (await AssinaturaDe(assinaturaId)).VigenteAte;
        var segunda = await EnviarWebhook(corpo, hmac);

        primeira.StatusCode.ShouldBe(HttpStatusCode.OK);
        segunda.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await segunda.Content.ReadFromJsonAsync<ReciboDeWebhookDTO>(Ct))!.Duplicado.ShouldBeTrue();
        (await AssinaturaDe(assinaturaId)).VigenteAte.ShouldBe(vigenciaDepoisDaPrimeira);
        (await StatusDa(formaturaId)).ShouldBe(StatusDaFormatura.Ativa);

        await using var contexto = fabrica.ContextoDe(null);
        (await contexto.EventosDeCobranca.CountAsync(e => e.AssinaturaId == assinaturaId, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Tipo_desconhecido_e_gravado_e_responde_200()
    {
        var (_, assinaturaId) = await FormaturaComCheckout(StatusDaFormatura.Rascunho);

        var resposta = await EnviarEvento("fatura.criada", assinaturaId);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AssinaturaDe(assinaturaId)).Status.ShouldBe(StatusDaAssinatura.Pendente);
    }

    /// <summary>A turma pagou, o webhook nunca chegou: a conciliação pergunta ao provedor e ativa.</summary>
    [Fact]
    public async Task Conciliacao_corrige_pagamento_cujo_webhook_nunca_chegou()
    {
        var (formaturaId, assinaturaId) = await FormaturaComCheckout(StatusDaFormatura.Rascunho);
        var sessao = (await AssinaturaDe(assinaturaId)).IdExterno!;
        Fake.Pagar(sessao, aprovado: true);

        var resumo = await Conciliar(DateTime.UtcNow.AddHours(1));

        resumo.Confirmadas.ShouldBeGreaterThanOrEqualTo(1);
        (await AssinaturaDe(assinaturaId)).Status.ShouldBe(StatusDaAssinatura.Ativa);
        (await StatusDa(formaturaId)).ShouldBe(StatusDaFormatura.Ativa);
    }

    [Fact]
    public async Task Conciliacao_nao_consulta_pendente_antes_de_30_minutos()
    {
        var (_, assinaturaId) = await FormaturaComCheckout(StatusDaFormatura.Rascunho);
        Fake.Pagar((await AssinaturaDe(assinaturaId)).IdExterno!, aprovado: true);

        await Conciliar(DateTime.UtcNow.AddMinutes(10));

        (await AssinaturaDe(assinaturaId)).Status.ShouldBe(StatusDaAssinatura.Pendente);
    }

    /// <summary>Vencida mais sete dias de carência: suspende — e a turma continua lendo, só não escreve.</summary>
    [Fact]
    public async Task Vencimento_depois_da_carencia_suspende_e_a_turma_continua_lendo()
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        var assinaturaId = await AssinaturaAtiva(formaturaId, pagaEm: DateTime.UtcNow.AddDays(-40));

        await Conciliar(DateTime.UtcNow);

        (await AssinaturaDe(assinaturaId)).Status.ShouldBe(StatusDaAssinatura.Vencida);
        (await StatusDa(formaturaId)).ShouldBe(StatusDaFormatura.Suspensa);

        var lida = await formando.Cliente.GetFromJsonAsync<FormaturaDetalheDTO>("/api/v1/formaturas/atual", Json, Ct);
        lida!.Status.ShouldBe(StatusDaFormatura.Suspensa);
        (await presidente.Cliente.GetAsync(Assinatura, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var escrita = await presidente.Cliente.PostAsync($"{Assinatura}/cancelar", null, Ct);
        escrita.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await escrita.Codigo(Ct)).ShouldBe("formatura.inativa");
    }

    [Fact]
    public async Task Dentro_da_carencia_a_formatura_continua_ativa()
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct);
        var assinaturaId = await AssinaturaAtiva(formaturaId, pagaEm: DateTime.UtcNow.AddMonths(-1).AddDays(-3));

        await Conciliar(DateTime.UtcNow);

        (await AssinaturaDe(assinaturaId)).Status.ShouldBe(StatusDaAssinatura.Ativa);
        (await StatusDa(formaturaId)).ShouldBe(StatusDaFormatura.Ativa);
    }

    [Fact]
    public async Task Pagamento_de_formatura_suspensa_reativa()
    {
        var (formaturaId, assinaturaId) = await FormaturaComCheckout(StatusDaFormatura.Suspensa);
        (await StatusDa(formaturaId)).ShouldBe(StatusDaFormatura.Suspensa);

        await EnviarEvento(TiposDeEvento.PagamentoConfirmado, assinaturaId);

        (await StatusDa(formaturaId)).ShouldBe(StatusDaFormatura.Ativa);
    }

    [Fact]
    public async Task Cancelar_mantem_a_vigencia_e_a_formatura_ativa()
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var assinaturaId = await AssinaturaAtiva(formaturaId, pagaEm: DateTime.UtcNow.AddDays(-5));
        var vigenteAte = (await AssinaturaDe(assinaturaId)).VigenteAte;

        var resposta = await presidente.Cliente.PostAsync($"{Assinatura}/cancelar", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cancelada = (await resposta.Content.ReadFromJsonAsync<AssinaturaDTO>(Json, Ct))!;
        cancelada.Status.ShouldBe(StatusDaAssinatura.Cancelada);
        cancelada.ProximaCobrancaEm.ShouldBeNull();
        (await AssinaturaDe(assinaturaId)).VigenteAte.ShouldBe(vigenteAte);
        (await StatusDa(formaturaId)).ShouldBe(StatusDaFormatura.Ativa);
    }

    [Fact]
    public async Task Tesoureiro_ve_a_assinatura_mas_nao_cancela_e_formando_nem_ve()
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct);
        var tesoureiro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Tesoureiro, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        await AssinaturaAtiva(formaturaId, pagaEm: DateTime.UtcNow);

        (await tesoureiro.Cliente.GetAsync(Assinatura, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await tesoureiro.Cliente.PostAsync($"{Assinatura}/cancelar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await formando.Cliente.GetAsync(Assinatura, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>A página fake paga, entrega o webhook assinado e devolve o navegador ao front.</summary>
    [Fact]
    public async Task Pagina_do_provedor_fake_paga_e_redireciona_para_o_retorno()
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Rascunho, Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var checkout = (await (await Checkout(presidente)).Content.ReadFromJsonAsync<CheckoutDTO>(Ct))!;
        var navegador = fabrica.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

        var pagina = await navegador.GetStringAsync(checkout.Url, Ct);
        var pagamento = await navegador.PostAsync($"{checkout.Url}/pagar", null, Ct);

        pagina.ShouldContain("R$ 349,90");
        pagamento.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        pagamento.Headers.Location!.ToString().ShouldEndWith("/assinatura/retorno");
        (await StatusDa(formaturaId)).ShouldBe(StatusDaFormatura.Ativa);
    }

    /// <summary>
    /// Pediu o Essencial, depois o Ampliado: a sessão do Essencial não paga mais nada. Sem isso,
    /// pagá-la ativava o Ampliado (400 formandos) pelo preço do Essencial.
    /// </summary>
    [Fact]
    public async Task Trocar_de_plano_invalida_a_sessao_do_plano_anterior()
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Rascunho, Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var barato = (await (await Checkout(presidente, "essencial")).Content.ReadFromJsonAsync<CheckoutDTO>(Ct))!;
        (await Checkout(presidente, "ampliado")).EnsureSuccessStatusCode();
        var navegador = fabrica.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

        var pagamento = await navegador.PostAsync($"{barato.Url}/pagar", null, Ct);

        pagamento.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await StatusDa(formaturaId)).ShouldBe(StatusDaFormatura.AguardandoPagamento);
    }

    private static Task<HttpResponseMessage> Checkout(MembroDeTeste membro, string plano = "completo") =>
        membro.Cliente.PostAsJsonAsync($"{Assinatura}/checkout", new IniciarCheckoutRequestDTO(plano), Ct);

    /// <summary>Formatura no status pedido, com Presidente, depois do checkout.</summary>
    private async Task<(Guid FormaturaId, Guid AssinaturaId)> FormaturaComCheckout(StatusDaFormatura status)
    {
        var formaturaId = await fabrica.CriarFormatura(status, Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        (await Checkout(presidente)).EnsureSuccessStatusCode();

        await using var contexto = fabrica.ContextoDe(formaturaId);

        return (formaturaId, (await contexto.Assinaturas.SingleAsync(a => a.Status == StatusDaAssinatura.Pendente, Ct)).Id);
    }

    /// <summary>Grava direto no banco uma assinatura paga na data informada.</summary>
    private async Task<Guid> AssinaturaAtiva(Guid formaturaId, DateTime pagaEm)
    {
        await using var contexto = fabrica.ContextoDe(formaturaId);

        var assinatura = new Assinatura { PlanoId = (await contexto.Planos.SingleAsync(p => p.Codigo == "completo", Ct)).Id };
        assinatura.ConfirmarPagamento(pagaEm, CicloDeCobranca.Mensal).Sucesso.ShouldBeTrue();

        contexto.Assinaturas.Add(assinatura);
        await contexto.SaveChangesAsync(Ct);

        return assinatura.Id;
    }

    private static string Corpo(EventoDoProvedor evento) => JsonSerializer.Serialize(evento, Json);

    private Task<HttpResponseMessage> EnviarEvento(string tipo, Guid assinaturaId)
    {
        var corpo = Corpo(new EventoDoProvedor($"evt_{Guid.CreateVersion7():N}", tipo, assinaturaId, null));

        return EnviarWebhook(corpo, ProvedorFake.Assinar(corpo, ApiFactory.SegredoDoWebhook));
    }

    private Task<HttpResponseMessage> EnviarWebhook(string corpo, string hmac)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, Webhook) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
        pedido.Headers.Add("X-Assinatura", hmac);

        return fabrica.CreateClient().SendAsync(pedido, Ct);
    }

    private async Task<ResumoDaConciliacao> Conciliar(DateTime agoraUtc)
    {
        using var escopo = fabrica.Services.CreateScope();

        return (await escopo.ServiceProvider.GetRequiredService<IWebhookService>().Conciliar(agoraUtc, Ct)).Valor;
    }

    private async Task<Assinatura> AssinaturaDe(Guid assinaturaId)
    {
        await using var contexto = fabrica.ContextoDe(null);

        return await contexto.Assinaturas.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == assinaturaId, Ct);
    }

    private async Task<StatusDaFormatura> StatusDa(Guid formaturaId)
    {
        await using var contexto = fabrica.ContextoDe(null);

        return await contexto.Formaturas.Where(f => f.Id == formaturaId).Select(f => f.Status).SingleAsync(Ct);
    }
}
