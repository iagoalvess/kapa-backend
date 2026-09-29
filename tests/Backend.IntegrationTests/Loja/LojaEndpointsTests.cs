using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Backend.Api.DTOs.Agenda;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Festa;
using Backend.Api.DTOs.Loja;
using Backend.Api.DTOs.Recebimentos;
using Backend.Business.Agenda.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Festa.Models;
using Backend.Business.Financeiro.Models;
using Backend.Business.Loja.Models;
using Backend.Business.MercadoPago.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Models;
using Backend.Data.Criptografia;
using Backend.Data.Repositories;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Backend.IntegrationTests.Loja;

/// <summary>
/// A loja pública (Sprint 26) contra a API e o Postgres de verdade, com o Mercado Pago falso: comprar sem
/// conta, o aviso que confirma e emite os convites, e as garantias que moram no banco — estoque único,
/// idempotência, limite por CPF, expiração condicional.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed partial class LojaEndpointsTests(ApiFactory fabrica)
{
    private const string Aviso = "/api/v1/webhooks/cobranca/mercadopago";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Uma turma com o Mercado Pago conectado e um convite à venda na loja.</summary>
    private sealed record Loja(MercadoPagoFalso Falso, WebApplicationFactory<Program> Api, TurmaDeTeste Turma, Guid ItemId, HttpClient Anonimo)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Api.DisposeAsync();
    }

    /// <summary>
    /// O caminho inteiro: vitrine, compra sem conta, F5 que não compra duas vezes, o aviso que confirma, os
    /// convites emitidos, a receita no caixa e a nomeação pelo link.
    /// </summary>
    [Fact]
    public async Task Comprar_sem_conta_confirma_pelo_aviso_emite_os_convites_e_vira_receita()
    {
        // Arrange
        await using var loja = await Montar(estoque: 3, limite: 2, precoPublico: 25_000, comFesta: true);
        var email = $"maria-{Guid.NewGuid():N}@teste.dev";
        var pedido = Pedido(2, email, "529.982.247-25");

        // Act — a vitrine e a compra, duas vezes com a mesma chave
        var vitrine = await Ler<LojaDTO>(await loja.Anonimo.GetAsync($"/api/v1/loja/{loja.Turma.FormaturaId}", Ct));
        var criada = await Ler<CompraCriadaDTO>(await Comprar(loja, pedido));
        var repetida = await Ler<CompraCriadaDTO>(await Comprar(loja, pedido));

        // Assert — reservou na hora, uma vez, com o PIX da compra
        var item = vitrine.Itens.ShouldHaveSingleItem();
        item.PrecoEmCentavos.ShouldBe(25_000);
        item.Disponivel.ShouldBe(3);
        item.Aberto.ShouldBeTrue();
        vitrine.Turma.ShouldNotBeNullOrWhiteSpace();
        repetida.Compra.Id.ShouldBe(criada.Compra.Id);
        repetida.Token.ShouldBe(criada.Token);
        criada.Compra.Status.ShouldBe(StatusDaCompra.Pendente);
        criada.Compra.ValorEmCentavos.ShouldBe(50_000);
        criada.Compra.Cobranca!.CopiaECola.ShouldNotBeNullOrWhiteSpace();
        loja.Falso.Pedidos.ShouldHaveSingleItem().Value.Valor.ShouldBe(50_000);
        (await Reservados(loja)).ShouldBe(2);

        // Act — o comprador paga, e o Mercado Pago avisa
        loja.Falso.Pagar(loja.Falso.Pedidos.Single().Key);
        (await Avisar(loja, loja.Falso.Pedidos.Single().Key)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var paga = await Ler<CompraDTO>(await loja.Anonimo.GetAsync($"/api/v1/loja/compras/{criada.Token}", Ct));

        // Assert — paga, dois convites, uma receita, e o e-mail com o link
        paga.Status.ShouldBe(StatusDaCompra.Paga);
        paga.Convites.Select(c => c.NomeDoConvidado).ShouldBe(["Convidado 1", "Convidado 2"]);
        paga.Convites.ShouldAllBe(c => c.Documento != null && c.Token != null);
        paga.Cobranca.ShouldBeNull();
        await using (var contexto = fabrica.ContextoDe(loja.Turma.FormaturaId))
        {
            var receita = await contexto.OutrasReceitas.SingleAsync(Ct);
            receita.Categoria.ShouldBe(CategoriaDeOutraReceita.VendaDeConvite);
            receita.Status.ShouldBe(StatusDaOutraReceita.Recebida);
            receita.ValorEmCentavos.ShouldBe(50_000);
            (await contexto.EmailsFila.CountAsync(e => e.Para == email, Ct)).ShouldBe(2);
            (await contexto.EmailsFila.AnyAsync(e => e.Para == email && e.CorpoHtml.Contains(criada.Token), Ct)).ShouldBeTrue();
        }

        // Act — o aviso chega de novo, e o comprador passa o primeiro convite para outra pessoa
        (await Avisar(loja, loja.Falso.Pedidos.Single().Key)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var nomeado = await Ler<MeuConviteDTO>(
            await loja.Anonimo.PutAsJsonAsync(
                $"/api/v1/loja/compras/{criada.Token}/convites/{paga.Convites.First().Id}/convidado",
                new ConvidadoRequestDTO("Tia Carmem", TipoDeDocumento.Cpf, "111.444.777-35", null),
                Json,
                Ct
            )
        );

        // Assert — o aviso repetido não emitiu mais nada nem desfez a troca, e o convite trocado tem código novo
        nomeado.NomeDoConvidado.ShouldBe("Tia Carmem");
        nomeado.Token.ShouldNotBeNull();
        nomeado.Codigo.ShouldNotBe(paga.Convites.First().Codigo);
        await using (var contexto = fabrica.ContextoDe(loja.Turma.FormaturaId))
        {
            (await contexto.ConvitesDoEvento.CountAsync(c => c.CompraId == criada.Compra.Id && c.RevogadoEm == null, Ct)).ShouldBe(2);
            (await contexto.OutrasReceitas.CountAsync(Ct)).ShouldBe(1);
        }
    }

    /// <summary>P3 e decisão 1: o CPF segura o limite, o estoque é um só, e esgotado diz esgotado.</summary>
    [Fact]
    public async Task Limite_por_cpf_estoque_unico_e_esgotado_com_o_codigo_certo()
    {
        await using var loja = await Montar(estoque: 3, limite: 2);

        (await Comprar(loja, Pedido(2, Email(), "52998224725"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var passouDoLimite = await Comprar(loja, Pedido(1, Email(), "529.982.247-25"));
        var alemDoEstoque = await Comprar(loja, Pedido(2, Email(), "11144477735"));
        var ultima = await Comprar(loja, Pedido(1, Email(), "11144477735"));
        var esgotado = await Comprar(loja, Pedido(1, Email(), "39053344705"));

        await Problema(passouDoLimite, HttpStatusCode.Conflict, "loja.limite_por_pessoa");
        await Problema(alemDoEstoque, HttpStatusCode.Conflict, "loja.esgotado");
        ultima.StatusCode.ShouldBe(HttpStatusCode.OK);
        await Problema(esgotado, HttpStatusCode.Conflict, "loja.esgotado");
        (await Reservados(loja)).ShouldBe(3);
    }

    /// <summary>Decisão 8: com muita gente e poucos convites, o banco vende o estoque e nenhum a mais.</summary>
    [Fact]
    public async Task Vinte_compradores_ao_mesmo_tempo_levam_exatamente_o_estoque()
    {
        await using var loja = await Montar(estoque: 5);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Comprar(loja, Pedido(1, Email(), AdesaoDeTeste.NovoCpf()))));

        respostas.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(5);
        respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(15);
        (await Reservados(loja)).ShouldBe(5);
    }

    /// <summary>P6 e P10: CPF inválido não chega à reserva, e a abertura é pela hora do servidor.</summary>
    [Fact]
    public async Task Cpf_invalido_e_compra_antes_da_abertura_sao_recusados()
    {
        await using var loja = await Montar(estoque: 3, abertura: DateTime.UtcNow.AddHours(2));

        await Problema(await Comprar(loja, Pedido(1, Email(), "52998224724")), HttpStatusCode.BadRequest, "cpf");
        await Problema(await Comprar(loja, Pedido(2, Email(), "52998224725") with { Convidados = [] }), HttpStatusCode.BadRequest, "convidados");
        await Problema(
            await Comprar(loja, Pedido(1, Email(), "52998224725") with { Convidados = [new ConvidadoRequestDTO("Tia Carmem", null, null, null)] }),
            HttpStatusCode.BadRequest,
            "convidados[0].numero_do_documento"
        );
        await Problema(await Comprar(loja, Pedido(1, Email(), "52998224725")), HttpStatusCode.Conflict, "cobranca.venda_nao_aberta");
        (await Reservados(loja)).ShouldBe(0);
    }

    /// <summary>Decisão 7: expirar devolve o estoque uma vez só — a segunda passada não acha a compra pendente.</summary>
    [Fact]
    public async Task Compra_vencida_expira_e_devolve_o_estoque_uma_vez()
    {
        await using var loja = await Montar(estoque: 3);
        var criada = await Ler<CompraCriadaDTO>(await Comprar(loja, Pedido(2, Email(), AdesaoDeTeste.NovoCpf())));
        await Vencer(loja, criada.Compra.Id);

        await using var contexto = fabrica.ContextoDe(loja.Turma.FormaturaId);
        var repositorio = new CompraDeConviteRepository(contexto, fabrica.Services.GetRequiredService<CifraDeCampo>());

        (await repositorio.Expirar(criada.Compra.Id, DateTime.UtcNow, Ct)).ShouldBeTrue();
        (await repositorio.Expirar(criada.Compra.Id, DateTime.UtcNow, Ct)).ShouldBeFalse();
        (await Reservados(loja)).ShouldBe(0);
        (await contexto.ComprasDeConvite.AsNoTracking().SingleAsync(c => c.Id == criada.Compra.Id, Ct)).Status.ShouldBe(StatusDaCompra.Expirada);
    }

    /// <summary>Decisão 9: pagamento de compra expirada, sem lugar, vai para a lista de devolução — nunca em silêncio.</summary>
    [Fact]
    public async Task Pagamento_tardio_sem_lugar_vai_para_a_devolucao()
    {
        await using var loja = await Montar(estoque: 1);
        var tardia = await Ler<CompraCriadaDTO>(await Comprar(loja, Pedido(1, Email(), AdesaoDeTeste.NovoCpf())));
        await Vencer(loja, tardia.Compra.Id);
        await using (var contexto = fabrica.ContextoDe(loja.Turma.FormaturaId))
            await new CompraDeConviteRepository(contexto, fabrica.Services.GetRequiredService<CifraDeCampo>()).Expirar(
                tardia.Compra.Id,
                DateTime.UtcNow,
                Ct
            );
        (await Comprar(loja, Pedido(1, Email(), AdesaoDeTeste.NovoCpf()))).StatusCode.ShouldBe(HttpStatusCode.OK);

        string idDaTardia;
        await using (var contexto = fabrica.ContextoDe(loja.Turma.FormaturaId))
            idDaTardia = (await contexto.CobrancasBancarias.SingleAsync(c => c.CompraId == tardia.Compra.Id, Ct)).IdExterno!;
        loja.Falso.Pagar(idDaTardia);
        (await Avisar(loja, idDaTardia)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var resumo = await Ler<ResumoDaLojaDTO>(await loja.Turma.Presidente.Cliente.GetAsync("/api/v1/loja/compras/resumo", Ct));
        resumo.ComprasADevolver.ShouldBe(1);
        resumo.ConvitesVendidos.ShouldBe(0);
        resumo.AguardandoPix.ShouldBe(1);
        (await Reservados(loja)).ShouldBe(1);
    }

    /// <summary>Decisão 10: o reenvio responde igual exista compra ou não, e mata o link anterior.</summary>
    [Fact]
    public async Task Reenviar_o_link_responde_igual_e_mata_o_anterior()
    {
        await using var loja = await Montar(estoque: 3);
        var email = Email();
        var criada = await Ler<CompraCriadaDTO>(await Comprar(loja, Pedido(1, email, AdesaoDeTeste.NovoCpf())));

        var conhecido = await loja.Anonimo.PostAsJsonAsync(
            $"/api/v1/loja/{loja.Turma.FormaturaId}/reenvio",
            new ReenvioDoLinkRequestDTO(email),
            Json,
            Ct
        );
        var desconhecido = await loja.Anonimo.PostAsJsonAsync(
            $"/api/v1/loja/{loja.Turma.FormaturaId}/reenvio",
            new ReenvioDoLinkRequestDTO(Email()),
            Json,
            Ct
        );

        conhecido.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        desconhecido.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await Problema(
            await loja.Anonimo.GetAsync($"/api/v1/loja/compras/{criada.Token}", Ct),
            HttpStatusCode.NotFound,
            "loja.compra_nao_encontrada"
        );
        await using var contexto = fabrica.ContextoDe(loja.Turma.FormaturaId);
        (await contexto.EmailsFila.CountAsync(e => e.Para == email, Ct)).ShouldBe(2);
    }

    /// <summary>P8 e fronteira: o item da loja sai da vitrine do formando, e a lista de compras é só da Gestão.</summary>
    [Fact]
    public async Task Formando_nao_pede_o_item_da_loja_nem_ve_a_lista_de_compras()
    {
        await using var loja = await Montar(estoque: 3);
        var formando = await fabrica.FormandoComAdesao(loja.Turma.FormaturaId);

        var pedido = await formando.Cliente.PostAsJsonAsync("/api/v1/pedidos", new PedidoRequestDTO(loja.ItemId, 1, 1), Json, Ct);
        var vitrine = await formando.Cliente.GetAsync("/api/v1/cobrancas/opcionais", Ct);
        var lista = await formando.Cliente.GetAsync("/api/v1/loja/compras", Ct);

        await Problema(pedido, HttpStatusCode.Conflict, "cobranca.item_da_loja");
        (await vitrine.Content.ReadAsStringAsync(Ct)).ShouldNotContain(loja.ItemId.ToString());
        lista.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Sem o Mercado Pago conectado, a turma não abre a loja.</summary>
    [Fact]
    public async Task Loja_sem_mercado_pago_e_recusada()
    {
        var falso = new MercadoPagoFalso();
        await using var api = falso.Na(fabrica);
        var turma = await fabrica.TurmaComPlano();
        var presidente = Cliente(api, turma.Presidente.Cliente);

        var resposta = await presidente.PostAsJsonAsync("/api/v1/cobrancas/opcionais", Opcional(3, null, null, null), Json, Ct);

        await Problema(resposta, HttpStatusCode.Conflict, "loja.sem_mercado_pago");
    }

    /// <summary>
    /// 29/09/2026: na cobrança manual a loja continua vendendo pelo Mercado Pago, e por isso ele não se desconecta com
    /// item à venda — a vitrine ficaria de pé sem ter como pagar. Encerrado o item, desconecta.
    /// </summary>
    [Fact]
    public async Task Na_cobranca_manual_a_loja_vende_e_segura_a_desconexao()
    {
        // Arrange — conectada no manual, com um convite na loja
        await using var loja = await Montar(estoque: 3);
        var presidente = Cliente(loja.Api, loja.Turma.Presidente.Cliente);
        const string MercadoPago = "/api/v1/recebimentos/conta/mercado-pago";

        // Act
        var vitrine = await Ler<LojaDTO>(await loja.Anonimo.GetAsync($"/api/v1/loja/{loja.Turma.FormaturaId}", Ct));
        var comLoja = await presidente.DeleteAsync(MercadoPago, Ct);
        (await presidente.PostAsync($"/api/v1/cobrancas/opcionais/{loja.ItemId}/encerrar", null, Ct)).EnsureSuccessStatusCode();
        var semLoja = await presidente.DeleteAsync(MercadoPago, Ct);

        // Assert
        (await Ler<ProvedorDaTurmaDTO>(await presidente.GetAsync(MercadoPago, Ct))).Provedor.ShouldBeNull();
        vitrine.Meios.ShouldBe([MeioDePagamento.Pix]);
        await Problema(comLoja, HttpStatusCode.Conflict, "recebimento.loja_aberta");
        semLoja.StatusCode.ShouldBe(HttpStatusCode.NoContent, await semLoja.Content.ReadAsStringAsync(Ct));
    }

    private async Task<Loja> Montar(int? estoque, int? limite = null, long? precoPublico = null, DateTime? abertura = null, bool comFesta = false)
    {
        var falso = new MercadoPagoFalso();
        var api = falso.Na(fabrica);
        var turma = await fabrica.TurmaComPlano();
        var presidente = Cliente(api, turma.Presidente.Cliente);
        var anonimo = Cliente(api, null);

        await presidente.CadastrarChavePix(Ct);
        var autorizacao = await Ler<AutorizacaoDoProvedorDTO>(
            await presidente.PostAsync("/api/v1/recebimentos/conta/mercado-pago/autorizacao", null, Ct)
        );
        var state = HttpUtility.ParseQueryString(new Uri(autorizacao.Url).Query)["state"];
        (await anonimo.GetAsync($"/api/v1/mercado-pago/retorno?code=codigo&state={Uri.EscapeDataString(state!)}", Ct))
            .Headers.Location!.ToString()
            .ShouldContain("mercado_pago=conectado");

        var item = await Ler<ItemDeCobrancaDTO>(
            await presidente.PostAsJsonAsync("/api/v1/cobrancas/opcionais", Opcional(estoque, limite, precoPublico, abertura), Json, Ct)
        );
        item.ModoDeVenda.ShouldBe(ModoDeVenda.Publica);

        if (comFesta)
            (
                await presidente.PostAsJsonAsync(
                    "/api/v1/agenda",
                    new EventoRequestDTO(
                        "Festa de formatura",
                        TipoDeEvento.Festa,
                        SituacaoDoEvento.Confirmado,
                        DataUtils.Hoje().AddMonths(3),
                        new TimeOnly(22, 0),
                        "Espaço Vitrália",
                        null
                    ),
                    Json,
                    Ct
                )
            ).StatusCode.ShouldBe(HttpStatusCode.Created);

        return new Loja(falso, api, turma, item.Id, anonimo);
    }

    /// <summary>
    /// Sprint 39, P5: com o cartão ligado, o comprador escolhe cartão, paga pela tela da compra e recebe os convites na
    /// hora; a taxa repassada vira receita à parte. A contestação no cartão revoga os convites e estorna a venda.
    /// </summary>
    [Fact]
    public async Task Comprar_no_cartao_confirma_na_hora_e_a_contestacao_revoga_os_convites()
    {
        // Arrange
        await using var loja = await Montar(estoque: 3, limite: 2, precoPublico: 25_000, comFesta: true);
        var presidente = Cliente(loja.Api, loja.Turma.Presidente.Cliente);
        (
            await presidente.PutAsJsonAsync("/api/v1/recebimentos/conta/mercado-pago/cartao", new ConfiguracaoDoCartaoRequestDTO(true, 500), Json, Ct)
        ).EnsureSuccessStatusCode();

        // Act — a vitrine e a compra no cartão
        var vitrine = await Ler<LojaDTO>(await loja.Anonimo.GetAsync($"/api/v1/loja/{loja.Turma.FormaturaId}", Ct));
        var criada = await Ler<CompraCriadaDTO>(await Comprar(loja, Pedido(2, Email(), "529.982.247-25") with { Meio = MeioDePagamento.Cartao }));

        // Assert — reservou sem emitir nada: o cartão só é cobrado com os dados dele
        vitrine.Meios.ShouldBe([MeioDePagamento.Pix, MeioDePagamento.Cartao]);
        criada.Compra.Cobranca.ShouldBeNull();
        criada.Compra.Cartao!.ValorEmCentavos.ShouldBe((long)Math.Ceiling(50_000 / 0.95m));
        loja.Falso.Pedidos.ShouldBeEmpty();

        // Act — o comprador paga no cartão
        var paga = await Ler<CompraDTO>(
            await loja.Anonimo.PostAsJsonAsync(
                $"/api/v1/loja/compras/{criada.Token}/cartao",
                new CartaoDaCompraRequestDTO("tok-visa", "visa", 2, criada.Compra.Cartao.ValorEmCentavos),
                Json,
                Ct
            )
        );

        // Assert — paga na hora, com os convites, a venda pelo preço e o acréscimo à parte
        paga.Status.ShouldBe(StatusDaCompra.Paga);
        paga.Convites.Count().ShouldBe(2);
        paga.Cartao.ShouldBeNull();
        await using (var contexto = fabrica.ContextoDe(loja.Turma.FormaturaId))
        {
            var receitas = await contexto.OutrasReceitas.OrderBy(r => r.ValorEmCentavos).ToListAsync(Ct);
            receitas.Select(r => r.ValorEmCentavos).ShouldBe([criada.Compra.Cartao.AcrescimoEmCentavos, 50_000]);
            receitas[1].Categoria.ShouldBe(CategoriaDeOutraReceita.VendaDeConvite);
        }

        // Act — o comprador contesta no cartão, e o Mercado Pago avisa
        var pedido = loja.Falso.Pedidos.Single().Key;
        loja.Falso.Devolver(pedido, SituacaoDoPedido.Contestado);
        (await Avisar(loja, pedido)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var devolvida = await Ler<CompraDTO>(await loja.Anonimo.GetAsync($"/api/v1/loja/compras/{criada.Token}", Ct));

        // Assert — convites revogados, venda estornada, e a compra fora da lista a devolver: o dinheiro já voltou
        devolvida.Status.ShouldBe(StatusDaCompra.Devolvida);
        devolvida.Convites.ShouldBeEmpty();
        devolvida.ValorADevolverEmCentavos.ShouldBe(0);
        await using var depois = fabrica.ContextoDe(loja.Turma.FormaturaId);
        (await depois.OutrasReceitas.Where(r => r.Categoria == CategoriaDeOutraReceita.VendaDeConvite).SumAsync(r => r.ValorEmCentavos, Ct)).ShouldBe(
            0
        );
    }

    private static OpcionalRequestDTO Opcional(int? estoque, int? limite, long? precoPublico, DateTime? abertura) =>
        new(
            TipoDeCobranca.ConviteExtra,
            "Convite adulto",
            20_000,
            1,
            10,
            DataUtils.Hoje().AddMonths(1),
            limite,
            null,
            estoque,
            abertura,
            null,
            ModoDeVenda.Publica,
            precoPublico
        );

    private static CompraRequestDTO Pedido(int quantidade, string email, string cpf) =>
        new(
            Guid.Empty,
            quantidade,
            "Maria Souza",
            email,
            cpf,
            MeioDePagamento.Pix,
            Guid.CreateVersion7(),
            [.. Enumerable.Range(1, quantidade).Select(i => new ConvidadoRequestDTO($"Convidado {i}", TipoDeDocumento.Rg, "1234567", null))]
        );

    private static Task<HttpResponseMessage> Comprar(Loja loja, CompraRequestDTO pedido) =>
        loja.Anonimo.PostAsJsonAsync($"/api/v1/loja/{loja.Turma.FormaturaId}/compras", pedido with { ItemDeCobrancaId = loja.ItemId }, Json, Ct);

    private static Task<HttpResponseMessage> Avisar(Loja loja, string idDoPedido)
    {
        var aviso = new HttpRequestMessage(HttpMethod.Post, $"{Aviso}?data.id={idDoPedido.ToLowerInvariant()}&type=order");
        aviso.Headers.Add("x-signature", MercadoPagoFalso.AssinaturaValida);
        aviso.Headers.Add("x-request-id", "req-1");

        return loja.Anonimo.SendAsync(aviso, Ct);
    }

    private async Task<int> Reservados(Loja loja)
    {
        await using var contexto = fabrica.ContextoDe(loja.Turma.FormaturaId);

        return (await contexto.ItensDeCobranca.AsNoTracking().SingleAsync(i => i.Id == loja.ItemId, Ct)).Reservados;
    }

    private async Task Vencer(Loja loja, Guid compraId)
    {
        await using var contexto = fabrica.ContextoDe(loja.Turma.FormaturaId);
        await contexto
            .ComprasDeConvite.Where(c => c.Id == compraId)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.ExpiraEm, DateTime.UtcNow.AddMinutes(-1)), Ct);
    }

    private static string Email() => $"comprador-{Guid.NewGuid():N}@teste.dev";

    private static async Task Problema(HttpResponseMessage resposta, HttpStatusCode status, string trecho)
    {
        var corpo = await resposta.Content.ReadAsStringAsync(Ct);
        resposta.StatusCode.ShouldBe(status, corpo);
        corpo.ShouldContain(trecho);
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
