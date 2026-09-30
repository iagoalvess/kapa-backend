using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Agenda;
using Backend.Api.DTOs.Financeiro;
using Backend.Api.DTOs.Formaturas;
using Backend.Business.Agenda.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Mvc;
using Shouldly;

namespace Backend.IntegrationTests.Agenda;

/// <summary>
/// A agenda contra a API e o Postgres de verdade: quem pode chamar cada endpoint e os critérios de
/// aceite da Sprint 19 — colação e festa são únicas por turma, as duas datas do detalhe da formatura
/// passam a sair daqui, e mover a colação move a janela da projeção do caixa.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class AgendaEndpointsTests(ApiFactory fabrica)
{
    private const string Agenda = "/api/v1/agenda";
    private const string FormaturaAtual = "/api/v1/formaturas/atual";
    private const string Projecao = "/api/v1/financeiro/caixa/projecao";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Hoje => DataUtils.Hoje();

    /// <summary>
    /// Ler é de todo membro; escrever é da Gestão (P3).
    /// </summary>
    /// <remarks>
    /// Marcar reunião não é decisão de presidente — é a mesma regra do mural e dos itens da festa.
    /// O formando lê a agenda inteira e não escreve nada nela.
    /// </remarks>
    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.Created)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.Created)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Created)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Todo_membro_le_a_agenda_mas_so_a_gestao_escreve(string papel, HttpStatusCode escrita)
    {
        var membro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), papel, Ct);

        (await membro.Cliente.GetAsync(Agenda, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await membro.Cliente.PostAsJsonAsync(Agenda, Novo("Reunião da comissão"), Json, Ct)).StatusCode.ShouldBe(escrita);
    }

    [Fact]
    public async Task O_formando_nao_altera_nem_exclui_evento()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var evento = await Criar(presidente, Novo("Reunião da comissão"), Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        (await formando.Cliente.PutAsJsonAsync($"{Agenda}/{evento.Id}", Novo("Outra reunião"), Json, Ct)).StatusCode.ShouldBe(
            HttpStatusCode.Forbidden
        );
        (await formando.Cliente.DeleteAsync($"{Agenda}/{evento.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await formando.Cliente.GetAsync($"{Agenda}/{evento.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Evento_de_outra_turma_devolve_404_e_nao_403()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var evento = await Criar(presidente, Novo("Reunião da outra turma"), Ct);

        var vizinha = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        (await vizinha.Cliente.GetAsync($"{Agenda}/{evento.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await vizinha.Cliente.DeleteAsync($"{Agenda}/{evento.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A turma tem uma colação, ou nenhuma — e a que existe se move.
    /// </summary>
    /// <remarks>
    /// O 409 vem do service, com código; a <b>garantia</b> é o índice único parcial, e é ele que
    /// responde quando dois cliques chegam juntos (decisão 2). O caminho de reparo é o mesmo que a
    /// mensagem sugere: alterar a data da que já existe.
    /// </remarks>
    [Fact]
    public async Task Segunda_colacao_devolve_409_e_a_primeira_continua_movel()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var colacao = await Criar(presidente, Novo("Colação de grau", TipoDeEvento.Colacao, Hoje.AddDays(300)), Ct);

        var segunda = await presidente.Cliente.PostAsJsonAsync(Agenda, Novo("Outra colação", TipoDeEvento.Colacao), Json, Ct);

        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await segunda.Content.ReadFromJsonAsync<ProblemDetails>(Json, Ct))!.Extensions["codigo"]!.ToString().ShouldBe("agenda.tipo_unico");

        var movida = await presidente.Cliente.PutAsJsonAsync(
            $"{Agenda}/{colacao.Id}",
            Novo("Colação de grau", TipoDeEvento.Colacao, Hoje.AddDays(330)),
            Json,
            Ct
        );

        movida.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await movida.Content.ReadFromJsonAsync<EventoDTO>(Json, Ct))!.Data.ShouldBe(Hoje.AddDays(330));
    }

    /// <summary>Reunião e prazo se repetem à vontade: só colação e festa são únicas.</summary>
    [Fact]
    public async Task Duas_reunioes_convivem_na_mesma_agenda()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        await Criar(presidente, Novo("Primeira reunião", TipoDeEvento.Reuniao, Hoje.AddDays(10)), Ct);
        await Criar(presidente, Novo("Segunda reunião", TipoDeEvento.Reuniao, Hoje.AddDays(20)), Ct);

        (await Listar(presidente, Ct)).Count(evento => evento.Tipo == TipoDeEvento.Reuniao).ShouldBe(2);
    }

    /// <summary>
    /// O critério de aceite da decisão 1: o detalhe da formatura lê as duas datas da agenda.
    /// </summary>
    /// <remarks>
    /// É o que faz o contador do Início, os três marcos e a faixa da turma continuarem certos sem
    /// nenhuma alteração neles — e o que prova que as colunas saíram sem levar o contrato junto.
    /// Apagar o evento devolve o campo a nulo, como a turma que nunca informou a data.
    /// </remarks>
    [Fact]
    public async Task As_duas_datas_do_detalhe_da_formatura_saem_da_agenda()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        var antes = await Detalhe(presidente, Ct);
        antes.PrevisaoDeColacao.ShouldBeNull();
        antes.PrevisaoDaFesta.ShouldBeNull();

        var colacao = await Criar(presidente, Novo("Colação de grau", TipoDeEvento.Colacao, Hoje.AddDays(300)), Ct);
        await Criar(presidente, Novo("Festa de formatura", TipoDeEvento.Festa, Hoje.AddDays(310)), Ct);

        var comAgenda = await Detalhe(presidente, Ct);
        comAgenda.PrevisaoDeColacao.ShouldBe(Hoje.AddDays(300));
        comAgenda.PrevisaoDaFesta.ShouldBe(Hoje.AddDays(310));

        (await presidente.Cliente.DeleteAsync($"{Agenda}/{colacao.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var semColacao = await Detalhe(presidente, Ct);
        semColacao.PrevisaoDeColacao.ShouldBeNull();
        semColacao.PrevisaoDaFesta.ShouldBe(Hoje.AddDays(310));
    }

    /// <summary>A data de uma turma não aparece no detalhe de outra.</summary>
    [Fact]
    public async Task A_colacao_de_uma_turma_nao_vaza_para_o_detalhe_da_vizinha()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        await Criar(presidente, Novo("Colação de grau", TipoDeEvento.Colacao, Hoje.AddDays(300)), Ct);

        var vizinha = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        (await Detalhe(vizinha, Ct)).PrevisaoDeColacao.ShouldBeNull();
        (await Listar(vizinha, Ct)).ShouldBeEmpty();
    }

    /// <summary>
    /// Mover a colação move o fim da janela da projeção do caixa, sem nenhuma outra escrita.
    /// </summary>
    /// <remarks>
    /// O <c>CaixaService</c> não mudou uma linha nesta sprint: ele continua lendo
    /// <c>PrevisaoDeColacao</c> do detalhe da formatura, que agora é projeção da agenda. Este teste
    /// é o que prova que a troca foi mesmo transparente.
    /// </remarks>
    [Fact]
    public async Task A_projecao_do_caixa_vai_ate_o_mes_da_colacao_marcada_na_agenda()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        var semColacao = await Projetar(presidente, Ct);
        semColacao.Meses.Count.ShouldBe(1);

        var colacao = Hoje.AddMonths(5);
        await Criar(presidente, Novo("Colação de grau", TipoDeEvento.Colacao, colacao), Ct);

        var comColacao = await Projetar(presidente, Ct);
        comColacao.Meses.Count.ShouldBe(6);
        comColacao.Meses[^1].Mes.ShouldBe(new DateOnly(colacao.Year, colacao.Month, 1));
    }

    /// <summary>
    /// Cancelado continua na lista, e a lista sai do mais antigo para o mais novo.
    /// </summary>
    /// <remarks>
    /// Sem hora vem antes de quem tem hora no mesmo dia, que é como se lê a lista do dia — e é o
    /// desempate que impede a ordem de mudar entre duas aberturas da tela.
    /// </remarks>
    [Fact]
    public async Task A_lista_vem_por_data_e_o_cancelado_continua_nela()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        await Criar(presidente, Novo("Depois", TipoDeEvento.Reuniao, Hoje.AddDays(20)), Ct);
        var desmarcada = await Criar(presidente, Novo("Antes", TipoDeEvento.Reuniao, Hoje.AddDays(10)), Ct);

        var cancelamento = await presidente.Cliente.PutAsJsonAsync(
            $"{Agenda}/{desmarcada.Id}",
            Novo("Antes", TipoDeEvento.Reuniao, Hoje.AddDays(10)) with
            {
                Situacao = SituacaoDoEvento.Cancelado,
            },
            Json,
            Ct
        );
        cancelamento.EnsureSuccessStatusCode();

        var lista = await Listar(presidente, Ct);
        lista.Select(evento => evento.Titulo).ShouldBe(["Antes", "Depois"]);
        lista[0].Situacao.ShouldBe(SituacaoDoEvento.Cancelado);
    }

    /// <summary>A hora viaja sem fuso: o que a comissão digitou é o que a turma lê (decisão 3).</summary>
    [Fact]
    public async Task A_hora_volta_igual_a_que_foi_informada()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        var evento = await Criar(
            presidente,
            Novo("Reunião da comissão", TipoDeEvento.Reuniao, Hoje.AddDays(3)) with
            {
                Hora = new TimeOnly(19, 30),
                Local = "Bloco A",
            },
            Ct
        );

        evento.Hora.ShouldBe(new TimeOnly(19, 30));
        (await Listar(presidente, Ct))[0].Hora.ShouldBe(new TimeOnly(19, 30));
    }

    [Fact]
    public async Task Turma_suspensa_le_a_agenda_e_nao_escreve_nela()
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Suspensa, Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        (await presidente.Cliente.GetAsync(Agenda, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var escrita = await presidente.Cliente.PostAsJsonAsync(Agenda, Novo("Reunião da comissão"), Json, Ct);
        escrita.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await escrita.Content.ReadFromJsonAsync<ProblemDetails>(Json, Ct))!.Extensions["codigo"]!.ToString().ShouldBe("formatura.inativa");
    }

    /// <summary>
    /// O resumo da Página Inicial: as próximas, quantas ainda vêm, e nada do que já passou.
    /// </summary>
    /// <remarks>
    /// Existe para a home não pedir a agenda inteira a cada abertura do app. Cancelado fica fora dos
    /// dois números — na tela da agenda ele continua visível, aqui o espaço é do que vai acontecer.
    /// </remarks>
    [Fact]
    public async Task O_resumo_traz_as_tres_proximas_e_conta_o_que_ainda_vem()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        await Criar(presidente, Novo("Assembleia passada", TipoDeEvento.Reuniao, Hoje.AddDays(-30)), Ct);
        await Criar(presidente, Novo("Hoje mesmo", TipoDeEvento.Reuniao, Hoje), Ct);
        await Criar(presidente, Novo("Prova da beca", TipoDeEvento.Prazo, Hoje.AddDays(5)), Ct);
        await Criar(presidente, Novo("Colação de grau", TipoDeEvento.Colacao, Hoje.AddDays(10)), Ct);
        await Criar(presidente, Novo("Festa de formatura", TipoDeEvento.Festa, Hoje.AddDays(20)), Ct);

        var desmarcada = await Criar(presidente, Novo("Reunião desmarcada", TipoDeEvento.Reuniao, Hoje.AddDays(1)), Ct);
        (
            await presidente.Cliente.PutAsJsonAsync(
                $"{Agenda}/{desmarcada.Id}",
                Novo("Reunião desmarcada", TipoDeEvento.Reuniao, Hoje.AddDays(1)) with
                {
                    Situacao = SituacaoDoEvento.Cancelado,
                },
                Json,
                Ct
            )
        ).EnsureSuccessStatusCode();

        var resumo = await Resumir(presidente, Ct);

        // Quatro ainda vêm (a de hoje conta, a cancelada não), e só três cabem na home.
        resumo.Proximos.Select(evento => evento.Titulo).ShouldBe(["Hoje mesmo", "Prova da beca", "Colação de grau"]);
    }

    [Fact]
    public async Task Turma_sem_agenda_tem_resumo_vazio_e_nao_404()
    {
        var formando = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Formando, Ct);

        var resumo = await Resumir(formando, Ct);

        resumo.Proximos.ShouldBeEmpty();
    }

    [Fact]
    public async Task Evento_sem_titulo_devolve_400()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.PostAsJsonAsync(Agenda, Novo(string.Empty), Json, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static EventoRequestDTO Novo(string titulo, TipoDeEvento tipo = TipoDeEvento.Reuniao, DateOnly? data = null) =>
        new(titulo, tipo, SituacaoDoEvento.AConfirmar, data ?? Hoje.AddDays(7), null, null, null);

    private static async Task<EventoDTO> Criar(MembroDeTeste membro, EventoRequestDTO corpo, CancellationToken ct)
    {
        var resposta = await membro.Cliente.PostAsJsonAsync(Agenda, corpo, Json, ct);
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<EventoDTO>(Json, ct))!;
    }

    private static async Task<ResumoDaAgendaDTO> Resumir(MembroDeTeste membro, CancellationToken ct) =>
        (await membro.Cliente.GetFromJsonAsync<ResumoDaAgendaDTO>($"{Agenda}/resumo", Json, ct))!;

    private static async Task<IReadOnlyList<EventoDTO>> Listar(MembroDeTeste membro, CancellationToken ct) =>
        (await membro.Cliente.GetFromJsonAsync<List<EventoDTO>>(Agenda, Json, ct))!;

    private static async Task<FormaturaDetalheDTO> Detalhe(MembroDeTeste membro, CancellationToken ct) =>
        (await membro.Cliente.GetFromJsonAsync<FormaturaDetalheDTO>(FormaturaAtual, Json, ct))!;

    private static async Task<ProjecaoDTO> Projetar(MembroDeTeste membro, CancellationToken ct) =>
        (await membro.Cliente.GetFromJsonAsync<ProjecaoDTO>(Projecao, Json, ct))!;
}
