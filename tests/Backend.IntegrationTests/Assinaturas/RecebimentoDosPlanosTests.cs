using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Admin;
using Backend.Api.DTOs.Assinaturas;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.MercadoPago.Models;
using Backend.Business.Pagamentos.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Backend.IntegrationTests.Assinaturas;

/// <summary>
/// A Sprint 37 de ponta a ponta, com a conta do Kapa no Mercado Pago falso: contratar pelo PIX e pelo cartão, a
/// renovação, a troca de meio e de plano, o estorno do suporte e a planilha da nota — tudo pelos avisos da conta do Kapa.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class RecebimentoDosPlanosTests(ApiFactory fabrica)
{
    private const string Assinatura = "/api/v1/formaturas/atual/assinatura";

    private const string Aviso = "/api/v1/webhooks/cobranca/mercadopago";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Critério de aceite: pelo PIX avulso, a assinatura fica ativa sozinha — e o aviso repetido não paga duas vezes.</summary>
    [Fact]
    public async Task Contratar_pelo_pix_ativa_a_turma_pelo_aviso_da_conta_do_kapa()
    {
        // Arrange
        var falso = new MercadoPagoFalso();
        await using var api = falso.CobrandoOsPlanos(fabrica);
        var (presidente, formaturaId) = await Turma(api);

        // Act
        var checkout = await Ler<CheckoutDTO>(
            await presidente.PostAsJsonAsync($"{Assinatura}/checkout", new { planoCodigo = "essencial", meio = "Pix" }, Json, Ct)
        );
        var cobranca = falso.Paginas.ShouldHaveSingleItem().Value;
        var pagamento = falso.PagarPagina(cobranca.Referencia);
        var primeiro = await Avisar(api, "payment", pagamento);
        var repetido = await Avisar(api, "payment", pagamento);

        // Assert
        checkout.Url.ShouldStartWith("https://mp.testes/checkout/");
        cobranca.Valor.ShouldBe(8900);
        primeiro.StatusCode.ShouldBe(HttpStatusCode.OK);
        repetido.StatusCode.ShouldBe(HttpStatusCode.OK);
        var assinatura = await Ler<AssinaturaDTO>(await presidente.GetAsync(Assinatura, Ct));
        assinatura.Status.ShouldBe(StatusDaAssinatura.Ativa);
        assinatura.Meio.ShouldBe(MeioDePagamento.Pix);
        assinatura.ProximaCobrancaEm.ShouldNotBeNull();
        var historico = await Ler<CobrancaDoPlanoDTO[]>(await presidente.GetAsync($"{Assinatura}/cobrancas", Ct));
        var pago = historico.ShouldHaveSingleItem();
        pago.Situacao.ShouldBe(SituacaoDaCobrancaDoPlano.Paga);
        pago.ValorEmCentavos.ShouldBe(8900);
        (await StatusDa(formaturaId)).ShouldBe(StatusDaFormatura.Ativa);
    }

    /// <summary>
    /// Critérios de aceite: pelo cartão a assinatura ativa no primeiro débito, e o débito do mês seguinte estende a
    /// vigência sem ninguém clicar. Autorizar a recorrência sozinho não ativa nada.
    /// </summary>
    [Fact]
    public async Task Cartao_ativa_no_primeiro_debito_e_renova_no_seguinte()
    {
        // Arrange
        var falso = new MercadoPagoFalso();
        await using var api = falso.CobrandoOsPlanos(fabrica);
        var (presidente, _) = await Turma(api);

        // Act — autorizada, sem débito
        var checkout = await Ler<CheckoutDTO>(
            await presidente.PostAsJsonAsync($"{Assinatura}/checkout", new { planoCodigo = "premium", meio = "Cartao" }, Json, Ct)
        );
        var recorrencia = falso.Recorrencias.ShouldHaveSingleItem().Key;
        falso.MudarRecorrencia(recorrencia, SituacaoDaRecorrencia.Autorizada);
        await Avisar(api, "subscription_preapproval", recorrencia);
        var autorizada = await Ler<AssinaturaDTO>(await presidente.GetAsync(Assinatura, Ct));

        // Act — o primeiro débito e o do mês seguinte
        await Avisar(api, "subscription_authorized_payment", falso.Debitar(recorrencia, aprovado: true));
        var ativa = await Ler<AssinaturaDTO>(await presidente.GetAsync(Assinatura, Ct));
        await Avisar(api, "subscription_authorized_payment", falso.Debitar(recorrencia, aprovado: true));
        var renovada = await Ler<AssinaturaDTO>(await presidente.GetAsync(Assinatura, Ct));

        // Assert
        checkout.Url.ShouldStartWith("https://mp.testes/preapproval/");
        autorizada.Status.ShouldBe(StatusDaAssinatura.Pendente);
        ativa.Status.ShouldBe(StatusDaAssinatura.Ativa);
        ativa.Meio.ShouldBe(MeioDePagamento.Cartao);
        renovada.VigenteAte!.Value.ShouldBe(ativa.VigenteAte!.Value.AddMonths(1), TimeSpan.FromSeconds(5));
        var historico = await Ler<CobrancaDoPlanoDTO[]>(await presidente.GetAsync($"{Assinatura}/cobrancas", Ct));
        historico.Length.ShouldBe(2);
        historico.ShouldAllBe(c => c.Situacao == SituacaoDaCobrancaDoPlano.Paga && c.Meio == MeioDePagamento.Cartao);
    }

    /// <summary>
    /// P5: trocar do cartão para o PIX cancela a recorrência — e o "cancelada" que o Mercado Pago manda em seguida não
    /// cancela a assinatura.
    /// </summary>
    [Fact]
    public async Task Trocar_do_cartao_para_o_pix_nao_deixa_o_aviso_de_cancelamento_cancelar_a_assinatura()
    {
        // Arrange
        var falso = new MercadoPagoFalso();
        await using var api = falso.CobrandoOsPlanos(fabrica);
        var (presidente, recorrencia) = await NoCartao(api, falso, "premium");

        // Act
        var troca = await Ler<TrocaDTO>(await presidente.PostAsJsonAsync($"{Assinatura}/trocar-meio", new { meio = "Pix" }, Json, Ct));
        var aviso = await Avisar(api, "subscription_preapproval", recorrencia);
        var depois = await Ler<AssinaturaDTO>(await presidente.GetAsync(Assinatura, Ct));

        // Assert
        troca.Url.ShouldBeNull();
        falso.Recorrencias[recorrencia].Situacao.ShouldBe(SituacaoDaRecorrencia.Cancelada);
        aviso.StatusCode.ShouldBe(HttpStatusCode.OK);
        depois.Status.ShouldBe(StatusDaAssinatura.Ativa);
        depois.Meio.ShouldBe(MeioDePagamento.Pix);
    }

    /// <summary>
    /// P5 no outro sentido: a recorrência nova começa no fim da vigência e a assinatura só passa para o cartão quando
    /// ela for autorizada.
    /// </summary>
    [Fact]
    public async Task Trocar_do_pix_para_o_cartao_so_vale_depois_de_autorizar()
    {
        // Arrange
        var falso = new MercadoPagoFalso();
        await using var api = falso.CobrandoOsPlanos(fabrica);
        var presidente = await NoPix(api, falso, "essencial");
        var antes = await Ler<AssinaturaDTO>(await presidente.GetAsync(Assinatura, Ct));

        // Act
        var troca = await Ler<TrocaDTO>(await presidente.PostAsJsonAsync($"{Assinatura}/trocar-meio", new { meio = "Cartao" }, Json, Ct));
        var aposATroca = await Ler<AssinaturaDTO>(await presidente.GetAsync(Assinatura, Ct));
        var recorrencia = falso.Recorrencias.ShouldHaveSingleItem();
        falso.MudarRecorrencia(recorrencia.Key, SituacaoDaRecorrencia.Autorizada);
        await Avisar(api, "subscription_preapproval", recorrencia.Key);
        var depois = await Ler<AssinaturaDTO>(await presidente.GetAsync(Assinatura, Ct));

        // Assert
        troca.Url.ShouldNotBeNull();
        aposATroca.Meio.ShouldBe(MeioDePagamento.Pix);
        aposATroca.CartaoAguardandoAutorizacao.ShouldBeTrue();
        recorrencia.Value.ProximaCobrancaEm!.Value.ShouldBe(antes.VigenteAte!.Value, TimeSpan.FromSeconds(1));
        depois.Meio.ShouldBe(MeioDePagamento.Cartao);
        depois.CartaoAguardandoAutorizacao.ShouldBeFalse();
        depois.VigenteAte.ShouldBe(antes.VigenteAte);
    }

    /// <summary>P4: a subida cobra a diferença proporcional e o Premium vale quando ela for paga; a recorrência passa a cobrar o Premium.</summary>
    [Fact]
    public async Task Subir_de_plano_cobra_a_diferenca_e_ajusta_a_recorrencia()
    {
        // Arrange
        var falso = new MercadoPagoFalso();
        await using var api = falso.CobrandoOsPlanos(fabrica);
        var (presidente, recorrencia) = await NoCartao(api, falso, "essencial");

        // Act
        var troca = await Ler<TrocaDTO>(await presidente.PostAsJsonAsync($"{Assinatura}/trocar-plano", new { planoCodigo = "premium" }, Json, Ct));
        var antesDePagar = await Ler<AssinaturaDTO>(await presidente.GetAsync(Assinatura, Ct));
        var diferenca = falso.Paginas.ShouldHaveSingleItem().Value;
        await Avisar(api, "payment", falso.PagarPagina(diferenca.Referencia));
        var depois = await Ler<AssinaturaDTO>(await presidente.GetAsync(Assinatura, Ct));

        // Assert
        troca.Url.ShouldNotBeNull();
        diferenca.Valor.ShouldBeInRange(8900, 9000);
        antesDePagar.Plano.Codigo.ShouldBe("essencial");
        depois.Plano.Codigo.ShouldBe("premium");
        depois.VigenteAte.ShouldBe(antesDePagar.VigenteAte);
        falso.ValorDaRecorrencia(recorrencia).ShouldBe(17900);
    }

    /// <summary>P4: a descida vale na próxima renovação, e a recorrência já cobra o preço novo nela.</summary>
    [Fact]
    public async Task Descer_de_plano_vale_na_renovacao()
    {
        // Arrange
        var falso = new MercadoPagoFalso();
        await using var api = falso.CobrandoOsPlanos(fabrica);
        var (presidente, recorrencia) = await NoCartao(api, falso, "premium");

        // Act
        var troca = await Ler<TrocaDTO>(await presidente.PostAsJsonAsync($"{Assinatura}/trocar-plano", new { planoCodigo = "essencial" }, Json, Ct));
        var aposATroca = await Ler<AssinaturaDTO>(await presidente.GetAsync(Assinatura, Ct));
        await Avisar(api, "subscription_authorized_payment", falso.Debitar(recorrencia, aprovado: true));
        var renovada = await Ler<AssinaturaDTO>(await presidente.GetAsync(Assinatura, Ct));

        // Assert
        troca.Url.ShouldBeNull();
        aposATroca.Plano.Codigo.ShouldBe("premium");
        aposATroca.ProximoPlano!.Codigo.ShouldBe("essencial");
        falso.ValorDaRecorrencia(recorrencia).ShouldBe(8900);
        renovada.Plano.Codigo.ShouldBe("essencial");
        renovada.ProximoPlano.ShouldBeNull();
    }

    /// <summary>A troca de plano é do Presidente, como a contratação.</summary>
    [Fact]
    public async Task Trocar_de_plano_e_de_meio_so_o_presidente()
    {
        // Arrange
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var tesoureiro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Tesoureiro, Ct);

        // Act
        var plano = await tesoureiro.Cliente.PostAsJsonAsync($"{Assinatura}/trocar-plano", new { planoCodigo = "essencial" }, Json, Ct);
        var meio = await tesoureiro.Cliente.PostAsJsonAsync($"{Assinatura}/trocar-meio", new { meio = "Pix" }, Json, Ct);
        var historico = await tesoureiro.Cliente.GetAsync($"{Assinatura}/cobrancas", Ct);

        // Assert
        plano.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        meio.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        historico.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Corpo sem o plano é erro de forma no campo, e não 500: quem confere é o validator.</summary>
    [Fact]
    public async Task Trocar_de_plano_sem_o_codigo_e_400_no_campo()
    {
        // Arrange
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        // Act
        var resposta = await presidente.Cliente.PostAsJsonAsync($"{Assinatura}/trocar-plano", new { }, Json, Ct);

        // Assert
        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problema = await resposta.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, Ct);
        problema!.Errors.Keys.ShouldContain("plano_codigo");
    }

    /// <summary>
    /// P7: o suporte estorna o pagamento inteiro nos 7 dias, a renovação para e a turma fica só para consulta. Estornar
    /// de novo é conflito.
    /// </summary>
    [Fact]
    public async Task Suporte_estorna_integral_e_encerra_a_assinatura()
    {
        // Arrange
        var falso = new MercadoPagoFalso();
        await using var api = falso.CobrandoOsPlanos(fabrica);
        var (presidente, recorrencia) = await NoCartao(api, falso, "premium");
        var formaturaId = await FormaturaDa(presidente);
        var suporte = await Suporte(api);
        var turma = await Ler<TurmaNoSuporteDTO>(await suporte.GetAsync($"/api/v1/admin/suporte/formaturas/{formaturaId}", Ct));
        var pagamento = turma.Pagamentos.ShouldHaveSingleItem();
        var rota = $"/api/v1/admin/suporte/formaturas/{formaturaId}/pagamentos/{pagamento.Id}/estornar";

        // Act
        var estorno = await suporte.PostAsJsonAsync(rota, new { modo = "Integral" }, Json, Ct);
        var denovo = await suporte.PostAsJsonAsync(rota, new { modo = "Integral" }, Json, Ct);

        // Assert
        var depois = await Ler<TurmaNoSuporteDTO>(estorno);
        depois.Pagamentos.ShouldHaveSingleItem().Situacao.ShouldBe(SituacaoDaCobrancaDoPlano.Estornada);
        depois.Assinatura!.Status.ShouldBe(nameof(StatusDaAssinatura.Vencida));
        depois.Status.ShouldBe(nameof(StatusDaFormatura.Suspensa));
        falso.Estornos.ShouldHaveSingleItem().Valor.ShouldBe(17900);
        falso.Recorrencias[recorrencia].Situacao.ShouldBe(SituacaoDaRecorrencia.Cancelada);
        denovo.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await denovo.Codigo(Ct)).ShouldBe("estorno.cobranca_nao_paga");
    }

    /// <summary>P6: a planilha do mês sai em .xlsx para o suporte, com o pagamento da turma.</summary>
    [Fact]
    public async Task Planilha_da_nota_sai_para_o_suporte()
    {
        // Arrange
        var falso = new MercadoPagoFalso();
        await using var api = falso.CobrandoOsPlanos(fabrica);
        await NoPix(api, falso, "essencial");
        var suporte = await Suporte(api);
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

        // Act
        var planilha = await suporte.GetAsync($"/api/v1/admin/suporte/pagamentos?ano={hoje.Year}&mes={hoje.Month}", Ct);
        var invalida = await suporte.GetAsync("/api/v1/admin/suporte/pagamentos?ano=2026&mes=13", Ct);

        // Assert
        planilha.StatusCode.ShouldBe(HttpStatusCode.OK);
        planilha.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        (await planilha.Content.ReadAsByteArrayAsync(Ct)).Length.ShouldBeGreaterThan(1000);
        invalida.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>Sprint 51, D4: no cartão, o cupom vale no primeiro débito; depois dele, a recorrência volta ao cheio.</summary>
    [Fact]
    public async Task Cupom_no_cartao_desconta_o_primeiro_debito_e_volta_ao_cheio()
    {
        // Arrange
        var falso = new MercadoPagoFalso();
        await using var api = falso.CobrandoOsPlanos(fabrica);
        var codigo = await CupomNoBanco(50);
        var (presidente, _) = await Turma(api);

        // Act
        await Ler<CheckoutDTO>(
            await presidente.PostAsJsonAsync(
                $"{Assinatura}/checkout",
                new
                {
                    planoCodigo = "essencial",
                    meio = "Cartao",
                    cupomCodigo = codigo,
                },
                Json,
                Ct
            )
        );
        var recorrencia = falso.Recorrencias.Keys.Last();
        var primeiro = falso.ValorDaRecorrencia(recorrencia);
        falso.MudarRecorrencia(recorrencia, SituacaoDaRecorrencia.Autorizada);
        await Avisar(api, "subscription_authorized_payment", falso.Debitar(recorrencia, aprovado: true));
        var assinatura = await Ler<AssinaturaDTO>(await presidente.GetAsync(Assinatura, Ct));

        // Assert
        primeiro.ShouldBe(4450);
        falso.ValorDaRecorrencia(recorrencia).ShouldBe(8900);
        assinatura.Status.ShouldBe(StatusDaAssinatura.Ativa);
        assinatura.Cupom!.Codigo.ShouldBe(codigo);
    }

    /// <summary>Sprint 51, D4: no PIX, a página da contratação sai com desconto.</summary>
    [Fact]
    public async Task Cupom_no_pix_desconta_a_pagina_da_contratacao()
    {
        // Arrange
        var falso = new MercadoPagoFalso();
        await using var api = falso.CobrandoOsPlanos(fabrica);
        var codigo = await CupomNoBanco(10);
        var (presidente, _) = await Turma(api);

        // Act
        await Ler<CheckoutDTO>(
            await presidente.PostAsJsonAsync(
                $"{Assinatura}/checkout",
                new
                {
                    planoCodigo = "premium-anual",
                    meio = "Pix",
                    cupomCodigo = codigo,
                },
                Json,
                Ct
            )
        );

        // Assert
        falso.Paginas.ShouldHaveSingleItem().Value.Valor.ShouldBe(154620);
    }

    /// <summary>Um cupom gravado direto no banco, com código único por teste.</summary>
    private async Task<string> CupomNoBanco(int percentual)
    {
        await using var contexto = fabrica.ContextoDe(null);
        var cupom = new Cupom
        {
            Codigo = $"T{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            Percentual = percentual,
            ValidoAte = DateTime.UtcNow.AddDays(1),
            LimiteDeUsos = 5,
        };
        contexto.Cupons.Add(cupom);
        await contexto.SaveChangesAsync(Ct);

        return cupom.Codigo;
    }

    /// <summary>Uma turma no gratuito com o Presidente, falando com a fábrica do falso.</summary>
    private async Task<(HttpClient Presidente, Guid FormaturaId)> Turma(WebApplicationFactory<Program> api)
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct, contratada: false);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        return (Cliente(api, presidente.Cliente), formaturaId);
    }

    /// <summary>Contrata pelo cartão e confirma o primeiro débito. Devolve o Presidente e a recorrência.</summary>
    private async Task<(HttpClient Presidente, string Recorrencia)> NoCartao(WebApplicationFactory<Program> api, MercadoPagoFalso falso, string plano)
    {
        var (presidente, _) = await Turma(api);
        await Ler<CheckoutDTO>(await presidente.PostAsJsonAsync($"{Assinatura}/checkout", new { planoCodigo = plano, meio = "Cartao" }, Json, Ct));
        var recorrencia = falso.Recorrencias.Keys.Last();
        falso.MudarRecorrencia(recorrencia, SituacaoDaRecorrencia.Autorizada);
        await Avisar(api, "subscription_authorized_payment", falso.Debitar(recorrencia, aprovado: true));

        return (presidente, recorrencia);
    }

    /// <summary>Contrata pelo PIX e paga a página. Devolve o Presidente.</summary>
    private async Task<HttpClient> NoPix(WebApplicationFactory<Program> api, MercadoPagoFalso falso, string plano)
    {
        var (presidente, _) = await Turma(api);
        await Ler<CheckoutDTO>(await presidente.PostAsJsonAsync($"{Assinatura}/checkout", new { planoCodigo = plano, meio = "Pix" }, Json, Ct));
        await Avisar(api, "payment", falso.PagarPagina(falso.Paginas.Values.Last().Referencia));

        return presidente;
    }

    /// <summary>O aviso do Mercado Pago, assinado, com a conta do Kapa no corpo.</summary>
    private static async Task<HttpResponseMessage> Avisar(WebApplicationFactory<Program> api, string tipo, string id)
    {
        var aviso = new HttpRequestMessage(HttpMethod.Post, $"{Aviso}?data.id={id}&type={tipo}")
        {
            Content = new StringContent($$$"""{"type":"{{{tipo}}}","user_id":{{{MercadoPagoFalso.ContaDoKapa}}},"data":{"id":"{{{id}}}"}}"""),
        };
        aviso.Headers.Add("x-signature", MercadoPagoFalso.AssinaturaValida);
        aviso.Headers.Add("x-request-id", "req-1");

        var resposta = await Cliente(api, null).SendAsync(aviso, Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync(Ct));

        return resposta;
    }

    private async Task<Guid> FormaturaDa(HttpClient presidente)
    {
        var assinatura = await Ler<AssinaturaDTO>(await presidente.GetAsync(Assinatura, Ct));
        await using var contexto = fabrica.ContextoDe(null);

        return await contexto.Assinaturas.IgnoreQueryFilters().Where(a => a.Id == assinatura.Id).Select(a => a.FormaturaId).SingleAsync(Ct);
    }

    private async Task<StatusDaFormatura> StatusDa(Guid formaturaId)
    {
        await using var contexto = fabrica.ContextoDe(null);

        return await contexto.Formaturas.Where(f => f.Id == formaturaId).Select(f => f.Status).SingleAsync(Ct);
    }

    private async Task<HttpClient> Suporte(WebApplicationFactory<Program> api)
    {
        var cliente = Cliente(api, null);
        var tokens = await cliente.AutenticarComoAdministrador(fabrica, Ct);

        return cliente.ComToken(tokens.AccessToken);
    }

    /// <summary>Um cliente da fábrica do falso, com a sessão de quem já entrou.</summary>
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
