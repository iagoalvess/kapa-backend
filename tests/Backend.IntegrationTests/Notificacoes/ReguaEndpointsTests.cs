using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Notificacoes;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Emails.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Notificacoes.Interfaces;
using Backend.Business.Notificacoes.Models;
using Backend.Business.Pagamentos.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Backend.IntegrationTests.Infra.AdesaoDeTeste;

namespace Backend.IntegrationTests.Notificacoes;

/// <summary>
/// A régua contra a API e o Postgres de verdade — os critérios de aceite que só o banco prova: a
/// idempotência do índice único, a parcela com informe pendente que não é cobrada, a parcela paga, a
/// turma suspensa que não dispara e os recortes de papel dos endpoints.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class ReguaEndpointsTests(ApiFactory fabrica)
{
    private const string Regras = "/api/v1/notificacoes/regras";
    private const string Historico = "/api/v1/notificacoes/historico";
    private const string Preferencias = "/api/v1/notificacoes/preferencias/eu";

    private const long Mensalidade = 340_000;

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    /// <summary>Terça-feira, 14h de Brasília — dentro da janela de envio.</summary>
    private static readonly DateTime DentroDaJanela = new(2026, 9, 15, 17, 0, 0, DateTimeKind.Utc);

    private static readonly DateOnly Hoje = new(2026, 9, 15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Turma(Guid Id, MembroDeTeste Presidente, MembroDeTeste Tesoureiro);

    private sealed record Formando(MembroDeTeste Membro, IReadOnlyList<Guid> Parcelas);

    /// <summary>Critério de aceite: turma nova recebe a régua padrão sem configurar nada.</summary>
    [Fact]
    public async Task A_turma_nova_ja_vem_com_a_regua_padrao()
    {
        var turma = await TurmaPronta();

        var regua = await Ler<ReguaDTO>(await turma.Tesoureiro.Cliente.GetAsync(Regras, Ct));

        regua.Regras.Count.ShouldBe(RegraDeNotificacao.Padrao().Count);
        regua.Regras.Select(r => r.DiasDeDeslocamento).ShouldContain(-5);
        regua.Regras.ShouldContain(r => r.Gatilho == GatilhoDaRegua.InformePendente);
        regua.Variaveis.ShouldContain("vencimento");
    }

    /// <summary>Critério de aceite: template com variável desconhecida é rejeitado na gravação.</summary>
    [Fact]
    public async Task A_gravacao_recusa_variavel_desconhecida_e_aceita_a_corrigida()
    {
        var turma = await TurmaPronta();
        var regua = await Ler<ReguaDTO>(await turma.Tesoureiro.Cliente.GetAsync(Regras, Ct));

        var comErro = Reescrever(regua, 3, "Vence em {vencimeto}.");
        var corrigida = Reescrever(regua, 3, "Vence em {vencimento}.");

        (await turma.Tesoureiro.Cliente.PutAsJsonAsync(Regras, comErro, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var salva = await Ler<ReguaDTO>(await turma.Tesoureiro.Cliente.PutAsJsonAsync(Regras, corrigida, Json, Ct));

        salva.Regras.Single(r => r.Gatilho == GatilhoDaRegua.Vencimento && r.DiasDeDeslocamento == 3).Template.ShouldBe("Vence em {vencimento}.");
    }

    /// <summary>Critério de aceite: <c>testar</c> envia só para quem clicou, e não grava no histórico.</summary>
    [Fact]
    public async Task Testar_manda_so_para_quem_clicou_e_nao_entra_no_historico()
    {
        var turma = await TurmaPronta();
        await FormandoQueAderiu(turma, "Ana Cobrada");

        var regua = await Ler<ReguaDTO>(await turma.Tesoureiro.Cliente.GetAsync(Regras, Ct));
        var degrau = regua.Regras.First(r => r.DiasDeDeslocamento == 3);

        (await turma.Tesoureiro.Cliente.PostAsync($"{Regras}/{degrau.Id}/testar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var email = await fabrica.UltimoEmailDaFila(Ct);
        email.ShouldNotBeNull();
        email.ShouldContain("[Teste]");

        (await Ler<PaginaDTO<NotificacaoDTO>>(await turma.Tesoureiro.Cliente.GetAsync(Historico, Ct))).Total.ShouldBe(0);
    }

    /// <summary>
    /// Critérios de aceite, num cenário só: uma mensagem por pessoa com as três parcelas, a segunda
    /// rodada do dia não manda de novo, e o histórico mostra destinatário, canal, data e resultado.
    /// </summary>
    [Fact]
    public async Task Rodar_duas_vezes_no_mesmo_dia_manda_uma_mensagem_com_as_tres_parcelas()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Cobrada");

        await Vencer(turma, ana.Parcelas, Hoje.AddDays(-3));

        var primeira = await Rodar(turma);
        var segunda = await Rodar(turma);

        primeira.Mensagens.ShouldBe(3);
        primeira.Parcelas.ShouldBe(3);
        segunda.Mensagens.ShouldBe(0);

        await using var contexto = fabrica.ContextoDe(turma.Id);
        var enviadas = await contexto.NotificacoesEnviadas.AsNoTracking().ToListAsync(Ct);
        enviadas.Count.ShouldBe(3);
        enviadas.Select(n => n.Destinatario).Distinct().Count().ShouldBe(1);

        var emails = await contexto
            .EmailsFila.AsNoTracking()
            .Where(e => enviadas.Select(n => n.EmailNaFilaId).Contains(e.Id))
            .Select(e => e.CorpoHtml)
            .Distinct()
            .ToListAsync(Ct);

        emails.Count.ShouldBe(1);

        var historico = await Ler<PaginaDTO<NotificacaoDTO>>(await turma.Tesoureiro.Cliente.GetAsync(Historico, Ct));
        historico.Total.ShouldBe(3);
        historico.Itens[0].Nome.ShouldNotBeNull();
        historico.Itens[0].Canal.ShouldBe(CanalDeNotificacao.Email);
        historico.Itens[0].DataDeReferencia.ShouldBe(Hoje);
        historico.Itens[0].Status.ShouldBe(StatusDaNotificacao.Enfileirada);
    }

    /// <summary>Critérios de aceite: parcela paga hoje e parcela com informe pendente não recebem cobrança.</summary>
    [Fact]
    public async Task Parcela_paga_e_parcela_com_informe_pendente_nao_sao_cobradas()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Conferida");

        await Vencer(turma, ana.Parcelas, Hoje.AddDays(-3));

        (
            await ana.Membro.Cliente.PostAsync($"/api/v1/parcelas/{ana.Parcelas[0]}/informes", Informe(Hoje, Mensalidade), Ct)
        ).EnsureSuccessStatusCode();

        (await turma.Tesoureiro.Cliente.PostAsync($"/api/v1/parcelas/{ana.Parcelas[1]}/baixa-manual", BaixaManual(), Ct)).EnsureSuccessStatusCode();

        var resumo = await Rodar(turma);

        resumo.Parcelas.ShouldBe(1);

        await using var contexto = fabrica.ContextoDe(turma.Id);
        (await contexto.NotificacoesEnviadas.AsNoTracking().Select(n => n.ParcelaId).ToListAsync(Ct)).ShouldBe([ana.Parcelas[2]]);
    }

    /// <summary>
    /// Critério de aceite: falha permanente marca o contato como inválido e para de tentar.
    /// </summary>
    /// <remarks>
    /// O desfecho vem da fila de e-mail, que é o que o worker realmente esvazia — e a rodada seguinte
    /// fecha o histórico com ele. Este teste é o que pega a consulta que devolve a notificação
    /// <b>solta</b>: o status seria trocado na memória e nunca chegaria ao banco.
    /// </remarks>
    [Fact]
    public async Task A_entrega_conferida_fecha_o_historico_e_o_endereco_recusado_nao_e_tentado_de_novo()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Caixa Cheia");

        await Vencer(turma, [ana.Parcelas[0]], Hoje.AddDays(-3));
        (await Rodar(turma)).Mensagens.ShouldBe(1);

        await using (var contexto = fabrica.ContextoDe(turma.Id))
        {
            var emailId = await contexto.NotificacoesEnviadas.Select(n => n.EmailNaFilaId).SingleAsync(Ct);

            await contexto
                .EmailsFila.Where(e => e.Id == emailId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(e => e.Status, EEmailStatus.Falhou).SetProperty(e => e.UltimoErro, "550 mailbox unavailable"),
                    Ct
                );
        }

        // A segunda parcela cai no mesmo degrau amanhã; aqui, o que importa é que o endereço já falhou.
        await Vencer(turma, [ana.Parcelas[1]], Hoje.AddDays(-3));

        var segunda = await Rodar(turma);

        segunda.Conferidas.ShouldBe(1);
        segunda.Mensagens.ShouldBe(0);

        await using var depois = fabrica.ContextoDe(turma.Id);
        var notificacao = await depois.NotificacoesEnviadas.AsNoTracking().SingleAsync(Ct);
        notificacao.Status.ShouldBe(StatusDaNotificacao.Falhou);
        notificacao.Erro.ShouldBe("550 mailbox unavailable");
    }

    /// <summary>Critério de aceite: formatura suspensa não dispara régua.</summary>
    [Fact]
    public async Task Formatura_suspensa_nao_entra_na_rodada()
    {
        var suspensaId = await fabrica.CriarFormatura(StatusDaFormatura.Suspensa, Ct);
        var ativaId = await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct);

        using var escopo = fabrica.EscopoDoWorker(ativaId);
        var turmas = await escopo.ServiceProvider.GetRequiredService<IReguaService>().ListarFormaturas(Ct);

        turmas.Select(t => t.Id).ShouldContain(ativaId);
        turmas.Select(t => t.Id).ShouldNotContain(suspensaId);
    }

    /// <summary>
    /// Critério de aceite: a notificação de cobrança não pode ser desativada pelo formando; a do mural pode.
    /// </summary>
    [Fact]
    public async Task O_formando_desliga_o_aviso_do_mural_mas_nao_a_cobranca()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Preferencias");

        var atual = await Ler<IReadOnlyList<PreferenciaDTO>>(await ana.Membro.Cliente.GetAsync(Preferencias, Ct));
        atual.ShouldContain(p => p.Tipo == TipoDeNotificacao.Cobranca && p.Obrigatoria && p.Ativa);

        var recusa = await ana.Membro.Cliente.PutAsJsonAsync(
            Preferencias,
            new PreferenciasRequestDTO([new PreferenciaRequestDTO(TipoDeNotificacao.Cobranca, false)]),
            Json,
            Ct
        );
        recusa.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var salvas = await Ler<IReadOnlyList<PreferenciaDTO>>(
            await ana.Membro.Cliente.PutAsJsonAsync(
                Preferencias,
                new PreferenciasRequestDTO([new PreferenciaRequestDTO(TipoDeNotificacao.Aviso, false)]),
                Json,
                Ct
            )
        );

        salvas.ShouldContain(p => p.Tipo == TipoDeNotificacao.Aviso && !p.Ativa);
        salvas.ShouldContain(p => p.Tipo == TipoDeNotificacao.Cobranca && p.Ativa);
    }

    /// <summary>Configurar é da Tesouraria; auditar, da Gestão. O formando não passa em nenhum dos dois.</summary>
    [Fact]
    public async Task O_formando_nao_configura_a_regua_nem_le_o_historico()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Sem Acesso");
        var comissao = await fabrica.NovoMembro(turma.Id, PapelNaFormatura.Comissao, Ct);

        (await ana.Membro.Cliente.GetAsync(Regras, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ana.Membro.Cliente.GetAsync(Historico, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ana.Membro.Cliente.PostAsync($"/api/v1/notificacoes/cobrar/{ana.Parcelas[0]}", null, Ct)).StatusCode.ShouldBe(
            HttpStatusCode.Forbidden
        );

        (await comissao.Cliente.GetAsync(Regras, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await comissao.Cliente.GetAsync(Historico, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ana.Membro.Cliente.GetAsync(Preferencias, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>O disparo avulso da tesouraria passa pelas mesmas barreiras: uma vez por dia, e não cobra quem pagou.</summary>
    [Fact]
    public async Task A_cobranca_avulsa_manda_uma_vez_e_recusa_a_segunda()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Avulsa");

        await Vencer(turma, [ana.Parcelas[0]], Hoje.AddDays(-10));

        var primeira = await turma.Tesoureiro.Cliente.PostAsync($"/api/v1/notificacoes/cobrar/{ana.Parcelas[0]}", null, Ct);
        var segunda = await turma.Tesoureiro.Cliente.PostAsync($"/api/v1/notificacoes/cobrar/{ana.Parcelas[0]}", null, Ct);

        primeira.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var contexto = fabrica.ContextoDe(turma.Id);
        (await contexto.NotificacoesEnviadas.CountAsync(n => n.ParcelaId == ana.Parcelas[0], Ct)).ShouldBe(1);
    }

    /// <summary>
    /// A última barreira contra a mensagem repetida é o banco: a mesma chave duas vezes não entra,
    /// nem quando a parcela é nula — o resumo à tesouraria.
    /// </summary>
    [Fact]
    public async Task O_indice_unico_barra_o_segundo_envio_da_mesma_chave()
    {
        var turma = await TurmaPronta();
        var regua = await Ler<ReguaDTO>(await turma.Tesoureiro.Cliente.GetAsync(Regras, Ct));
        var regraId = regua.Regras[0].Id;

        await using var contexto = fabrica.ContextoDe(turma.Id);

        contexto.NotificacoesEnviadas.Add(NotificacaoEnviada.Nova(regraId, Hoje, CanalDeNotificacao.Email, "tesouraria@turma.dev", "Resumo"));
        await contexto.SaveChangesAsync(Ct);

        contexto.NotificacoesEnviadas.Add(NotificacaoEnviada.Nova(regraId, Hoje, CanalDeNotificacao.Email, "tesouraria@turma.dev", "Resumo de novo"));

        await Should.ThrowAsync<DbUpdateException>(() => contexto.SaveChangesAsync(Ct));
    }

    private static ReguaRequestDTO Reescrever(ReguaDTO regua, int dias, string template) =>
        new([
            .. regua.Regras.Select(r => new RegraRequestDTO(
                r.Gatilho,
                r.DiasDeDeslocamento,
                r.Canal,
                r.Assunto,
                r.Gatilho == GatilhoDaRegua.Vencimento && r.DiasDeDeslocamento == dias ? template : r.Template,
                r.Ativa,
                r.AvisarTesouraria
            )),
        ]);

    private async Task<ResumoDaRodada> Rodar(Turma turma)
    {
        using var escopo = fabrica.EscopoDoWorker(turma.Id);

        var nome = await fabrica.ContextoDe(null).Formaturas.Where(f => f.Id == turma.Id).Select(f => f.Nome).FirstAsync(Ct);

        return await escopo.ServiceProvider.GetRequiredService<IReguaService>().Executar(new FormaturaParaRegua(turma.Id, nome), DentroDaJanela, Ct);
    }

    private async Task Vencer(Turma turma, IReadOnlyList<Guid> parcelas, DateOnly vencimento)
    {
        await using var contexto = fabrica.ContextoDe(turma.Id);

        await contexto.Parcelas.Where(p => parcelas.Contains(p.Id)).ExecuteUpdateAsync(s => s.SetProperty(p => p.Vencimento, vencimento), Ct);
    }

    private async Task<Turma> TurmaPronta()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var tesoureiro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Tesoureiro, Ct);

        var plano = await Ler<PlanoDeCobrancaDTO>(
            await presidente.Cliente.PostAsJsonAsync(
                "/api/v1/cobrancas/planos",
                new PlanoDeCobrancaRequestDTO("Plano 2027", 200, 100, 0, 0),
                Json,
                Ct
            ),
            HttpStatusCode.Created
        );
        (
            await presidente.Cliente.PostAsJsonAsync(
                $"/api/v1/cobrancas/planos/{plano.Id}/itens",
                new ItemDeCobrancaRequestDTO(TipoDeCobranca.Mensalidade, "Mensalidade", 3 * Mensalidade, 3, 10, new DateOnly(Hoje.Year + 1, 3, 1)),
                Json,
                Ct
            )
        ).EnsureSuccessStatusCode();
        (await presidente.Cliente.PostAsync($"/api/v1/cobrancas/planos/{plano.Id}/vigorar", null, Ct)).EnsureSuccessStatusCode();
        (
            await presidente.Cliente.PostAsJsonAsync(
                "/api/v1/adesoes/termos",
                new { conteudo = "# Termo\n\nA turma divide o custo da formatura." },
                Json,
                Ct
            )
        ).EnsureSuccessStatusCode();

        return new Turma(formaturaId, presidente, tesoureiro);
    }

    private async Task<Formando> FormandoQueAderiu(Turma turma, string nome)
    {
        var membro = await fabrica.NovoMembro(turma.Id, PapelNaFormatura.Formando, Ct);
        await PreencherCadastro(membro.Cliente, NovoCpf(), nome: nome);
        (await Aderir(fabrica, membro.Cliente)).Resposta.StatusCode.ShouldBe(HttpStatusCode.Created);

        await using var contexto = fabrica.ContextoDe(turma.Id);
        var vinculoId = await contexto.Vinculos.Where(v => v.UsuarioId == membro.UsuarioId).Select(v => v.Id).SingleAsync(Ct);
        var parcelas = await contexto.Parcelas.Where(p => p.VinculoId == vinculoId).OrderBy(p => p.Vencimento).Select(p => p.Id).ToListAsync(Ct);

        return new Formando(membro, parcelas);
    }

    private static MultipartFormDataContent Informe(DateOnly pagoEm, long valor)
    {
        return new MultipartFormDataContent
        {
            { new StringContent(pagoEm.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "pagoEm" },
            { new StringContent(valor.ToString(CultureInfo.InvariantCulture)), "valorEmCentavos" },
        };
    }

    private static MultipartFormDataContent BaixaManual()
    {
        var formulario = Informe(Hoje, Mensalidade);
        formulario.Add(new StringContent(nameof(FormaDePagamento.Dinheiro)), "forma");

        return formulario;
    }

    private static async Task<T> Ler<T>(HttpResponseMessage resposta, HttpStatusCode esperado = HttpStatusCode.OK)
    {
        resposta.StatusCode.ShouldBe(esperado, await resposta.Content.ReadAsStringAsync(Ct));

        return (await resposta.Content.ReadFromJsonAsync<T>(Json, Ct))!;
    }
}
