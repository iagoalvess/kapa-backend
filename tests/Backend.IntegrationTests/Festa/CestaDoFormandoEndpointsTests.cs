using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Adesoes;
using Backend.Api.DTOs.Agenda;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Convites;
using Backend.Api.DTOs.Festa;
using Backend.Api.DTOs.Formaturas;
using Backend.Business.Agenda.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Festa.Models;
using Backend.Business.Festa.Services;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using static Backend.IntegrationTests.Infra.AdesaoDeTeste;

namespace Backend.IntegrationTests.Festa;

/// <summary>
/// A cesta do formando (Sprint 47) contra a API e o Postgres de verdade: o que se cobra, os convites que os pacotes
/// concedem, o painel do evento, a trava por atraso, o gate de adesão e o convite que exige termo.
/// </summary>
/// <remarks>
/// A emissão é uma instrução só, com <c>ON CONFLICT</c> sobre o índice da posição — por isso aqui, e não num dublê.
/// O convite do pacote é o da Sprint 21: página, portaria e revogação são as mesmas.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class CestaDoFormandoEndpointsTests(ApiFactory fabrica)
{
    private const string Painel = "/api/v1/festa/painel-de-convites";
    private const string Convites = "/api/v1/festa/convites";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>O catálogo dos testes: duas faixas de festa, foto avulsa e colação.</summary>
    private sealed record Catalogo(TurmaDeTeste Turma, Guid Festa10, Guid Festa15, Guid Foto, Guid Colacao);

    private static DateOnly MesQueVem => DataUtils.Hoje().AddMonths(1);

    /// <summary>
    /// D2, D6, D14, D16 e D17: cada um deve a soma da própria cesta, a faixa concede os convites dela, e quem não
    /// escolheu a festa não recebe convite de nada.
    /// </summary>
    [Fact]
    public async Task Cada_formando_deve_so_a_propria_cesta_e_recebe_os_convites_do_pacote()
    {
        // Arrange
        var catalogo = await Montar();
        var festaId = await CriarEvento(catalogo.Turma.Presidente, TipoDeEvento.Festa, TimeSpan.FromDays(60));

        // Act
        var ana = await fabrica.FormandoComAdesao(catalogo.Turma.FormaturaId, catalogo.Festa15, catalogo.Colacao);
        var bruno = await fabrica.FormandoComAdesao(catalogo.Turma.FormaturaId, catalogo.Foto);

        // Assert
        (await Devido(catalogo, ana)).ShouldBe(420_000 + 30_000);
        (await Devido(catalogo, bruno)).ShouldBe(80_000);
        var daAna = (await Meus(ana, TipoDeEvento.Festa)).Convites.ToList();
        daAna.Count.ShouldBe(15);
        daAna.Single(convite => convite.Sequencial == 1).Documento.ShouldNotBeNull().ShouldStartWith("CPF");
        daAna.Count(convite => convite.NomeDoConvidado is null).ShouldBe(14);
        (await Meus(bruno, TipoDeEvento.Festa)).Convites.ShouldBeEmpty();

        var cesta = (await ana.Cliente.GetFromJsonAsync<MinhaAdesaoDTO>("/api/v1/adesoes/eu", Json, Ct))!.Adesao!.Plano.Cesta;
        cesta.ShouldNotBeNull().Select(pacote => pacote.ItemId).ShouldBe([catalogo.Festa15, catalogo.Colacao]);
        festaId.ShouldNotBe(Guid.Empty);
    }

    /// <summary>O evento que entra na agenda depois da adesão emite os convites de quem já tinha o pacote.</summary>
    [Fact]
    public async Task Colacao_marcada_depois_emite_os_convites_de_quem_ja_aderiu()
    {
        // Arrange
        var catalogo = await Montar();
        var ana = await fabrica.FormandoComAdesao(catalogo.Turma.FormaturaId, catalogo.Colacao);
        (await Meus(ana, TipoDeEvento.Colacao)).Convites.ShouldBeEmpty();

        // Act
        await CriarEvento(catalogo.Turma.Presidente, TipoDeEvento.Colacao, TimeSpan.FromDays(30));

        // Assert
        (await Meus(ana, TipoDeEvento.Colacao))
            .Convites.Select(convite => (convite.Sequencial, ComNome: convite.NomeDoConvidado is not null))
            .ShouldBe([(1, true), (2, false), (3, false)]);
    }

    /// <summary>D32 e D33: uma faixa por grupo, e ao menos um pacote para aderir.</summary>
    [Fact]
    public async Task Cesta_com_duas_faixas_ou_vazia_e_recusada()
    {
        // Arrange
        var catalogo = await Montar();
        var formando = await fabrica.NovoMembro(catalogo.Turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        await PreencherCadastro(formando.Cliente, NovoCpf());

        // Act
        var duasFaixas = await formando.Cliente.GetAsync(Termo([catalogo.Festa10, catalogo.Festa15]), Ct);
        var vazia = await Aderir(fabrica, formando.Cliente, pacotes: []);

        // Assert
        (await duasFaixas.Codigo(Ct)).ShouldBe("cobranca.faixa_invalida");
        vazia.Resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await vazia.Resposta.Codigo(Ct)).ShouldBe("adesao.cesta_sem_escolha");
    }

    /// <summary>
    /// D18: com o termo publicado, o formando sem adesão só alcança o termo e o cadastro — e a API recusa o atalho pela
    /// URL. A comissão não é barrada.
    /// </summary>
    [Fact]
    public async Task Formando_sem_adesao_so_ve_o_termo_e_o_cadastro()
    {
        // Arrange
        var catalogo = await Montar();
        var formando = await fabrica.NovoMembro(catalogo.Turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        var comissao = await fabrica.NovoMembro(catalogo.Turma.FormaturaId, PapelNaFormatura.Comissao, Ct);

        // Act
        var agendaAntes = await formando.Cliente.GetAsync("/api/v1/agenda", Ct);
        var termo = await formando.Cliente.GetAsync("/api/v1/adesoes/termos/vigente", Ct);
        var cadastro = await formando.Cliente.GetAsync("/api/v1/formandos/eu", Ct);
        var moldura = await formando.Cliente.GetAsync("/api/v1/formaturas/atual", Ct);
        await PreencherCadastro(formando.Cliente, NovoCpf());
        (await Aderir(fabrica, formando.Cliente)).Resposta.StatusCode.ShouldBe(HttpStatusCode.Created);
        var agendaDepois = await formando.Cliente.GetAsync("/api/v1/agenda", Ct);

        // Assert
        agendaAntes.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await agendaAntes.Codigo(Ct)).ShouldBe("adesao.pendente");
        termo.StatusCode.ShouldBe(HttpStatusCode.OK);
        cadastro.StatusCode.ShouldBe(HttpStatusCode.OK);
        moldura.StatusCode.ShouldBe(HttpStatusCode.OK);
        agendaDepois.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await comissao.Cliente.GetAsync("/api/v1/agenda", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>D34: sem termo e catálogo, convidar formando é recusado; a comissão continua podendo entrar.</summary>
    [Fact]
    public async Task Convidar_formando_exige_termo_e_plano()
    {
        // Arrange
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        // Act
        var link = await presidente.Cliente.PostAsJsonAsync("/api/v1/formaturas/atual/convites", new CriarConviteRequestDTO(null, null), Json, Ct);
        var tesoureiro = await presidente.Cliente.PostAsJsonAsync(
            "/api/v1/formaturas/atual/convites",
            new CriarConviteRequestDTO($"{Guid.NewGuid():N}@kapa.dev", PapelNaFormatura.Tesoureiro),
            Json,
            Ct
        );

        // Assert
        link.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await link.Codigo(Ct)).ShouldBe("convite.sem_termo_ou_plano");
        tesoureiro.IsSuccessStatusCode.ShouldBeTrue();
    }

    /// <summary>
    /// D24: o convite do pacote nasce preso enquanto houver parcela vencida além da carência; a comissão vê quem está
    /// preso e libera, e o mesmo código passa a entrar.
    /// </summary>
    [Fact]
    public async Task Convite_preso_por_atraso_nao_entra_ate_a_comissao_liberar()
    {
        // Arrange
        var catalogo = await Montar();
        var festaId = await CriarEvento(catalogo.Turma.Presidente, TipoDeEvento.Festa, TimeSpan.FromHours(1));
        var ana = await fabrica.FormandoComAdesao(catalogo.Turma.FormaturaId, catalogo.Festa10);
        var gestao = catalogo.Turma.Presidente.Cliente;
        var convite = (await Meus(ana, TipoDeEvento.Festa)).Convites.First(c => c.NomeDoConvidado is null);
        (
            await gestao.PutAsJsonAsync(
                $"{Convites}/{convite.Id}/convidado",
                new ConvidadoRequestDTO("Tia Rosa", TipoDeDocumento.Rg, "7654321", null),
                Json,
                Ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.OK);
        await VencerParcelas(catalogo, ana);

        // Act
        var presa = await CheckIn(gestao, convite.Codigo, festaId);
        var painel = await Obter(catalogo);
        var liberacao = await gestao.PostAsync($"{Painel}/presos/{painel.Presos.Single().VinculoId}/liberacao", null, Ct);
        var liberada = await CheckIn(gestao, convite.Codigo, festaId);

        // Assert
        presa.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await presa.Codigo(Ct)).ShouldBe("festa.convite_preso");
        painel.Presos.ShouldHaveSingleItem().Convites.ShouldBe(10);
        (await liberacao.Content.ReadFromJsonAsync<LiberacaoDeConvitesDTO>(Json, Ct))!.Liberados.ShouldBe(10);
        liberada.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Obter(catalogo)).Presos.ShouldBeEmpty();
    }

    /// <summary>D15: a conta de lugares lê os benefícios das cestas e as cortesias; passar da capacidade avisa e salva.</summary>
    [Fact]
    public async Task Painel_soma_os_beneficios_das_cestas_e_avisa_o_excedente()
    {
        // Arrange
        var catalogo = await Montar();
        await CriarEvento(catalogo.Turma.Presidente, TipoDeEvento.Festa, TimeSpan.FromDays(60));
        var ana = await fabrica.FormandoComAdesao(catalogo.Turma.FormaturaId, catalogo.Festa15);
        await fabrica.FormandoComAdesao(catalogo.Turma.FormaturaId, catalogo.Festa10);
        await fabrica.FormandoComAdesao(catalogo.Turma.FormaturaId, catalogo.Foto);

        // Act
        var resposta = await catalogo.Turma.Presidente.Cliente.PutAsJsonAsync(
            $"{Painel}/capacidade?tipo=Festa",
            new CapacidadeRequestDTO(20),
            Json,
            Ct
        );

        // Assert
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var painel = (await resposta.Content.ReadFromJsonAsync<PainelDeConvitesDTO>(Json, Ct))!;
        painel.Beneficios.ShouldBe(25);
        painel.Emitidos.ShouldBe(25);
        painel.Lugares.ShouldBe(25);
        painel.Excedente.ShouldBe(5);
        (await ana.Cliente.GetAsync($"{Painel}?tipo=Festa", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await catalogo.Turma.Presidente.Cliente.GetAsync($"{Painel}?tipo=Reuniao", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>Quem sai da turma não leva ninguém: os convites do pacote caem com motivo (Sprint 30, decisão 5).</summary>
    [Fact]
    public async Task Desligar_revoga_os_convites_do_pacote()
    {
        // Arrange
        var catalogo = await Montar();
        await CriarEvento(catalogo.Turma.Presidente, TipoDeEvento.Festa, TimeSpan.FromDays(60));
        var ana = await fabrica.FormandoComAdesao(catalogo.Turma.FormaturaId, catalogo.Festa10);

        // Act
        var desligar = await catalogo.Turma.Presidente.Cliente.PostAsJsonAsync(
            $"/api/v1/formaturas/atual/membros/{ana.UsuarioId}/desligar",
            new DesligarMembroRequestDTO(MotivoDeSaida.Trancamento, null, false),
            Json,
            Ct
        );

        // Assert
        desligar.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var contexto = fabrica.ContextoDe(catalogo.Turma.FormaturaId);
        var vinculoId = await contexto.Vinculos.Where(v => v.UsuarioId == ana.UsuarioId).Select(v => v.Id).SingleAsync(Ct);
        var daAna = await contexto.ConvitesDoEvento.Where(c => c.VinculoId == vinculoId).ToListAsync(Ct);
        daAna.Count.ShouldBe(10);
        daAna.ShouldAllBe(c => c.RevogadoEm != null && c.MotivoDaRevogacao == EmissaoDeConvites.MotivoDaSaida);
        (await Obter(catalogo)).Beneficios.ShouldBe(0);
    }

    /// <summary>D28/D35: a grade do pacote não passa do último vencimento que a comissão definiu.</summary>
    [Fact]
    public async Task Pacote_que_passa_do_ultimo_vencimento_e_recusado()
    {
        // Arrange
        var catalogo = await Montar();
        var planoId = (
            await catalogo.Turma.Presidente.Cliente.GetFromJsonAsync<List<PlanoDeCobrancaResumoDTO>>("/api/v1/cobrancas/planos", Json, Ct)
        )![0].Id;

        // Act
        var resposta = await catalogo.Turma.Presidente.Cliente.PostAsJsonAsync(
            $"/api/v1/cobrancas/planos/{planoId}/itens",
            Pacote(TipoDeCobranca.Festa, "20 pessoas", 540_000, "Festa", festa: 20) with
            {
                NumeroDeParcelas = 12,
                UltimoVencimento = MesQueVem.AddMonths(3),
            },
            Json,
            Ct
        );

        // Assert
        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await resposta.Codigo(Ct)).ShouldBe("cobranca.ultima_parcela_depois_do_limite");
    }

    /// <summary>D6 e D16: quem não vai à festa contrata só foto e colação, deve só os dois e recebe só o convite da colação.</summary>
    [Fact]
    public async Task Cesta_de_foto_e_colacao_deve_os_dois_e_recebe_so_a_colacao()
    {
        // Arrange
        var catalogo = await Montar();
        var carla = await fabrica.FormandoComAdesao(catalogo.Turma.FormaturaId, catalogo.Foto, catalogo.Colacao);

        // Act
        await CriarEvento(catalogo.Turma.Presidente, TipoDeEvento.Festa, TimeSpan.FromDays(60));
        await CriarEvento(catalogo.Turma.Presidente, TipoDeEvento.Colacao, TimeSpan.FromDays(30));

        // Assert
        var cesta = (await carla.Cliente.GetFromJsonAsync<MinhaAdesaoDTO>("/api/v1/adesoes/eu", Json, Ct))!.Adesao!.Plano.Cesta;
        cesta.ShouldNotBeNull().Select(pacote => pacote.ItemId).ShouldBe([catalogo.Foto, catalogo.Colacao], ignoreOrder: true);
        cesta.Sum(pacote => pacote.ConvitesDaFesta).ShouldBe(0);
        (await Devido(catalogo, carla)).ShouldBe(80_000 + 30_000);
        (await Meus(carla, TipoDeEvento.Colacao)).Convites.Count().ShouldBe(3);
        (await Meus(carla, TipoDeEvento.Festa)).Convites.ShouldBeEmpty();
    }

    /// <summary>D4: o avulso comprado depois vira pedido — o snapshot, o hash do aceite e a cesta ficam como foram assinados.</summary>
    [Fact]
    public async Task Pedido_depois_da_adesao_nao_altera_snapshot_hash_nem_cesta()
    {
        // Arrange
        var catalogo = await Montar();
        var ana = await fabrica.FormandoComAdesao(catalogo.Turma.FormaturaId, catalogo.Festa10);
        var itemId = await CriarConviteExtra(catalogo);
        var antes = await Assinado(catalogo, ana);
        var minhaAntes = await ana.Cliente.GetStringAsync("/api/v1/adesoes/eu", Ct);

        // Act
        var pedido = await ana.Cliente.PostAsJsonAsync("/api/v1/pedidos", new PedidoRequestDTO(itemId, 2, 1), Json, Ct);

        // Assert
        pedido.StatusCode.ShouldBe(HttpStatusCode.OK);
        var depois = await Assinado(catalogo, ana);
        depois.Hash.ShouldBe(antes.Hash);
        depois.Snapshot.ShouldBe(antes.Snapshot);
        depois.Escolhas.ShouldBe(antes.Escolhas);
        (await ana.Cliente.GetStringAsync("/api/v1/adesoes/eu", Ct)).ShouldBe(minhaAntes);
        (await Devido(catalogo, ana)).ShouldBe(300_000 + 2 * 18_000);
    }

    /// <summary>A cesta está dentro do hash: quem leu o termo com uma faixa não aceita com outra usando o mesmo hash.</summary>
    [Fact]
    public async Task Aceitar_com_cesta_diferente_da_lida_e_recusado()
    {
        // Arrange
        var catalogo = await Montar();
        var formando = await fabrica.NovoMembro(catalogo.Turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        await PreencherCadastro(formando.Cliente, NovoCpf());
        var lido = (await formando.Cliente.GetFromJsonAsync<ConteudoParaAdesaoDTO>(Termo([catalogo.Festa10]), Json, Ct))!.HashDoConteudo;
        var outro = (await formando.Cliente.GetFromJsonAsync<ConteudoParaAdesaoDTO>(Termo([catalogo.Festa15]), Json, Ct))!.HashDoConteudo;
        var codigo = await PedirCodigo(fabrica, formando.Cliente);

        // Act
        var resposta = await formando.Cliente.PostAsJsonAsync("/api/v1/adesoes", new AderirRequestDTO(lido, codigo, [catalogo.Festa15]), Json, Ct);

        // Assert
        outro.ShouldNotBe(lido);
        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("adesao.termo_desatualizado");
        (await formando.Cliente.GetFromJsonAsync<MinhaAdesaoDTO>("/api/v1/adesoes/eu", Json, Ct))!.Adesao.ShouldBeNull();
    }

    /// <summary>D17: sem pacote, sem benefício — quem não aderiu não recebe nada, e quem aderiu sem a colação não recebe a colação.</summary>
    [Fact]
    public async Task Sem_adesao_ou_sem_o_pacote_nao_ha_convite()
    {
        // Arrange
        var catalogo = await Montar();
        var semAdesao = await fabrica.NovoMembro(catalogo.Turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        await PreencherCadastro(semAdesao.Cliente, NovoCpf());
        var soFesta = await fabrica.FormandoComAdesao(catalogo.Turma.FormaturaId, catalogo.Festa10);

        // Act
        await CriarEvento(catalogo.Turma.Presidente, TipoDeEvento.Festa, TimeSpan.FromDays(60));
        await CriarEvento(catalogo.Turma.Presidente, TipoDeEvento.Colacao, TimeSpan.FromDays(30));
        var comColacao = await fabrica.FormandoComAdesao(catalogo.Turma.FormaturaId, catalogo.Foto, catalogo.Colacao);

        // Assert
        (await ConvitesNoBanco(catalogo, semAdesao)).ShouldBeEmpty();
        (await Meus(soFesta, TipoDeEvento.Colacao)).Convites.ShouldBeEmpty();
        (await Meus(soFesta, TipoDeEvento.Festa)).Convites.Count().ShouldBe(10);
        (await Meus(comColacao, TipoDeEvento.Colacao)).Convites.Count().ShouldBe(3);
    }

    /// <summary>D15: o convite extra comprado em pedido entra na conta de lugares ao lado dos benefícios e das cortesias.</summary>
    [Fact]
    public async Task Painel_soma_beneficios_extras_e_cortesias()
    {
        // Arrange
        var catalogo = await Montar();
        var gestao = catalogo.Turma.Presidente.Cliente;
        await CriarEvento(catalogo.Turma.Presidente, TipoDeEvento.Festa, TimeSpan.FromDays(60));
        var ana = await fabrica.FormandoComAdesao(catalogo.Turma.FormaturaId, catalogo.Festa15);
        var itemId = await CriarConviteExtra(catalogo);
        var pedido = await ana.Cliente.PostAsJsonAsync("/api/v1/pedidos", new PedidoRequestDTO(itemId, 2, 1), Json, Ct);
        pedido.StatusCode.ShouldBe(HttpStatusCode.OK);
        var pedidoId = (await pedido.Content.ReadFromJsonAsync<PedidoDTO>(Json, Ct))!.Id;

        // Act
        var liberacao = await gestao.PostAsJsonAsync($"{Convites}/liberar", new LiberacaoRequestDTO(pedidoId, "Paga na porta"), Json, Ct);
        var cortesia = await gestao.PostAsJsonAsync(
            $"{Convites}/cortesias",
            new CortesiaRequestDTO("Prof. Carlos", null, null, null, "Paraninfo"),
            Json,
            Ct
        );

        // Assert
        liberacao.StatusCode.ShouldBe(HttpStatusCode.OK);
        cortesia.StatusCode.ShouldBe(HttpStatusCode.OK);
        var painel = await Obter(catalogo);
        painel.Beneficios.ShouldBe(15);
        painel.Extras.ShouldBe(2);
        painel.Cortesias.ShouldBe(1);
        painel.Lugares.ShouldBe(15 + 2 + 1);
    }

    /// <summary>D24 com carência: vencida dentro dela o convite entra; um dia além, fica preso na portaria.</summary>
    [Fact]
    public async Task Convite_so_fica_preso_quando_o_atraso_passa_da_carencia()
    {
        // Arrange
        const int carencia = 5;
        var catalogo = await Montar();
        var festaId = await CriarEvento(catalogo.Turma.Presidente, TipoDeEvento.Festa, TimeSpan.FromHours(1));
        var ana = await fabrica.FormandoComAdesao(catalogo.Turma.FormaturaId, catalogo.Festa10);
        var gestao = catalogo.Turma.Presidente.Cliente;
        var convites = (await Meus(ana, TipoDeEvento.Festa)).Convites.Where(c => c.NomeDoConvidado is null).Take(2).ToList();
        foreach (var convite in convites)
            (
                await gestao.PutAsJsonAsync(
                    $"{Convites}/{convite.Id}/convidado",
                    new ConvidadoRequestDTO("Tia Rosa", TipoDeDocumento.Rg, "7654321", null),
                    Json,
                    Ct
                )
            ).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using (var contexto = fabrica.ContextoDe(catalogo.Turma.FormaturaId))
            await contexto
                .PlanosDeCobranca.Where(p => p.Status == StatusDoPlano.Vigente)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CarenciaEmDias, carencia), Ct);

        // Act
        await VencerParcelas(catalogo, ana, dias: carencia);
        var dentro = await CheckIn(gestao, convites[0].Codigo, festaId);
        var presosDentro = (await Obter(catalogo)).Presos;
        await VencerParcelas(catalogo, ana, dias: carencia + 1);
        var alem = await CheckIn(gestao, convites[1].Codigo, festaId);

        // Assert
        dentro.StatusCode.ShouldBe(HttpStatusCode.OK);
        presosDentro.ShouldBeEmpty();
        alem.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await alem.Codigo(Ct)).ShouldBe("festa.convite_preso");
        (await Obter(catalogo)).Presos.ShouldHaveSingleItem();
    }

    private async Task<Catalogo> Montar()
    {
        var (turma, pacotes) = await fabrica.TurmaComCatalogo(
            Pacote(TipoDeCobranca.Festa, "10 pessoas", 300_000, "Festa", festa: 10),
            Pacote(TipoDeCobranca.Festa, "15 pessoas", 420_000, "Festa", festa: 15),
            Pacote(TipoDeCobranca.FotoEAlbum, "Foto", 80_000, null),
            Pacote(TipoDeCobranca.Colacao, "Colação", 30_000, null, colacao: 3)
        );

        return new Catalogo(turma, pacotes[0], pacotes[1], pacotes[2], pacotes[3]);
    }

    private static ItemDeCobrancaRequestDTO Pacote(
        TipoDeCobranca tipo,
        string descricao,
        long valor,
        string? grupo,
        int festa = 0,
        int colacao = 0
    ) => new(tipo, descricao, valor, 2, 10, MesQueVem, Grupo: grupo, ConvitesDaFesta: festa, ConvitesDaColacao: colacao);

    /// <summary>Um evento único na agenda, começando daqui a <paramref name="daqui"/> no fuso da turma.</summary>
    private static async Task<Guid> CriarEvento(MembroDeTeste gestao, TipoDeEvento tipo, TimeSpan daqui)
    {
        var inicio = DataUtils.ParaExibicao(DateTime.UtcNow + daqui);
        var resposta = await gestao.Cliente.PostAsJsonAsync(
            "/api/v1/agenda",
            new EventoRequestDTO(
                tipo.ToString(),
                tipo,
                SituacaoDoEvento.Confirmado,
                DateOnly.FromDateTime(inicio),
                new TimeOnly(inicio.Hour, inicio.Minute),
                "Teatro Guaíra",
                null
            ),
            Json,
            Ct
        );
        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await resposta.Content.ReadFromJsonAsync<EventoDTO>(Json, Ct))!.Id;
    }

    private async Task<long> Devido(Catalogo catalogo, MembroDeTeste formando)
    {
        await using var contexto = fabrica.ContextoDe(catalogo.Turma.FormaturaId);
        var vinculoId = await contexto.Vinculos.Where(v => v.UsuarioId == formando.UsuarioId).Select(v => v.Id).SingleAsync(Ct);

        return await contexto.Parcelas.Where(p => p.VinculoId == vinculoId).SumAsync(p => p.ValorOriginalEmCentavos, Ct);
    }

    /// <summary>O que o formando assinou: hash e snapshot da adesão, e as escolhas da cesta gravadas.</summary>
    private sealed record Assinatura(string Hash, string Snapshot, IReadOnlyList<string> Escolhas);

    private async Task<Assinatura> Assinado(Catalogo catalogo, MembroDeTeste formando)
    {
        await using var contexto = fabrica.ContextoDe(catalogo.Turma.FormaturaId);
        var vinculoId = await contexto.Vinculos.Where(v => v.UsuarioId == formando.UsuarioId).Select(v => v.Id).SingleAsync(Ct);
        var adesao = await contexto.Adesoes.AsNoTracking().SingleAsync(a => a.VinculoId == vinculoId, Ct);
        var escolhas = await contexto
            .EscolhasDaCesta.AsNoTracking()
            .Where(e => e.VinculoId == vinculoId)
            .OrderBy(e => e.ItemDeCobrancaId)
            .Select(e => $"{e.Id}:{e.ItemDeCobrancaId}:{e.Observacao}")
            .ToListAsync(Ct);

        return new Assinatura(adesao.HashDoConteudo, adesao.PlanoAceito, escolhas);
    }

    /// <summary>Todos os convites gravados para o vínculo do membro, de qualquer evento — revogados inclusive.</summary>
    private async Task<List<ConviteDoEvento>> ConvitesNoBanco(Catalogo catalogo, MembroDeTeste membro)
    {
        await using var contexto = fabrica.ContextoDe(catalogo.Turma.FormaturaId);
        var vinculoId = await contexto.Vinculos.Where(v => v.UsuarioId == membro.UsuarioId).Select(v => v.Id).SingleAsync(Ct);

        return await contexto.ConvitesDoEvento.Where(c => c.VinculoId == vinculoId).ToListAsync(Ct);
    }

    /// <summary>"Convite extra, R$ 180" como opcional do plano vigente — o avulso que vira pedido.</summary>
    private static async Task<Guid> CriarConviteExtra(Catalogo catalogo)
    {
        var criacao = await catalogo.Turma.Presidente.Cliente.PostAsJsonAsync(
            "/api/v1/cobrancas/opcionais",
            new OpcionalRequestDTO(TipoDeCobranca.ConviteExtra, "Convite extra", 18_000, 1, 10, MesQueVem, null, null, null, null, null),
            Json,
            Ct
        );
        criacao.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await criacao.Content.ReadFromJsonAsync<ItemDeCobrancaDTO>(Json, Ct))!.Id;
    }

    /// <summary>Joga as parcelas do formando para <paramref name="dias"/> atrás — o atraso que prende o convite.</summary>
    private async Task VencerParcelas(Catalogo catalogo, MembroDeTeste formando, int dias = 2)
    {
        await using var contexto = fabrica.ContextoDe(catalogo.Turma.FormaturaId);
        var vinculoId = await contexto.Vinculos.Where(v => v.UsuarioId == formando.UsuarioId).Select(v => v.Id).SingleAsync(Ct);
        var vencimento = DataUtils.Hoje().AddDays(-dias);

        await contexto.Database.ExecuteSqlAsync($"UPDATE parcelas SET vencimento = {vencimento} WHERE vinculo_id = {vinculoId}", Ct);
    }

    private static async Task<PainelDeConvitesDTO> Obter(Catalogo catalogo) =>
        (await catalogo.Turma.Presidente.Cliente.GetFromJsonAsync<PainelDeConvitesDTO>($"{Painel}?tipo=Festa", Json, Ct))!;

    private static async Task<MeusConvitesDTO> Meus(MembroDeTeste membro, TipoDeEvento tipo) =>
        (await membro.Cliente.GetFromJsonAsync<MeusConvitesDTO>($"{Convites}/meus?tipo={tipo}", Json, Ct))!;

    private static Task<HttpResponseMessage> CheckIn(HttpClient cliente, string codigo, Guid eventoId) =>
        cliente.PostAsJsonAsync($"{Convites}/{codigo}/check-in", new CheckInRequestDTO(eventoId), Json, Ct);
}
