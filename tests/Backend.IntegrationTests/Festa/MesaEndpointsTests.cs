using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Festa;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Festa.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;

namespace Backend.IntegrationTests.Festa;

/// <summary>
/// As mesas do jantar contra a API e o Postgres: cadastro, a mesa vendida, o mapa do salão e quem pode mexer.
/// </summary>
/// <remarks>
/// A trava das atribuições e o <c>CHECK</c> da mesa reservada moram no banco — é aqui que se provam.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class MesaEndpointsTests(ApiFactory fabrica)
{
    private const string Mesas = "/api/v1/festa/mesas";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Uma turma, um formando que pediu mesas e o id do vínculo dele.</summary>
    private sealed record Cenario(TurmaDeTeste Turma, MembroDeTeste Formando, Guid VinculoId, Guid PedidoId);

    [Fact]
    public async Task Cadastro_monta_a_faixa_e_recusa_nome_repetido_e_lugares_invalidos()
    {
        var turma = await fabrica.TurmaComPlano();
        var gestao = turma.Presidente.Cliente;

        await Criar(gestao, "Mesa 2", 8);
        await Criar(gestao, "Mesa 12", 10);
        await Criar(gestao, "Mesa dos pais", 12, reservada: true);
        var repetida = await gestao.PostAsJsonAsync(Mesas, new MesaRequestDTO("mesa 2", 8, null, null), Json, Ct);
        var semLugar = await gestao.PostAsJsonAsync(Mesas, new MesaRequestDTO("Mesa 3", 0, null, null), Json, Ct);

        repetida.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await repetida.Codigo(Ct)).ShouldBe("festa.identificacao_em_uso");
        semLugar.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var mapa = await Mapa(turma.Presidente);
        mapa.Mesas.ShouldBe(3);
        mapa.Lugares.ShouldBe(30);
        mapa.Reservadas.ShouldBe(1);
        mapa.Lista.Select(mesa => mesa.Identificacao).ShouldBe(["Mesa 2", "Mesa 12", "Mesa dos pais"]);
    }

    /// <summary>
    /// P9 (28/09/2026): o formando lê o mapa do salão, com a mesa dele marcada e sem o nome de dono
    /// nenhum; não lê o mapa da Gestão nem escreve.
    /// </summary>
    [Fact]
    public async Task Formando_ve_o_salao_sem_os_donos_com_a_mesa_dele_marcada_e_nao_escreve()
    {
        var cenario = await Montar(quantidade: 1);
        var formando = cenario.Formando.Cliente;

        (await formando.GetAsync(Mesas, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await formando.PostAsJsonAsync(Mesas, new MesaRequestDTO("Minha", 10, null, null), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await formando.PutAsJsonAsync($"{Mesas}/salao", new SalaoRequestDTO(2400, 1600, null, null), Json, Ct)).StatusCode.ShouldBe(
            HttpStatusCode.Forbidden
        );

        var dele = await Criar(cenario.Turma.Presidente.Cliente, "Mesa 5", 10);
        await Criar(cenario.Turma.Presidente.Cliente, "Mesa 6", 10);
        (await Atribuir(cenario.Turma.Presidente, dele.Id, cenario.VinculoId)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var resposta = await formando.GetAsync($"{Mesas}/salao", Ct);
        var corpo = await resposta.Content.ReadAsStringAsync(Ct);
        var salao = JsonSerializer.Deserialize<SalaoDoFormandoDTO>(corpo, Json)!;

        salao.Salao.Largura.ShouldBe(2400);
        salao.Mesas.Select(mesa => (mesa.Identificacao, mesa.Minha)).ShouldBe([("Mesa 5", true), ("Mesa 6", false)]);
        corpo.ShouldNotContain("vinculo_id");
        corpo.ShouldNotContain("dono");
    }

    /// <summary>O "Salvar mapa" grava o salão e move só as mesas que vieram; a que não veio fica onde estava.</summary>
    [Fact]
    public async Task Salvar_o_salao_grava_os_elementos_e_move_so_as_mesas_enviadas()
    {
        var turma = await fabrica.TurmaComPlano();
        var gestao = turma.Presidente.Cliente;
        var primeira = await Criar(gestao, "Mesa 1", 10);
        var segunda = await Criar(gestao, "Mesa 2", 8, formato: FormatoDaMesa.Retangular);
        var palco = new ElementoDoSalaoDTO(TipoDeElemento.Palco, "  Palco  ", 800, 0, 800, 300, null);
        var familia = new ElementoDoSalaoDTO(TipoDeElemento.Area, "Família", 0, 400, 600, 600, CorDaArea.Lilas);

        var primeiraVez = await gestao.PutAsJsonAsync(
            $"{Mesas}/salao",
            new SalaoRequestDTO(3000, 2000, [palco, familia], [new(primeira.Id, 300, 700, null), new(segunda.Id, 1500, 1200, true)]),
            Json,
            Ct
        );
        var segundaVez = await gestao.PutAsJsonAsync(
            $"{Mesas}/salao",
            new SalaoRequestDTO(3000, 2000, [palco], [new(primeira.Id, null, null, null)]),
            Json,
            Ct
        );

        primeiraVez.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        segundaVez.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var mapa = await Mapa(turma.Presidente);
        mapa.Salao.Largura.ShouldBe(3000);
        mapa.Salao.Elementos.ShouldHaveSingleItem().Rotulo.ShouldBe("Palco");
        var mesas = mapa.Lista.ToDictionary(mesa => mesa.Identificacao);
        (mesas["Mesa 1"].X, mesas["Mesa 1"].Y).ShouldBe((null, null));
        (mesas["Mesa 2"].X, mesas["Mesa 2"].Y, mesas["Mesa 2"].Girada, mesas["Mesa 2"].Formato).ShouldBe(
            (1500, 1200, true, FormatoDaMesa.Retangular)
        );
    }

    [Fact]
    public async Task Salao_com_elemento_ou_mesa_fora_dele_e_recusado_e_o_banco_exige_as_duas_coordenadas()
    {
        var turma = await fabrica.TurmaComPlano();
        var gestao = turma.Presidente.Cliente;
        var mesa = await Criar(gestao, "Mesa 1", 10);

        var elementoFora = await gestao.PutAsJsonAsync(
            $"{Mesas}/salao",
            new SalaoRequestDTO(1000, 1000, [new(TipoDeElemento.Pista, "Pista", 800, 0, 400, 400, null)], null),
            Json,
            Ct
        );
        var mesaFora = await gestao.PutAsJsonAsync(
            $"{Mesas}/salao",
            new SalaoRequestDTO(1000, 1000, null, [new(mesa.Id, 1200, 500, null)]),
            Json,
            Ct
        );
        var meiaCoordenada = await gestao.PutAsJsonAsync(
            $"{Mesas}/salao",
            new SalaoRequestDTO(1000, 1000, null, [new(mesa.Id, 500, null, null)]),
            Json,
            Ct
        );
        var salaoMinusculo = await gestao.PutAsJsonAsync($"{Mesas}/salao", new SalaoRequestDTO(100, 1000, null, null), Json, Ct);

        elementoFora.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        mesaFora.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        meiaCoordenada.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        salaoMinusculo.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        var erro = await Should.ThrowAsync<PostgresException>(() =>
            contexto.Database.ExecuteSqlAsync($"UPDATE mesas SET x = 10 WHERE id = {mesa.Id}", Ct)
        );
        erro.ConstraintName.ShouldBe("ck_mesas_posicao");
    }

    [Fact]
    public async Task Atribuir_alem_do_pedido_ou_mesa_reservada_e_recusado_e_mesa_com_dono_nao_se_exclui()
    {
        var cenario = await Montar(quantidade: 1);
        var gestao = cenario.Turma.Presidente;
        var primeira = await Criar(gestao.Cliente, "Mesa 1", 10);
        var segunda = await Criar(gestao.Cliente, "Mesa 2", 10);
        var reservada = await Criar(gestao.Cliente, "Mesa dos pais", 10, reservada: true);

        (await Atribuir(gestao, primeira.Id, cenario.VinculoId)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var alem = await Atribuir(gestao, segunda.Id, cenario.VinculoId);
        var naReservada = await Atribuir(gestao, reservada.Id, cenario.VinculoId);
        var excluir = await gestao.Cliente.DeleteAsync($"{Mesas}/{primeira.Id}", Ct);
        var reservarComDono = await gestao.Cliente.PutAsJsonAsync($"{Mesas}/{primeira.Id}", new MesaRequestDTO("Mesa 1", 10, null, true), Json, Ct);

        (await alem.Codigo(Ct)).ShouldBe("festa.mesas_alem_do_pedido");
        (await naReservada.Codigo(Ct)).ShouldBe("festa.mesa_reservada");
        (await excluir.Codigo(Ct)).ShouldBe("festa.mesa_com_dono");
        (await reservarComDono.Codigo(Ct)).ShouldBe("festa.mesa_reservada");

        var comprador = (await Mapa(gestao)).Compradores.Single();
        comprador.Compradas.ShouldBe(1);
        comprador.Atribuidas.ShouldBe(1);

        (await gestao.Cliente.DeleteAsync($"{Mesas}/{segunda.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>Dois da comissão dando a última mesa do formando no mesmo segundo: só um passa.</summary>
    [Fact]
    public async Task Duas_atribuicoes_concorrentes_nao_passam_do_que_ele_comprou()
    {
        var cenario = await Montar(quantidade: 1);
        var gestao = cenario.Turma.Presidente;
        var mesas = new List<MesaDTO>();
        for (var i = 1; i <= 6; i++)
            mesas.Add(await Criar(gestao.Cliente, $"Mesa {i}", 10));

        var respostas = await Task.WhenAll(mesas.Select(mesa => Atribuir(gestao, mesa.Id, cenario.VinculoId)));

        respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Conflict).ShouldBe(5);
        await using var contexto = fabrica.ContextoDe(cenario.Turma.FormaturaId);
        (await contexto.Mesas.CountAsync(mesa => mesa.VinculoId == cenario.VinculoId, Ct)).ShouldBe(1);
    }

    /// <summary>Decisão 6: cancelar o pedido de mesa solta o dono, e a mesa fica no mapa.</summary>
    [Fact]
    public async Task Cancelar_o_pedido_de_mesa_solta_o_dono_e_mantem_a_mesa()
    {
        var cenario = await Montar(quantidade: 1);
        var mesa = await Criar(cenario.Turma.Presidente.Cliente, "Mesa 7", 10);
        (await Atribuir(cenario.Turma.Presidente, mesa.Id, cenario.VinculoId)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await cenario.Formando.Cliente.PostAsync($"/api/v1/pedidos/{cenario.PedidoId}/cancelar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var mapa = await Mapa(cenario.Turma.Presidente);
        mapa.Lista.Single().VinculoId.ShouldBeNull();
        mapa.Compradores.ShouldBeEmpty();
    }

    /// <summary>P3, direto no banco: mesa reservada com dono é recusada pelo <c>CHECK</c>, sem passar pelo service.</summary>
    [Fact]
    public async Task Check_recusa_mesa_reservada_com_dono()
    {
        var cenario = await Montar(quantidade: 1);
        var mesa = await Criar(cenario.Turma.Presidente.Cliente, "Mesa dos pais", 10, reservada: true);

        await using var contexto = fabrica.ContextoDe(cenario.Turma.FormaturaId);
        var erro = await Should.ThrowAsync<PostgresException>(() =>
            contexto.Database.ExecuteSqlAsync($"UPDATE mesas SET vinculo_id = {cenario.VinculoId} WHERE id = {mesa.Id}", Ct)
        );

        erro.ConstraintName.ShouldBe("ck_mesas_reservada_sem_dono");
    }

    private async Task<Cenario> Montar(int quantidade)
    {
        var turma = await fabrica.TurmaComPlano();
        var criacao = await turma.Presidente.Cliente.PostAsJsonAsync(
            "/api/v1/cobrancas/opcionais",
            new OpcionalRequestDTO(TipoDeCobranca.Mesa, "Mesa de 10", 200_000, 1, 10, DataUtils.Hoje().AddMonths(1), null, null, null, null, null),
            Json,
            Ct
        );
        criacao.StatusCode.ShouldBe(HttpStatusCode.OK);
        var itemId = (await criacao.Content.ReadFromJsonAsync<ItemDeCobrancaDTO>(Json, Ct))!.Id;
        var formando = await fabrica.FormandoComAdesao(turma.FormaturaId);

        var pedido = await formando.Cliente.PostAsJsonAsync("/api/v1/pedidos", new PedidoRequestDTO(itemId, quantidade, 1), Json, Ct);
        pedido.StatusCode.ShouldBe(HttpStatusCode.OK);
        var pedidoId = (await pedido.Content.ReadFromJsonAsync<PedidoDTO>(Json, Ct))!.Id;

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        var vinculoId = (await contexto.Pedidos.SingleAsync(p => p.Id == pedidoId, Ct)).VinculoId;

        return new Cenario(turma, formando, vinculoId, pedidoId);
    }

    private static async Task<MesaDTO> Criar(
        HttpClient gestao,
        string identificacao,
        int lugares,
        bool reservada = false,
        FormatoDaMesa formato = FormatoDaMesa.Redonda
    )
    {
        var resposta = await gestao.PostAsJsonAsync(Mesas, new MesaRequestDTO(identificacao, lugares, null, reservada, formato), Json, Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await resposta.Content.ReadFromJsonAsync<MesaDTO>(Json, Ct))!;
    }

    private static Task<HttpResponseMessage> Atribuir(MembroDeTeste gestao, Guid mesaId, Guid? vinculoId) =>
        gestao.Cliente.PutAsJsonAsync($"{Mesas}/{mesaId}/dono", new DonoDaMesaRequestDTO(vinculoId), Json, Ct);

    private static async Task<MapaDeMesasDTO> Mapa(MembroDeTeste gestao) => (await gestao.Cliente.GetFromJsonAsync<MapaDeMesasDTO>(Mesas, Json, Ct))!;
}
