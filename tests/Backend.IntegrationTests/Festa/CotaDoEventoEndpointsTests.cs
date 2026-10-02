using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backend.Api.DTOs.Agenda;
using Backend.Api.DTOs.Auth;
using Backend.Api.DTOs.Convites;
using Backend.Api.DTOs.Festa;
using Backend.Api.DTOs.Formaturas;
using Backend.Business.Agenda.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Festa.Models;
using Backend.Business.Festa.Services;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Backend.IntegrationTests.Festa;

/// <summary>
/// A cota de convites de um evento — a colação (Sprint 30) e a festa (01/10/2026) — contra a API e o
/// Postgres de verdade.
/// </summary>
/// <remarks>
/// A garantia "abrir de novo não duplica" é o índice único da posição e o <c>ON CONFLICT</c> da
/// instrução — por isso aqui, e não num dublê do banco. O convite de cota é o da Sprint 21: página,
/// portaria e revogação são as mesmas, e os testes de lá valem para ele.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class CotaDoEventoEndpointsTests(ApiFactory fabrica)
{
    private const string Cota = "/api/v1/festa/cota";
    private const string Convites = "/api/v1/festa/convites";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Uma turma com presidente e dois formandos com adesão, e a colação na agenda.</summary>
    private sealed record Cenario(TurmaDeTeste Turma, MembroDeTeste Ana, MembroDeTeste Bruno, Guid ColacaoId);

    /// <summary>P1 e critério 2: N por formando ativo numa instrução; abrir de novo não cria nada.</summary>
    [Fact]
    public async Task Abrir_emite_n_por_formando_ativo_e_reabrir_nao_duplica()
    {
        var cenario = await Montar();
        await Definir(cenario, 2, null);

        var primeira = await Abrir(cenario);
        var segunda = await Abrir(cenario);

        primeira.FormandosAtivos.ShouldBe(3);
        primeira.Emitidos.ShouldBe(6);
        primeira.AbertaEm.ShouldNotBeNull();
        segunda.Emitidos.ShouldBe(6);
        segunda.AbertaEm!.Value.ShouldBe(primeira.AbertaEm!.Value, TimeSpan.FromMilliseconds(1));
        (await ValidosDaCota(cenario.Turma.FormaturaId, cenario.ColacaoId)).ShouldBe(6);

        var meus = await Meus(cenario.Ana, TipoDeEvento.Colacao);
        meus.Convites.Select(c => c.Sequencial).ShouldBe([1, 2]);
        meus.Evento!.Id.ShouldBe(cenario.ColacaoId);
        meus.AguardandoPagamento.ShouldBe(0);
    }

    /// <summary>A cota vale para a festa desde 01/10/2026: emite os convites do evento da festa.</summary>
    [Fact]
    public async Task Cota_da_festa_emite_os_convites_da_festa()
    {
        var cenario = await Montar();
        var festaId = await CriarEvento(cenario.Turma.Presidente, TipoDeEvento.Festa, "Festa de formatura", TimeSpan.FromDays(60));

        await Definir(cenario, 3, null, TipoDeEvento.Festa);
        var aberta = await Abrir(cenario, TipoDeEvento.Festa);

        aberta.Evento.Id.ShouldBe(festaId);
        aberta.Emitidos.ShouldBe(9);
        (await ValidosDaCota(cenario.Turma.FormaturaId, festaId)).ShouldBe(9);

        var daFesta = await Meus(cenario.Ana, TipoDeEvento.Festa);
        daFesta.Convites.Select(c => c.Sequencial).ShouldBe([1, 2, 3]);
        daFesta.Evento!.Id.ShouldBe(festaId);

        (await Meus(cenario.Ana, TipoDeEvento.Colacao)).Convites.ShouldBeEmpty();
    }

    /// <summary>P1: quem entra pelo convite depois da abertura recebe a cota na entrada.</summary>
    [Fact]
    public async Task Quem_entra_pelo_convite_depois_da_abertura_recebe_a_cota_na_entrada()
    {
        var cenario = await Montar();
        await Definir(cenario, 2, null);
        await Abrir(cenario);

        var novo = await EntrarPeloConvite(cenario);

        var meus = await novo.GetFromJsonAsync<MeusConvitesDTO>($"{Convites}/meus?tipo=Colacao", Json, Ct);
        meus!.Convites.Count().ShouldBe(2);
        (await Abrir(cenario)).Emitidos.ShouldBe(8);
    }

    /// <summary>Critério 3: quem entrou sem passar pela emissão recebe a dele ao reabrir, e ninguém ganha a mais.</summary>
    [Fact]
    public async Task Reabrir_emite_so_para_quem_ainda_nao_tem()
    {
        var cenario = await Montar();
        await Definir(cenario, 2, null);
        await Abrir(cenario);
        var carla = await fabrica.NovoMembro(cenario.Turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        (await Meus(carla, TipoDeEvento.Colacao)).Convites.ShouldBeEmpty();

        var reaberta = await Abrir(cenario);

        reaberta.Emitidos.ShouldBe(8);
        (await Meus(carla, TipoDeEvento.Colacao)).Convites.Count().ShouldBe(2);
        (await Meus(cenario.Ana, TipoDeEvento.Colacao)).Convites.Count().ShouldBe(2);
    }

    /// <summary>Decisão 3 e P3: cota × formandos + cortesias acima da capacidade avisa e salva.</summary>
    [Fact]
    public async Task Capacidade_estourada_avisa_com_as_cortesias_e_salva()
    {
        var cenario = await Montar();
        var cortesia = await cenario.Turma.Presidente.Cliente.PostAsJsonAsync(
            $"{Convites}/cortesias",
            new CortesiaRequestDTO("Prof. Paraninfo", null, null, null, "Paraninfo da turma", cenario.ColacaoId),
            Json,
            Ct
        );
        cortesia.StatusCode.ShouldBe(HttpStatusCode.OK);

        var painel = await Definir(cenario, 2, 5);

        painel.Lugares.ShouldBe(7);
        painel.Cortesias.ShouldBe(1);
        painel.Excedente.ShouldBe(2);
        painel.Capacidade.ShouldBe(5);
        (await Abrir(cenario)).Emitidos.ShouldBe(6);
    }

    /// <summary>Depois de aberta, a cota só sobe: descer deixaria convites emitidos além dela.</summary>
    [Fact]
    public async Task Depois_de_aberta_a_cota_so_aumenta()
    {
        var cenario = await Montar();
        await Definir(cenario, 2, null);
        await Abrir(cenario);

        var descer = await Put(cenario, 1, null);
        await Definir(cenario, 3, null);

        descer.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await descer.Codigo(Ct)).ShouldBe("festa.cota_ja_aberta");
        (await Abrir(cenario)).Emitidos.ShouldBe(9);
    }

    /// <summary>Os erros do padrão: sem cota, sem hora e local, sem colação — e forma inválida.</summary>
    [Fact]
    public async Task Abrir_sem_cota_ou_sem_hora_e_local_e_recusado()
    {
        var turma = await fabrica.TurmaComPlano();
        var cliente = turma.Presidente.Cliente;

        (await cliente.GetAsync(Rota(TipoDeEvento.Colacao), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await CriarColacao(turma.Presidente, TimeSpan.FromDays(30), comHora: false);
        var semCota = await cliente.PostAsync(RotaDeAbertura(TipoDeEvento.Colacao), null, Ct);
        (await Put(turma, 11, null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Put(turma, 2, null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var semHora = await cliente.PostAsync(RotaDeAbertura(TipoDeEvento.Colacao), null, Ct);

        (await semCota.Codigo(Ct)).ShouldBe("festa.cota_nao_configurada");
        semHora.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await semHora.Codigo(Ct)).ShouldBe("festa.evento_incompleto");
    }

    /// <summary>Só a festa e a colação têm convite; outro tipo de evento não tem cota (01/10/2026).</summary>
    [Fact]
    public async Task Cota_so_existe_na_festa_e_na_colacao()
    {
        var turma = await fabrica.TurmaComPlano();
        var cliente = turma.Presidente.Cliente;

        (await cliente.GetAsync(Rota(TipoDeEvento.Reuniao), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await cliente.PutAsJsonAsync(Rota(TipoDeEvento.Prazo), new CotaRequestDTO(2, null), Json, Ct)).StatusCode.ShouldBe(
            HttpStatusCode.BadRequest
        );
        (await cliente.PostAsync(RotaDeAbertura(TipoDeEvento.Outro), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>O painel é da Gestão: o formando recebe 403.</summary>
    [Fact]
    public async Task Formando_nao_ve_nem_abre_a_cota()
    {
        var cenario = await Montar();

        (await cenario.Ana.Cliente.GetAsync(Rota(TipoDeEvento.Colacao), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await cenario.Ana.Cliente.PostAsync(RotaDeAbertura(TipoDeEvento.Colacao), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Decisão 5: desligar o formando revoga a cota dele, com motivo, e tira ele da conta.</summary>
    [Fact]
    public async Task Desligar_revoga_a_cota_com_motivo()
    {
        var cenario = await Montar();
        await Definir(cenario, 2, null);
        await Abrir(cenario);

        var desligar = await cenario.Turma.Presidente.Cliente.PostAsJsonAsync(
            $"/api/v1/formaturas/atual/membros/{cenario.Ana.UsuarioId}/desligar",
            new DesligarMembroRequestDTO(MotivoDeSaida.Trancamento, null, false),
            Json,
            Ct
        );
        desligar.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var contexto = fabrica.ContextoDe(cenario.Turma.FormaturaId);
        var vinculoId = await contexto.Vinculos.Where(v => v.UsuarioId == cenario.Ana.UsuarioId).Select(v => v.Id).SingleAsync(Ct);
        var daAna = await contexto.ConvitesDoEvento.Where(c => c.VinculoId == vinculoId).ToListAsync(Ct);
        daAna.Count.ShouldBe(2);
        daAna.ShouldAllBe(c => c.RevogadoEm != null && c.MotivoDaRevogacao == EmissaoDeConvites.MotivoDaSaida);

        var painel = await Painel(cenario);
        painel.FormandosAtivos.ShouldBe(2);
        painel.Emitidos.ShouldBe(4);
    }

    /// <summary>
    /// Critérios 4 e 7: o convite de cota passa pela portaria sem tratamento especial, e a porta da
    /// colação não valida o da festa, nem a da festa o da colação. P1: sem nome no fechamento, fora da lista.
    /// </summary>
    [Fact]
    public async Task Portaria_da_colacao_valida_so_o_convite_dela_e_esconde_a_cota_sem_nome()
    {
        var cenario = await Montar(colacaoEm: TimeSpan.FromHours(1));
        var festaId = await CriarEvento(cenario.Turma.Presidente, TipoDeEvento.Festa, "Festa de formatura", TimeSpan.FromHours(2));
        await Definir(cenario, 2, null);
        await Abrir(cenario);
        var gestao = cenario.Turma.Presidente.Cliente;
        var convite = (await Meus(cenario.Ana, TipoDeEvento.Colacao)).Convites.First();
        (
            await gestao.PutAsJsonAsync(
                $"{Convites}/{convite.Id}/convidado",
                new ConvidadoRequestDTO("Tia Rosa", TipoDeDocumento.Rg, "7654321", null),
                Json,
                Ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.OK);

        var lista = await gestao.GetFromJsonAsync<ListaDaPortariaDTO>("/api/v1/festa/portaria?tipo=Colacao", Json, Ct);
        var naFesta = await CheckIn(gestao, convite.Codigo, festaId);
        var naColacao = await CheckIn(gestao, convite.Codigo, cenario.ColacaoId);
        var pdf = await gestao.GetAsync("/api/v1/festa/portaria/pdf?tipo=Colacao", Ct);

        lista!.Evento.Id.ShouldBe(cenario.ColacaoId);
        lista.Convites.ShouldHaveSingleItem().Origem.ShouldBe(OrigemDoConvite.Cota);
        (await naFesta.Codigo(Ct)).ShouldBe("festa.outro_evento");
        naColacao.StatusCode.ShouldBe(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
    }

    private async Task<Cenario> Montar(TimeSpan? colacaoEm = null)
    {
        var turma = await fabrica.TurmaComPlano();
        var ana = await fabrica.FormandoComAdesao(turma.FormaturaId);
        var bruno = await fabrica.FormandoComAdesao(turma.FormaturaId);
        var colacaoId = await CriarColacao(turma.Presidente, colacaoEm ?? TimeSpan.FromDays(30));

        return new Cenario(turma, ana, bruno, colacaoId);
    }

    private static string Rota(TipoDeEvento tipo) => $"{Cota}?tipo={tipo}";

    private static string RotaDeAbertura(TipoDeEvento tipo) => $"{Cota}/abrir?tipo={tipo}";

    private static Task<Guid> CriarColacao(MembroDeTeste gestao, TimeSpan daqui, bool comHora = true) =>
        CriarEvento(gestao, TipoDeEvento.Colacao, "Colação de grau", daqui, comHora);

    /// <summary>Um evento único na agenda, começando daqui a <paramref name="daqui"/> no fuso da turma.</summary>
    private static async Task<Guid> CriarEvento(MembroDeTeste gestao, TipoDeEvento tipo, string titulo, TimeSpan daqui, bool comHora = true)
    {
        var inicio = DataUtils.ParaExibicao(DateTime.UtcNow + daqui);
        var resposta = await gestao.Cliente.PostAsJsonAsync(
            "/api/v1/agenda",
            new EventoRequestDTO(
                titulo,
                tipo,
                SituacaoDoEvento.Confirmado,
                DateOnly.FromDateTime(inicio),
                comHora ? new TimeOnly(inicio.Hour, inicio.Minute) : null,
                "Teatro Guaíra",
                null
            ),
            Json,
            Ct
        );
        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await resposta.Content.ReadFromJsonAsync<EventoDTO>(Json, Ct))!.Id;
    }

    /// <summary>Um formando novo que entra pelo link da turma — o caminho do <c>ConviteService.Aceitar</c>.</summary>
    private async Task<HttpClient> EntrarPeloConvite(Cenario cenario)
    {
        var criado = await cenario.Turma.Presidente.Cliente.PostAsJsonAsync(
            "/api/v1/formaturas/atual/convites",
            new CriarConviteRequestDTO(null, null),
            Json,
            Ct
        );
        var token = (await criado.Content.ReadFromJsonAsync<ConviteCriadoDTO>(Json, Ct))!.Link.Split('/').Last();
        var cliente = fabrica.CreateClient();
        cliente = cliente.ComToken((await cliente.RegistrarUsuarioComum(Ct)).AccessToken);

        var aceite = await cliente.PostAsync($"/api/v1/convites/{token}/aceitar", new StringContent("{}", Encoding.UTF8, "application/json"), Ct);
        aceite.StatusCode.ShouldBe(HttpStatusCode.OK);

        return cliente.ComToken((await aceite.Content.ReadFromJsonAsync<TokenResponseDTO>(Json, Ct))!.AccessToken);
    }

    private static Task<HttpResponseMessage> Put(Cenario cenario, int? cota, int? capacidade) => Put(cenario.Turma, cota, capacidade);

    private static Task<HttpResponseMessage> Put(TurmaDeTeste turma, int? cota, int? capacidade, TipoDeEvento tipo = TipoDeEvento.Colacao) =>
        turma.Presidente.Cliente.PutAsJsonAsync(Rota(tipo), new CotaRequestDTO(cota, capacidade), Json, Ct);

    private static async Task<PainelDaCotaDTO> Definir(Cenario cenario, int? cota, int? capacidade, TipoDeEvento tipo = TipoDeEvento.Colacao)
    {
        var resposta = await Put(cenario.Turma, cota, capacidade, tipo);
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await resposta.Content.ReadFromJsonAsync<PainelDaCotaDTO>(Json, Ct))!;
    }

    private static async Task<PainelDaCotaDTO> Abrir(Cenario cenario, TipoDeEvento tipo = TipoDeEvento.Colacao)
    {
        var resposta = await cenario.Turma.Presidente.Cliente.PostAsync(RotaDeAbertura(tipo), null, Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await resposta.Content.ReadFromJsonAsync<PainelDaCotaDTO>(Json, Ct))!;
    }

    private static async Task<PainelDaCotaDTO> Painel(Cenario cenario, TipoDeEvento tipo = TipoDeEvento.Colacao) =>
        (await cenario.Turma.Presidente.Cliente.GetFromJsonAsync<PainelDaCotaDTO>(Rota(tipo), Json, Ct))!;

    private static async Task<MeusConvitesDTO> Meus(MembroDeTeste membro, TipoDeEvento tipo) =>
        (await membro.Cliente.GetFromJsonAsync<MeusConvitesDTO>($"{Convites}/meus?tipo={tipo}", Json, Ct))!;

    private async Task<int> ValidosDaCota(Guid formaturaId, Guid eventoId)
    {
        await using var contexto = fabrica.ContextoDe(formaturaId);
        return await contexto.ConvitesDoEvento.CountAsync(c => c.EventoId == eventoId && c.PedidoId == null && c.RevogadoEm == null, Ct);
    }

    private static Task<HttpResponseMessage> CheckIn(HttpClient cliente, string codigo, Guid eventoId) =>
        cliente.PostAsJsonAsync($"{Convites}/{codigo}/check-in", new CheckInRequestDTO(eventoId), Json, Ct);
}
