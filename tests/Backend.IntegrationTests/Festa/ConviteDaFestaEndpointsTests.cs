using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backend.Api.DTOs.Agenda;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Festa;
using Backend.Api.DTOs.Pagamentos;
using Backend.Business.Agenda.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Models;
using Backend.Business.Festa.Models;
using Backend.Business.Festa.Services;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Data.Repositories;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Backend.IntegrationTests.Festa;

/// <summary>
/// O convite da festa contra a API e o Postgres de verdade: emissão, página pública, portaria.
/// </summary>
/// <remarks>
/// É aqui que a sprint se prova, pelo mesmo motivo da Sprint 20: as garantias contra convite em
/// dobro e entrada em dobro moram em índices únicos e em instruções com <c>ON CONFLICT</c> — um dublê
/// do banco testaria exatamente o que não é a regra.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class ConviteDaFestaEndpointsTests(ApiFactory fabrica)
{
    private const string Convites = "/api/v1/festa/convites";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Uma turma com convite extra à venda, um formando com pedido e, se pedida, a festa na agenda.</summary>
    private sealed record Cenario(TurmaDeTeste Turma, Guid ItemId, MembroDeTeste Formando, Guid PedidoId, Guid? FestaId);

    // ---- Emissão ----

    /// <summary>P2 e decisão 12: quitar emite; estornar revoga; quitar de novo não duplica.</summary>
    [Fact]
    public async Task Quitar_emite_uma_vez_e_baixa_estorno_baixa_nao_passa_da_quantidade()
    {
        var cenario = await Montar(quantidade: 3, festaEm: TimeSpan.FromHours(1));

        var parcela = (await Quitar(cenario)).Single();
        (await ValidosDoPedido(cenario)).ShouldBe(3);

        (await Estornar(cenario.Turma, parcela)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ValidosDoPedido(cenario)).ShouldBe(0);

        await Quitar(cenario);

        await using var contexto = fabrica.ContextoDe(cenario.Turma.FormaturaId);
        var convites = await contexto.ConvitesDoEvento.Where(c => c.PedidoId == cenario.PedidoId).ToListAsync(Ct);
        convites.Count(c => c.RevogadoEm == null).ShouldBe(3);
        convites.Count(c => c.RevogadoEm != null).ShouldBe(3);
        convites.Where(c => c.RevogadoEm != null).ShouldAllBe(c => c.MotivoDaRevogacao == "pagamento estornado");
    }

    /// <summary>Decisão 12, direto no repositório: dez emissões seguidas dão N convites, não 10N.</summary>
    [Fact]
    public async Task Emitir_dez_vezes_no_repositorio_produz_n_convites()
    {
        var cenario = await Montar(quantidade: 4, festaEm: TimeSpan.FromDays(10));

        await using var contexto = fabrica.ContextoDe(cenario.Turma.FormaturaId);
        var repositorio = new ConviteDoEventoRepository(contexto);
        var vinculoId = (await contexto.Pedidos.SingleAsync(p => p.Id == cenario.PedidoId, Ct)).VinculoId;

        for (var vez = 0; vez < 10; vez++)
            (await repositorio.EmitirDoPedido(cenario.FestaId!.Value, vinculoId, cenario.PedidoId, 4, "MED27", Ct)).ShouldBe(4);

        (await contexto.ConvitesDoEvento.CountAsync(c => c.PedidoId == cenario.PedidoId, Ct)).ShouldBe(4);
    }

    /// <summary>P6: a baixa não cai sem festa na agenda; o convite espera e sai quando a Gestão emite os pendentes.</summary>
    [Fact]
    public async Task Sem_festa_completa_o_convite_espera_e_emitir_pendentes_devolve_evento_incompleto()
    {
        var cenario = await Montar(quantidade: 2, festaEm: null);

        await Quitar(cenario);
        (await ValidosDoPedido(cenario)).ShouldBe(0);

        var semFesta = await cenario.Turma.Presidente.Cliente.PostAsync($"{Convites}/emitir-pendentes", null, Ct);
        semFesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await semFesta.Codigo(Ct)).ShouldBe("festa.evento_incompleto");

        await CriarFesta(cenario.Turma.Presidente, TimeSpan.FromDays(10));
        var resumo = await cenario.Turma.Presidente.Cliente.GetFromJsonAsync<ResumoDosConvitesDTO>($"{Convites}/resumo", Json, Ct);
        resumo!.PedidosQuitadosSemConvite.ShouldBe(1);

        var emitidos = await cenario.Turma.Presidente.Cliente.PostAsync($"{Convites}/emitir-pendentes", null, Ct);
        emitidos.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ValidosDoPedido(cenario)).ShouldBe(2);
    }

    /// <summary>P2: a liberação manual emite com o pedido em aberto, exige motivo e fica na auditoria.</summary>
    [Fact]
    public async Task Liberacao_manual_emite_antes_da_quitacao_exige_motivo_e_audita()
    {
        var cenario = await Montar(quantidade: 2, parcelas: 2, festaEm: TimeSpan.FromDays(10));
        var gestao = cenario.Turma.Presidente.Cliente;

        var semMotivo = await gestao.PostAsJsonAsync($"{Convites}/liberar", new LiberacaoRequestDTO(cenario.PedidoId, " "), Json, Ct);
        var pelaComissao = await gestao.PostAsJsonAsync(
            $"{Convites}/liberar",
            new LiberacaoRequestDTO(cenario.PedidoId, "Pagou 1 de 2, paga o resto na porta"),
            Json,
            Ct
        );
        var peloFormando = await cenario.Formando.Cliente.PostAsJsonAsync(
            $"{Convites}/liberar",
            new LiberacaoRequestDTO(cenario.PedidoId, "Eu mesmo"),
            Json,
            Ct
        );

        semMotivo.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        pelaComissao.StatusCode.ShouldBe(HttpStatusCode.OK);
        peloFormando.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ValidosDoPedido(cenario)).ShouldBe(2);
        await using var contexto = fabrica.ContextoDe(cenario.Turma.FormaturaId);
        (
            await contexto.Eventos.CountAsync(e => e.Nome == NomesDeAuditoria.ConvitesLiberados && e.FormaturaId == cenario.Turma.FormaturaId, Ct)
        ).ShouldBe(1);
    }

    /// <summary>Cancelar o pedido derruba o convite (decisão 8).</summary>
    [Fact]
    public async Task Cancelar_o_pedido_revoga_os_convites()
    {
        var cenario = await Montar(quantidade: 2, parcelas: 2, festaEm: TimeSpan.FromDays(10));
        await Liberar(cenario);

        (await cenario.Formando.Cliente.PostAsync($"/api/v1/pedidos/{cenario.PedidoId}/cancelar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ValidosDoPedido(cenario)).ShouldBe(0);
    }

    /// <summary>P2.1: convite extra com grade que passa do fechamento da lista é recusado no cadastro.</summary>
    [Fact]
    public async Task Grade_do_convite_depois_do_fechamento_da_lista_e_recusada()
    {
        var turma = await fabrica.TurmaComPlano();
        await CriarFesta(turma.Presidente, TimeSpan.FromDays(60));

        var tarde = await turma.Presidente.Cliente.PostAsJsonAsync("/api/v1/cobrancas/opcionais", Opcional(parcelas: 6), Json, Ct);
        var cabe = await turma.Presidente.Cliente.PostAsJsonAsync("/api/v1/cobrancas/opcionais", Opcional(parcelas: 1), Json, Ct);

        tarde.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tarde.Codigo(Ct)).ShouldBe("cobranca.grade_depois_da_festa");
        cabe.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- Página pública ----

    /// <summary>Decisão 11: sem sessão, e sem nome de formando, valor ou cadastro — conferido no JSON cru.</summary>
    [Fact]
    public async Task Pagina_publica_abre_sem_sessao_e_nao_expoe_formando_nem_valor()
    {
        var cenario = await Montar(quantidade: 1, festaEm: TimeSpan.FromDays(10));
        await Quitar(cenario);
        var convite = await NomearPeloFormando(cenario, "Maria Avó", TipoDeDocumento.Rg, "12.345.678-9");
        var anonimo = fabrica.CreateClient();

        var resposta = await anonimo.GetAsync($"{Convites}/{convite.Token}", Ct);
        var cru = await resposta.Content.ReadAsStringAsync(Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        cru.ShouldContain(convite.Codigo);
        cru.ShouldContain("Maria Avó");
        cru.ShouldContain("RG ••••6789");
        cru.ShouldNotContain("123456789");
        cru.ShouldNotContain("centavos");
        cru.ShouldNotContain("Ana Souza");
        cru.ShouldNotContain("email");
        cru.ShouldNotContain(cenario.Formando.UsuarioId.ToString());
    }

    /// <summary>Assinatura adulterada, código inexistente e convite revogado respondem a mesma coisa.</summary>
    [Fact]
    public async Task Adulterado_inexistente_e_revogado_respondem_o_mesmo_404()
    {
        var cenario = await Montar(quantidade: 1, festaEm: TimeSpan.FromDays(10));
        var parcela = (await Quitar(cenario)).Single();
        var token = (await NomearPeloFormando(cenario, "Maria Avó", TipoDeDocumento.Rg, "12.345.678-9")).Token!;
        var anonimo = fabrica.CreateClient();
        var adulterado = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        var respostas = new List<HttpResponseMessage>
        {
            await anonimo.GetAsync($"{Convites}/{adulterado}", Ct),
            await anonimo.GetAsync($"{Convites}/MED27-ZZZZ-AAAAAAAA", Ct),
        };
        (await Estornar(cenario.Turma, parcela)).EnsureSuccessStatusCode();
        respostas.Add(await anonimo.GetAsync($"{Convites}/{token}", Ct));

        foreach (var resposta in respostas)
        {
            resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await resposta.Codigo(Ct)).ShouldBe("festa.convite_nao_encontrado");
        }
    }

    /// <summary>
    /// Convite "a definir" é vaga paga, não ingresso (revisão da P1, 24/09/2026): sem link em "Meus
    /// convites", e a página e o PDF respondem o 404 de um código que não existe — até alguém ser
    /// nomeado. A primeira nomeação não troca o código, e é esse mesmo código que passa a abrir.
    /// </summary>
    [Fact]
    public async Task Convite_a_definir_nao_tem_link_nem_pagina_ate_ser_nomeado()
    {
        var cenario = await Montar(quantidade: 1, festaEm: TimeSpan.FromDays(10));
        await Quitar(cenario);
        var aDefinir = (await MeusConvites(cenario.Formando)).Convites.Single();
        var token = fabrica.Services.GetRequiredService<CodigoDoConvite>().Token(aDefinir.Codigo);
        var anonimo = fabrica.CreateClient();

        var pagina = await anonimo.GetAsync($"{Convites}/{token}", Ct);
        var pdf = await anonimo.GetAsync($"{Convites}/{token}/pdf", Ct);
        var nomeado = await NomearPeloFormando(cenario, "Maria Avó", TipoDeDocumento.Rg, "12.345.678-9");
        var depois = await anonimo.GetAsync($"{Convites}/{token}", Ct);

        aDefinir.Token.ShouldBeNull();
        foreach (var resposta in new[] { pagina, pdf })
        {
            resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await resposta.Codigo(Ct)).ShouldBe("festa.convite_nao_encontrado");
        }
        nomeado.Token.ShouldBe(token);
        depois.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>O PDF abre, com o QR como vetor e o código impresso.</summary>
    [Fact]
    public async Task Pdf_do_convite_e_da_lista_sao_pdf()
    {
        var cenario = await Montar(quantidade: 1, festaEm: TimeSpan.FromDays(10));
        await Quitar(cenario);
        var token = (await NomearPeloFormando(cenario, "Maria Avó", TipoDeDocumento.Rg, "12.345.678-9")).Token;

        var convite = await fabrica.CreateClient().GetAsync($"{Convites}/{token}/pdf", Ct);
        var lista = await cenario.Turma.Presidente.Cliente.GetAsync("/api/v1/festa/portaria/pdf", Ct);
        var listaDoFormando = await cenario.Formando.Cliente.GetAsync("/api/v1/festa/portaria/pdf", Ct);

        convite.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        (await convite.Content.ReadAsStringAsync(Ct)).ShouldStartWith("%PDF-1.4");
        (await lista.Content.ReadAsStringAsync(Ct)).ShouldStartWith("%PDF-1.4");
        listaDoFormando.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Decisão 11: a rota pública tem balde próprio — a enumeração de códigos esbarra nele.</summary>
    [Fact]
    public async Task Rota_publica_barra_a_enumeracao()
    {
        await using var apertada = fabrica.WithWebHostBuilder(host =>
            host.UseSetting("RateLimit:IngressoRajada", "3").UseSetting("RateLimit:IngressoPorMinuto", "1")
        );
        var anonimo = apertada.CreateClient();
        var status = new List<HttpStatusCode>();

        for (var i = 0; i < 5; i++)
            status.Add((await anonimo.GetAsync($"{Convites}/MED27-{i}ZZZ-AAAAAAAA", Ct)).StatusCode);

        status.Take(3).ShouldAllBe(s => s == HttpStatusCode.NotFound);
        status.Skip(3).ShouldAllBe(s => s == HttpStatusCode.TooManyRequests);
    }

    // ---- Nome, documento e transferência ----

    /// <summary>Decisão 17: trocar o titular gera código novo, o antigo fica revogado; o novo recebe o convite e o anterior, o aviso.</summary>
    [Fact]
    public async Task Trocar_o_titular_gera_codigo_novo_e_revoga_o_antigo()
    {
        var cenario = await Montar(quantidade: 1, festaEm: TimeSpan.FromDays(10));
        await Quitar(cenario);
        var (maria, joao) = ($"maria-{Guid.NewGuid():N}@kapa.test", $"joao-{Guid.NewGuid():N}@kapa.test");
        var primeiro = await NomearPeloFormando(cenario, "Maria Avó", TipoDeDocumento.Cpf, AdesaoDeTeste.NovoCpf(), maria);

        var transferido = await NomearPeloFormando(cenario, "João Primo", TipoDeDocumento.Cpf, AdesaoDeTeste.NovoCpf(), joao);

        transferido.Codigo.ShouldNotBe(primeiro.Codigo);
        var antigo = await cenario.Turma.Presidente.Cliente.GetFromJsonAsync<ConsultaNaPortariaDTO>(
            $"/api/v1/festa/portaria/convites/{primeiro.Codigo}",
            Json,
            Ct
        );
        antigo!.Convite.Situacao.ShouldBe(SituacaoNaPortaria.Revogado);
        (await MeusConvites(cenario.Formando)).Convites.Single().Codigo.ShouldBe(transferido.Codigo);
        await using var contexto = fabrica.ContextoDe(cenario.Turma.FormaturaId);
        (await contexto.EmailsFila.CountAsync(e => e.Para == maria, Ct)).ShouldBe(2);
        (await contexto.EmailsFila.CountAsync(e => e.Para == joao, Ct)).ShouldBe(1);
        var paraOJoao = await contexto.EmailsFila.SingleAsync(e => e.Para == joao, Ct);
        paraOJoao.AnexoNome.ShouldBe($"convite-{transferido.Codigo}.pdf");
        paraOJoao.AnexoContentType.ShouldBe("application/pdf");
        Encoding.ASCII.GetString(paraOJoao.AnexoConteudo!, 0, 8).ShouldBe("%PDF-1.4");
        (await contexto.EmailsFila.Where(e => e.Para == maria).Select(e => e.AnexoNome).ToListAsync(Ct)).ShouldContain((string?)null);
    }

    /// <summary>P5.1: CPF com dígito errado é recusado; o documento vai cifrado no banco.</summary>
    [Fact]
    public async Task Cpf_invalido_e_recusado_e_o_documento_e_cifrado()
    {
        var cenario = await Montar(quantidade: 1, festaEm: TimeSpan.FromDays(10));
        await Quitar(cenario);
        var conviteId = (await MeusConvites(cenario.Formando)).Convites.Single().Id;
        var cpf = AdesaoDeTeste.NovoCpf();

        var invalido = await cenario.Formando.Cliente.PutAsJsonAsync(
            $"{Convites}/{conviteId}/convidado",
            new ConvidadoRequestDTO("Maria", TipoDeDocumento.Cpf, "111.111.111-12", null),
            Json,
            Ct
        );
        var valido = await NomearPeloFormando(cenario, "Maria", TipoDeDocumento.Cpf, cpf);

        invalido.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        valido.Documento.ShouldBe($"CPF ••••{cpf[^4..]}");
        await using var contexto = fabrica.ContextoDe(cenario.Turma.FormaturaId);
        var gravado = await contexto
            .Database.SqlQuery<string>($"SELECT numero_do_documento AS \"Value\" FROM convites_do_evento WHERE id = {valido.Id}")
            .SingleAsync(Ct);
        gravado.ShouldNotContain(cpf);
    }

    /// <summary>P5: depois do fechamento o formando não edita; a Gestão edita com documento, e fica na auditoria.</summary>
    [Fact]
    public async Task Depois_do_fechamento_so_a_gestao_altera_com_documento_e_auditoria()
    {
        var cenario = await Montar(quantidade: 1, festaEm: TimeSpan.FromHours(3));
        await Quitar(cenario);
        var conviteId = (await MeusConvites(cenario.Formando)).Convites.Single().Id;
        var url = $"{Convites}/{conviteId}/convidado";

        var peloFormando = await cenario.Formando.Cliente.PutAsJsonAsync(url, new ConvidadoRequestDTO("Maria", null, null, null), Json, Ct);
        var semDocumento = await cenario.Turma.Presidente.Cliente.PutAsJsonAsync(url, new ConvidadoRequestDTO("Maria", null, null, null), Json, Ct);
        var comDocumento = await cenario.Turma.Presidente.Cliente.PutAsJsonAsync(
            url,
            new ConvidadoRequestDTO("Maria", TipoDeDocumento.Rg, "1234567", null),
            Json,
            Ct
        );

        peloFormando.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await peloFormando.Codigo(Ct)).ShouldBe("festa.lista_fechada");
        semDocumento.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await semDocumento.Codigo(Ct)).ShouldBe("festa.documento_obrigatorio");
        comDocumento.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var contexto = fabrica.ContextoDe(cenario.Turma.FormaturaId);
        (
            await contexto.Eventos.CountAsync(e => e.Nome == NomesDeAuditoria.ConvidadoAlterado && e.FormaturaId == cenario.Turma.FormaturaId, Ct)
        ).ShouldBe(1);
    }

    /// <summary>Convite de outro formando responde 404, nunca 403.</summary>
    [Fact]
    public async Task Convite_de_outro_formando_responde_404()
    {
        var cenario = await Montar(quantidade: 1, festaEm: TimeSpan.FromDays(10));
        await Quitar(cenario);
        var conviteId = (await MeusConvites(cenario.Formando)).Convites.Single().Id;
        var outro = await fabrica.NovoMembro(cenario.Turma.FormaturaId, PapelNaFormatura.Formando, Ct);

        var resposta = await outro.Cliente.PutAsJsonAsync(
            $"{Convites}/{conviteId}/convidado",
            new ConvidadoRequestDTO("X", null, null, null),
            Json,
            Ct
        );

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- Cortesia ----

    /// <summary>Decisão 14: cortesia exige nome e motivo, audita e ocupa a cadeira do item — sem estoque, recusa.</summary>
    [Fact]
    public async Task Cortesia_exige_nome_e_motivo_e_consome_o_estoque()
    {
        var cenario = await Montar(quantidade: 2, estoque: 3, festaEm: TimeSpan.FromDays(10));
        var gestao = cenario.Turma.Presidente.Cliente;
        var url = $"{Convites}/cortesias";

        var semNome = await gestao.PostAsJsonAsync(url, new CortesiaRequestDTO(null, null, null, null, "Paraninfo"), Json, Ct);
        var semMotivo = await gestao.PostAsJsonAsync(url, new CortesiaRequestDTO("Prof. Carlos", null, null, null, null), Json, Ct);
        var paraninfo = await gestao.PostAsJsonAsync(url, new CortesiaRequestDTO("Prof. Carlos", null, null, null, "Paraninfo"), Json, Ct);
        var esgotada = await gestao.PostAsJsonAsync(url, new CortesiaRequestDTO("Patrocinador", null, null, null, "Patrocínio"), Json, Ct);

        semNome.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        semMotivo.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        paraninfo.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await paraninfo.Content.ReadFromJsonAsync<ConviteNaPortariaDTO>(Json, Ct))!.Origem.ShouldBe(OrigemDoConvite.Cortesia);
        esgotada.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await esgotada.Codigo(Ct)).ShouldBe("cobranca.estoque_esgotado");
        await using var contexto = fabrica.ContextoDe(cenario.Turma.FormaturaId);
        (await contexto.ItensDeCobranca.SingleAsync(i => i.Id == cenario.ItemId, Ct)).Reservados.ShouldBe(3);
        (
            await contexto.Eventos.CountAsync(e => e.Nome == NomesDeAuditoria.CortesiaEmitida && e.FormaturaId == cenario.Turma.FormaturaId, Ct)
        ).ShouldBe(1);
    }

    // ---- Portaria ----

    /// <summary>Decisão 6 e P4: a Gestão valida, a segunda leitura diz quem e quando; desfazer e validar de novo deixa as duas linhas.</summary>
    [Fact]
    public async Task Validar_de_novo_responde_ja_validado_com_autor_e_desfazer_preserva_o_historico()
    {
        var cenario = await ComConviteNomeado();
        var (gestao, codigo) = (cenario.Cenario.Turma.Presidente.Cliente, cenario.Codigo);

        var peloFormando = await CheckIn(cenario.Cenario.Formando.Cliente, codigo);
        var primeira = await CheckIn(gestao, codigo);
        var segunda = await CheckIn(gestao, codigo);

        peloFormando.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        primeira.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entrada = (await primeira.Content.ReadFromJsonAsync<EntradaNaPortariaDTO>(Json, Ct))!;
        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using (var corpo = JsonDocument.Parse(await segunda.Content.ReadAsStringAsync(Ct)))
        {
            corpo.RootElement.GetProperty("codigo").GetString().ShouldBe("festa.ja_validado");
            var dados = corpo.RootElement.GetProperty("dados");
            dados.GetProperty("check_in_id").GetGuid().ShouldBe(entrada.CheckInId);
            dados.GetProperty("validado_por").GetString().ShouldNotBeNullOrEmpty();
            dados.GetProperty("validado_em").GetDateTime().ShouldBe(entrada.ValidadoEm, TimeSpan.FromSeconds(1));
        }

        (await gestao.PostAsync($"/api/v1/festa/check-ins/{entrada.CheckInId}/desfazer", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CheckIn(gestao, codigo)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var contexto = fabrica.ContextoDe(cenario.Cenario.Turma.FormaturaId);
        var linhas = await contexto.CheckIns.Where(c => c.ConviteId == cenario.ConviteId).ToListAsync(Ct);
        linhas.Count.ShouldBe(2);
        linhas.Count(c => c.DesfeitoEm == null).ShouldBe(1);
    }

    /// <summary>Decisão 12: dois mesários, o mesmo convite, o mesmo instante — uma linha só.</summary>
    [Fact]
    public async Task Check_ins_concorrentes_gravam_uma_entrada()
    {
        var cenario = await ComConviteNomeado();
        var outroMesario = await fabrica.NovoMembro(cenario.Cenario.Turma.FormaturaId, PapelNaFormatura.Comissao, Ct);

        var respostas = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(i => CheckIn(i % 2 == 0 ? cenario.Cenario.Turma.Presidente.Cliente : outroMesario.Cliente, cenario.Codigo))
        );

        respostas.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(5);
        await using var contexto = fabrica.ContextoDe(cenario.Cenario.Turma.FormaturaId);
        (await contexto.CheckIns.CountAsync(c => c.ConviteId == cenario.ConviteId, Ct)).ShouldBe(1);
    }

    /// <summary>Decisão 8: o estorno derruba o convite e a portaria mostra o motivo.</summary>
    [Fact]
    public async Task Estorno_revoga_e_a_portaria_mostra_o_motivo()
    {
        var cenario = await ComConviteNomeado();

        (await Estornar(cenario.Cenario.Turma, cenario.Parcela)).EnsureSuccessStatusCode();
        var resposta = await CheckIn(cenario.Cenario.Turma.Presidente.Cliente, cenario.Codigo);
        var lista = await cenario.Cenario.Turma.Presidente.Cliente.GetFromJsonAsync<ListaDaPortariaDTO>("/api/v1/festa/portaria", Json, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("festa.convite_revogado");
        var linha = lista!.Convites.Single(c => c.Codigo == cenario.Codigo);
        linha.Situacao.ShouldBe(SituacaoNaPortaria.Revogado);
        linha.MotivoDaRevogacao.ShouldBe("pagamento estornado");
    }

    /// <summary>P7: fora da janela, o check-in direto na API responde fora_da_janela.</summary>
    [Fact]
    public async Task Fora_da_janela_o_check_in_e_recusado()
    {
        var cenario = await ComConviteNomeado(festaEm: TimeSpan.FromDays(3));

        var resposta = await CheckIn(cenario.Cenario.Turma.Presidente.Cliente, cenario.Codigo);
        var consulta = await cenario.Cenario.Turma.Presidente.Cliente.GetFromJsonAsync<ConsultaNaPortariaDTO>(
            $"/api/v1/festa/portaria/convites/{cenario.Codigo}",
            Json,
            Ct
        );

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("festa.fora_da_janela");
        consulta!.JanelaAberta.ShouldBeFalse();
    }

    /// <summary>Decisão 13: convite de outro evento diz "outro evento", não "código inválido".</summary>
    [Fact]
    public async Task Convite_de_outro_evento_diz_outro_evento()
    {
        var cenario = await ComConviteNomeado();
        var reuniao = await cenario.Cenario.Turma.Presidente.Cliente.PostAsJsonAsync(
            "/api/v1/agenda",
            new EventoRequestDTO(
                "Colação",
                TipoDeEvento.Colacao,
                SituacaoDoEvento.Confirmado,
                DataUtils.Hoje(),
                new TimeOnly(9, 0),
                "Auditório",
                null
            ),
            Json,
            Ct
        );
        var colacaoId = (await reuniao.Content.ReadFromJsonAsync<EventoDTO>(Json, Ct))!.Id;

        var resposta = await CheckIn(cenario.Cenario.Turma.Presidente.Cliente, cenario.Codigo, colacaoId);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("festa.outro_evento");
    }

    /// <summary>Decisão 15: o mesário da turma B não enxerga o convite da turma A.</summary>
    [Fact]
    public async Task Mesario_de_outra_turma_recebe_404()
    {
        var cenario = await ComConviteNomeado();
        var outraTurma = await fabrica.CriarFormatura(Ct);
        var mesario = await fabrica.NovoMembro(outraTurma, PapelNaFormatura.Comissao, Ct);

        var porCodigo = await CheckIn(mesario.Cliente, cenario.Codigo);
        var porToken = await CheckIn(mesario.Cliente, cenario.Token);

        porCodigo.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        porToken.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>P5: convite sem nome no fechamento é pendência, não entrada anônima.</summary>
    [Fact]
    public async Task Convite_sem_titular_e_pendencia_e_nao_entra()
    {
        var cenario = await Montar(quantidade: 1, festaEm: TimeSpan.FromHours(1));
        await Quitar(cenario);
        var codigo = (await MeusConvites(cenario.Formando)).Convites.Single().Codigo;

        var resposta = await CheckIn(cenario.Turma.Presidente.Cliente, codigo);
        var lista = await cenario.Turma.Presidente.Cliente.GetFromJsonAsync<ListaDaPortariaDTO>("/api/v1/festa/portaria", Json, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("festa.convite_sem_titular");
        lista!.SemTitular.ShouldBe(1);
        lista.Convites.Single().Situacao.ShouldBe(SituacaoNaPortaria.SemTitular);
    }

    /// <summary>Decisão 16: dois aparelhos sem rede, o mesmo convite — uma entrada e uma tentativa repetida, nenhuma descartada.</summary>
    [Fact]
    public async Task Sincronizacao_sem_rede_registra_a_entrada_repetida()
    {
        var cenario = await ComConviteNomeado();
        var agora = DateTime.UtcNow;
        var corpo = new SincronizacaoRequestDTO([
            new EntradaSemRedeDTO(cenario.Codigo, agora.AddMinutes(-3), "celular da Ana"),
            new EntradaSemRedeDTO(cenario.Codigo, agora.AddMinutes(-2), "celular do Bruno"),
            new EntradaSemRedeDTO("MED27-ZZZZ", agora, "celular do Bruno"),
        ]);

        var resposta = await cenario.Cenario.Turma.Presidente.Cliente.PostAsJsonAsync("/api/v1/festa/check-ins/sincronizar", corpo, Json, Ct);

        var resultado = (await resposta.Content.ReadFromJsonAsync<ResultadoDaSincronizacaoDTO>(Json, Ct))!;
        resultado.ShouldBe(new ResultadoDaSincronizacaoDTO(1, 1, 1));
        var lista = await cenario.Cenario.Turma.Presidente.Cliente.GetFromJsonAsync<ListaDaPortariaDTO>("/api/v1/festa/portaria", Json, Ct);
        var linha = lista!.Convites.Single(c => c.Codigo == cenario.Codigo);
        linha.Situacao.ShouldBe(SituacaoNaPortaria.Validado);
        linha.EntrouSemRedeDuasVezes.ShouldBeTrue();
        linha.Entrada!.ValidadoEm.ShouldBe(agora.AddMinutes(-3), TimeSpan.FromSeconds(1));
    }

    /// <summary>P5: reemitir revoga o código antigo e o convidado continua o mesmo.</summary>
    [Fact]
    public async Task Reemitir_troca_o_codigo_e_mantem_o_convidado()
    {
        var cenario = await ComConviteNomeado();

        var resposta = await cenario.Cenario.Turma.Presidente.Cliente.PostAsync($"{Convites}/{cenario.ConviteId}/reemitir", null, Ct);

        var novo = (await resposta.Content.ReadFromJsonAsync<ConviteNaPortariaDTO>(Json, Ct))!;
        novo.Codigo.ShouldNotBe(cenario.Codigo);
        novo.NomeDoConvidado.ShouldBe("Maria Avó");
        (await CheckIn(cenario.Cenario.Turma.Presidente.Cliente, cenario.Codigo)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CheckIn(cenario.Cenario.Turma.Presidente.Cliente, novo.Codigo)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>P5.1: 30 dias depois do evento, documento e e-mail somem; o nome fica.</summary>
    [Fact]
    public async Task Descarte_apaga_documento_e_email_e_mantem_o_nome()
    {
        var cenario = await Montar(quantidade: 1, festaEm: TimeSpan.FromDays(10));
        await Quitar(cenario);
        var convite = await NomearPeloFormando(cenario, "Maria Avó", TipoDeDocumento.Rg, "1234567", "maria@kapa.test");

        await using var contexto = fabrica.ContextoDe(cenario.Turma.FormaturaId);
        await contexto
            .EventosDaTurma.Where(e => e.Id == cenario.FestaId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Data, DataUtils.Hoje().AddDays(-31)), Ct);

        (await new ConviteDoEventoRepository(contexto).DescartarDocumentosDeTodasAsFormaturas(DataUtils.Hoje().AddDays(-30), Ct)).ShouldBeGreaterThan(
            0
        );

        var gravado = await contexto.ConvitesDoEvento.AsNoTracking().SingleAsync(c => c.Id == convite.Id, Ct);
        gravado.NomeDoConvidado.ShouldBe("Maria Avó");
        gravado.NumeroDoDocumento.ShouldBeNull();
        gravado.EmailDoConvidado.ShouldBeNull();
    }

    // ---- Montagem ----

    /// <summary>Convite quitado, nomeado pela Gestão, com a festa dentro da janela da portaria.</summary>
    private sealed record ConviteNomeado(Cenario Cenario, Guid ConviteId, string Codigo, string Token, Guid Parcela);

    private async Task<ConviteNomeado> ComConviteNomeado(TimeSpan? festaEm = null)
    {
        var cenario = await Montar(quantidade: 1, festaEm: festaEm ?? TimeSpan.FromHours(1));
        var parcela = (await Quitar(cenario)).Single();
        var convite = (await MeusConvites(cenario.Formando)).Convites.Single();

        var nomeacao = await cenario.Turma.Presidente.Cliente.PutAsJsonAsync(
            $"{Convites}/{convite.Id}/convidado",
            new ConvidadoRequestDTO("Maria Avó", TipoDeDocumento.Rg, "1234567", null),
            Json,
            Ct
        );
        nomeacao.StatusCode.ShouldBe(HttpStatusCode.OK);
        var nomeado = (await nomeacao.Content.ReadFromJsonAsync<MeuConviteDTO>(Json, Ct))!;

        return new ConviteNomeado(cenario, convite.Id, convite.Codigo, nomeado.Token!, parcela);
    }

    private async Task<Cenario> Montar(int quantidade, TimeSpan? festaEm, int parcelas = 1, int? estoque = null)
    {
        var turma = await fabrica.TurmaComPlano();
        var criacao = await turma.Presidente.Cliente.PostAsJsonAsync(
            "/api/v1/cobrancas/opcionais",
            Opcional(parcelas: 2, estoque: estoque),
            Json,
            Ct
        );
        criacao.StatusCode.ShouldBe(HttpStatusCode.OK);
        var itemId = (await criacao.Content.ReadFromJsonAsync<ItemDeCobrancaDTO>(Json, Ct))!.Id;
        var formando = await fabrica.FormandoComAdesao(turma.FormaturaId);

        var pedido = await formando.Cliente.PostAsJsonAsync("/api/v1/pedidos", new PedidoRequestDTO(itemId, quantidade, parcelas), Json, Ct);
        pedido.StatusCode.ShouldBe(HttpStatusCode.OK);
        var pedidoId = (await pedido.Content.ReadFromJsonAsync<PedidoDTO>(Json, Ct))!.Id;

        var festaId = festaEm is { } quando ? await CriarFesta(turma.Presidente, quando) : (Guid?)null;

        return new Cenario(turma, itemId, formando, pedidoId, festaId);
    }

    /// <summary>"Convite extra, R$ 180, todo dia 10" — o do exemplo da Sprint 20.</summary>
    private static OpcionalRequestDTO Opcional(int parcelas, int? estoque = null) =>
        new(TipoDeCobranca.ConviteExtra, "Convite extra", 18_000, parcelas, 10, DataUtils.Hoje().AddMonths(1), null, null, estoque, null, null);

    /// <summary>A festa na agenda, começando daqui a <paramref name="daqui"/> no fuso da turma.</summary>
    private static async Task<Guid> CriarFesta(MembroDeTeste gestao, TimeSpan daqui)
    {
        var inicio = DataUtils.ParaExibicao(DateTime.UtcNow + daqui);
        var resposta = await gestao.Cliente.PostAsJsonAsync(
            "/api/v1/agenda",
            new EventoRequestDTO(
                "Festa de formatura",
                TipoDeEvento.Festa,
                SituacaoDoEvento.Confirmado,
                DateOnly.FromDateTime(inicio),
                new TimeOnly(inicio.Hour, inicio.Minute),
                "Espaço Vitrália",
                null
            ),
            Json,
            Ct
        );
        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await resposta.Content.ReadFromJsonAsync<EventoDTO>(Json, Ct))!.Id;
    }

    /// <summary>Baixa manual de todas as parcelas abertas do pedido, pela tesouraria.</summary>
    /// <returns>As parcelas baixadas.</returns>
    private async Task<List<Guid>> Quitar(Cenario cenario)
    {
        List<Parcela> abertas;

        await using (var contexto = fabrica.ContextoDe(cenario.Turma.FormaturaId))
            abertas = await contexto
                .Parcelas.AsNoTracking()
                .Where(p => p.ItemDeCobrancaId == cenario.ItemId && p.Status == StatusDaParcela.Aberta)
                .ToListAsync(Ct);

        foreach (var parcela in abertas)
        {
            var formulario = new MultipartFormDataContent
            {
                { new StringContent(DataUtils.Hoje().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "pagoEm" },
                { new StringContent(parcela.ValorOriginalEmCentavos.ToString(CultureInfo.InvariantCulture)), "valorEmCentavos" },
                { new StringContent(nameof(FormaDePagamento.Dinheiro)), "forma" },
            };

            (await cenario.Turma.Presidente.Cliente.PostAsync($"/api/v1/parcelas/{parcela.Id}/baixa-manual", formulario, Ct)).StatusCode.ShouldBe(
                HttpStatusCode.OK
            );
        }

        return [.. abertas.Select(p => p.Id)];
    }

    private static Task<HttpResponseMessage> Estornar(TurmaDeTeste turma, Guid parcelaId) =>
        turma.Presidente.Cliente.PostAsJsonAsync(
            $"/api/v1/parcelas/{parcelaId}/estornar-baixa",
            new EstornarBaixaRequestDTO("O PIX voltou: o banco devolveu a transferência."),
            Json,
            Ct
        );

    private async Task<int> ValidosDoPedido(Cenario cenario)
    {
        await using var contexto = fabrica.ContextoDe(cenario.Turma.FormaturaId);

        return await contexto.ConvitesDoEvento.CountAsync(c => c.PedidoId == cenario.PedidoId && c.RevogadoEm == null, Ct);
    }

    private static async Task<MeusConvitesDTO> MeusConvites(MembroDeTeste formando) =>
        (await formando.Cliente.GetFromJsonAsync<MeusConvitesDTO>($"{Convites}/meus", Json, Ct))!;

    private static async Task<MeuConviteDTO> NomearPeloFormando(
        Cenario cenario,
        string nome,
        TipoDeDocumento tipo,
        string numero,
        string? email = null
    )
    {
        var conviteId = (await MeusConvites(cenario.Formando)).Convites.Single().Id;
        var resposta = await cenario.Formando.Cliente.PutAsJsonAsync(
            $"{Convites}/{conviteId}/convidado",
            new ConvidadoRequestDTO(nome, tipo, numero, email),
            Json,
            Ct
        );
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await resposta.Content.ReadFromJsonAsync<MeuConviteDTO>(Json, Ct))!;
    }

    private static Task<HttpResponseMessage> CheckIn(HttpClient cliente, string codigo, Guid? eventoId = null) =>
        cliente.PostAsJsonAsync($"{Convites}/{codigo}/check-in", new CheckInRequestDTO(eventoId), Json, Ct);

    private static async Task Liberar(Cenario cenario) =>
        (
            await cenario.Turma.Presidente.Cliente.PostAsJsonAsync(
                $"{Convites}/liberar",
                new LiberacaoRequestDTO(cenario.PedidoId, "Paga o resto na porta"),
                Json,
                Ct
            )
        ).EnsureSuccessStatusCode();
}
