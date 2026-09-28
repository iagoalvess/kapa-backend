using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Adesoes;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Financeiro;
using Backend.Api.DTOs.Formaturas;
using Backend.Api.DTOs.Relatorios;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Eventos.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Notificacoes.Interfaces;
using Backend.Business.Notificacoes.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Backend.IntegrationTests.Infra.AdesaoDeTeste;

namespace Backend.IntegrationTests.Formaturas;

/// <summary>
/// A saída do formando contra a API e o Postgres de verdade.
/// </summary>
/// <remarks>
/// Aqui ficam os critérios de aceite que só o banco prova: a régua da noite seguinte, o "a receber"
/// do Caixa, a adimplência do dashboard e o acesso de leitura que sobra para quem saiu. O teste é
/// sobre a <b>consulta</b>, e não sobre a tela — a tela some com um <c>if</c>, a consulta não.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class DesligamentoEndpointsTests(ApiFactory fabrica)
{
    private const string Membros = "/api/v1/formaturas/atual/membros";

    /// <summary>Três parcelas de R$ 3.400 cada — a grade que a turma de teste gera.</summary>
    private const long Mensalidade = 340_000;

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    /// <summary>Terça-feira, 14h de Brasília — dentro da janela de envio da régua.</summary>
    private static readonly DateTime DentroDaJanela = new(2026, 9, 15, 17, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Turma(Guid Id, MembroDeTeste Presidente, MembroDeTeste Tesoureiro);

    private sealed record Formando(MembroDeTeste Membro, Guid VinculoId, IReadOnlyList<Guid> Parcelas);

    private static DesligarMembroRequestDTO Pedido(bool cancelarAtraso = false, string motivo = MotivoDeSaida.Trancamento) =>
        new(motivo, null, cancelarAtraso);

    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.NoContent)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Desligar_e_so_do_presidente(string papel, HttpStatusCode esperado)
    {
        var turma = await TurmaPronta();
        var quemChama = await fabrica.NovoMembro(turma.Id, papel, Ct);
        var ana = await FormandoQueAderiu(turma, "Ana Politica");

        var resposta = await quemChama.Cliente.PostAsJsonAsync($"{Membros}/{ana.Membro.UsuarioId}/desligar", Pedido(), Json, Ct);

        resposta.StatusCode.ShouldBe(esperado);
    }

    /// <summary>O Tesoureiro vê e propõe; o Presidente executa (P3 de 17/09/2026).</summary>
    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Resumo_da_saida_segue_a_politica_de_gestao(string papel, HttpStatusCode esperado)
    {
        var turma = await TurmaPronta();
        var quemChama = await fabrica.NovoMembro(turma.Id, papel, Ct);
        var ana = await FormandoQueAderiu(turma, "Ana Resumo");

        var resposta = await quemChama.Cliente.GetAsync($"{Membros}/{ana.Membro.UsuarioId}/resumo-da-saida", Ct);

        resposta.StatusCode.ShouldBe(esperado);
    }

    /// <summary>
    /// Critério de aceite: a régua da noite seguinte não envia nada para quem foi desligado.
    /// </summary>
    /// <remarks>
    /// Sobre a consulta da régua, e não sobre a tela: o atraso que a comissão escolheu <b>manter</b>
    /// continua devido e continua na lista de inadimplentes, mas o e-mail para. A parcela é a mesma —
    /// o que muda é o vínculo.
    /// </remarks>
    [Fact]
    public async Task A_regua_para_no_mesmo_instante_do_desligamento()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Regua");
        var bruno = await FormandoQueAderiu(turma, "Bruno Regua");

        // As duas primeiras parcelas de cada um vencem hoje: a régua do dia cobra as duas pessoas.
        await Vencer(turma, [ana.Parcelas[0], bruno.Parcelas[0]], DateOnly.FromDateTime(DentroDaJanela));

        var antes = await Rodar(turma);
        antes.Parcelas.ShouldBe(2);

        await LimparEnvios(turma);
        // O atraso fica devido de propósito: é o caso em que a parcela continua aberta e, ainda
        // assim, o e-mail não pode sair.
        await Desligar(turma, ana, Pedido());

        var depois = await Rodar(turma);

        depois.Parcelas.ShouldBe(1);
        await using var contexto = fabrica.ContextoDe(turma.Id);
        var cobrados = await contexto.NotificacoesEnviadas.AsNoTracking().Select(n => n.Destinatario).ToListAsync(Ct);
        cobrados.ShouldNotContain(await EmailDe(ana));
        cobrados.ShouldContain(await EmailDe(bruno));
    }

    /// <summary>
    /// Critérios de aceite: o "a receber" cai exatamente pelo valor cancelado, o arrecadado não muda,
    /// e a adimplência passa a ignorar o desligado nas duas pontas.
    /// </summary>
    [Fact]
    public async Task O_caixa_perde_o_que_foi_cancelado_e_a_adimplencia_ignora_quem_saiu()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Caixa");
        await FormandoQueAderiu(turma, "Bruno Caixa");

        // A primeira de Ana venceu e não foi paga: entra no denominador da adimplência e no atraso.
        await Vencer(turma, [ana.Parcelas[0]], DateOnly.FromDateTime(DentroDaJanela).AddDays(-10));

        var antes = await Painel(turma);
        antes.Adimplencia.DevidoEmCentavos.ShouldBe(Mensalidade);
        antes.Caixa.AReceberEmCentavos.ShouldBe(5 * Mensalidade);
        antes.Caixa.EmAtrasoEmCentavos.ShouldBe(Mensalidade);

        await Desligar(turma, ana, Pedido(cancelarAtraso: true));

        var depois = await Painel(turma);

        // Caíram as três de Ana: duas a vencer e a vencida que a comissão mandou cancelar.
        depois.Caixa.AReceberEmCentavos.ShouldBe(3 * Mensalidade);
        depois.Caixa.EmAtrasoEmCentavos.ShouldBe(0);
        // O que entrou não muda — ninguém pagou nada aqui, e continua zero dos dois lados.
        depois.Caixa.ArrecadadoEmCentavos.ShouldBe(antes.Caixa.ArrecadadoEmCentavos);
        // Sem vencido de vínculo ativo, o denominador zera e o índice volta a 100%.
        depois.Adimplencia.DevidoEmCentavos.ShouldBe(0);
        depois.Adimplencia.PercentualBaseDezMil.ShouldBe(10_000);
    }

    /// <summary>
    /// Critério de aceite: desligar cancela o que está em aberto e não toca no que já foi pago.
    /// </summary>
    /// <remarks>
    /// Com a caixa do atraso desmarcada — o padrão —, a parcela vencida continua aberta: a comissão
    /// não pediu para perdoá-la, e o sistema não decide isso sozinho.
    /// </remarks>
    [Fact]
    public async Task Desligar_cancela_o_que_nao_venceu_e_deixa_o_atraso_de_pe()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Parcelas");
        await Vencer(turma, [ana.Parcelas[0]], DateOnly.FromDateTime(DentroDaJanela).AddDays(-10));

        await Desligar(turma, ana, Pedido());

        await using var contexto = fabrica.ContextoDe(turma.Id);
        var status = await contexto
            .Parcelas.AsNoTracking()
            .Where(p => p.VinculoId == ana.VinculoId)
            .OrderBy(p => p.Vencimento)
            .Select(p => p.Status)
            .ToListAsync(Ct);

        status.ShouldBe([StatusDaParcela.Aberta, StatusDaParcela.Cancelada, StatusDaParcela.Cancelada]);
    }

    /// <summary>
    /// Critério de aceite: desligar duas vezes não cancela nada a mais nem manda o segundo e-mail.
    /// </summary>
    [Fact]
    public async Task Desligar_duas_vezes_e_recusado_e_nao_manda_outro_email()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Idempotente");

        await Desligar(turma, ana, Pedido());
        var enviados = await ContarEmails(await EmailDe(ana));

        var segunda = await turma.Presidente.Cliente.PostAsJsonAsync($"{Membros}/{ana.Membro.UsuarioId}/desligar", Pedido(), Json, Ct);

        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await segunda.Codigo(Ct)).ShouldBe("formatura.membro_ja_desligado");
        (await ContarEmails(await EmailDe(ana))).ShouldBe(enviados);
    }

    /// <summary>
    /// Critério de aceite: membro sem adesão devolve <c>formatura.membro_sem_adesao</c> — a tela
    /// oferece Remover, porque ele nunca deveu nada.
    /// </summary>
    [Fact]
    public async Task Desligar_quem_nao_aderiu_devolve_membro_sem_adesao()
    {
        var turma = await TurmaPronta();
        var convidado = await fabrica.NovoMembro(turma.Id, PapelNaFormatura.Formando, Ct);

        var resposta = await turma.Presidente.Cliente.PostAsJsonAsync($"{Membros}/{convidado.UsuarioId}/desligar", Pedido(), Json, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("formatura.membro_sem_adesao");

        var resumo = await Ler<ResumoDaSaidaDTO>(await turma.Presidente.Cliente.GetAsync($"{Membros}/{convidado.UsuarioId}/resumo-da-saida", Ct));
        resumo.TemAdesao.ShouldBeFalse();
    }

    /// <summary>Critério de aceite: desligar o último presidente é recusado, como remover já é.</summary>
    [Fact]
    public async Task Desligar_o_ultimo_presidente_e_recusado()
    {
        var turma = await TurmaPronta();
        await PreencherCadastro(turma.Presidente.Cliente, NovoCpf(), nome: "Presidente Solitario");
        (await Aderir(fabrica, turma.Presidente.Cliente)).Resposta.StatusCode.ShouldBe(HttpStatusCode.Created);

        var resposta = await turma.Presidente.Cliente.PostAsJsonAsync($"{Membros}/{turma.Presidente.UsuarioId}/desligar", Pedido(), Json, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("formatura.ultimo_presidente");
    }

    /// <summary>
    /// P5: quem saiu continua lendo o próprio histórico — e só ele.
    /// </summary>
    /// <remarks>
    /// Critério de aceite: a adesão e o PDF do termo continuam acessíveis depois do desligamento. O
    /// mural é da turma e some junto com o vínculo, o que prova que a política nova não virou um
    /// passe livre.
    /// </remarks>
    [Fact]
    public async Task Quem_saiu_le_o_proprio_extrato_e_o_termo_mas_nao_a_turma()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Historico");
        var minha = await Ler<MinhaAdesaoDTO>(await ana.Membro.Cliente.GetAsync("/api/v1/adesoes/eu", Ct));
        var adesaoId = minha.Adesao!.Id;

        await Desligar(turma, ana, Pedido());

        (await ana.Membro.Cliente.GetAsync("/api/v1/extrato/eu", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ana.Membro.Cliente.GetAsync("/api/v1/adesoes/eu", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ana.Membro.Cliente.GetAsync($"/api/v1/adesoes/{adesaoId}/pdf", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ana.Membro.Cliente.GetAsync("/api/v1/formaturas/atual", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ana.Membro.Cliente.GetAsync("/api/v1/comunicacao/avisos", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ana.Membro.Cliente.GetAsync("/api/v1/dashboard/publico", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ana.Membro.Cliente.GetAsync("/api/v1/formandos/eu", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Desligar é definitivo (23/09/2026): o religar saiu, e o endpoint não existe mais.
    /// </summary>
    [Fact]
    public async Task Nao_existe_religar()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Desligada");
        await Desligar(turma, ana, Pedido());

        var resposta = await turma.Presidente.Cliente.PostAsync($"{Membros}/{ana.Membro.UsuarioId}/religar", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ana.Membro.Cliente.GetAsync("/api/v1/comunicacao/avisos", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Critério de aceite: o evento aparece na auditoria com motivo e valores.
    /// </summary>
    /// <remarks>
    /// É a mitigação de "comissão desligando para limpar a inadimplência do painel": o número do
    /// dashboard melhora, e a linha que explica por quê fica.
    /// </remarks>
    [Fact]
    public async Task O_desligamento_deixa_o_evento_na_auditoria_com_motivo_e_valores()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Auditada");

        await Desligar(turma, ana, Pedido(motivo: MotivoDeSaida.DificuldadeFinanceira));

        await using var contexto = fabrica.ContextoDe(turma.Id);
        var evento = await contexto
            .Eventos.AsNoTracking()
            .Where(e => e.Nome == NomesDeAuditoria.FormandoDesligado)
            .OrderByDescending(e => e.OcorridoEm)
            .FirstAsync(Ct);

        evento.UsuarioId.ShouldBe(turma.Presidente.UsuarioId);
        evento.Dados.ShouldNotBeNull();

        // Pelo JSON, e não por trecho de texto: a coluna é `jsonb`, e o Postgres reordena as chaves
        // e normaliza os espaços — comparar a string crua quebraria sem nada ter mudado.
        var dados = JsonDocument.Parse(evento.Dados).RootElement;
        dados.GetProperty("motivo").GetString().ShouldBe(MotivoDeSaida.DificuldadeFinanceira);
        dados.GetProperty("parcelasCanceladas").GetInt32().ShouldBe(3);
        dados.GetProperty("canceladoEmCentavos").GetInt64().ShouldBe(3 * Mensalidade);
        dados.GetProperty("formaturaId").GetGuid().ShouldBe(turma.Id);
    }

    /// <summary>
    /// Na lista de Membros o desligado não some, e o filtro o separa do removido.
    /// </summary>
    /// <remarks>Sumir esconderia o histórico de quem pagou parte — e é o que a decisão 1 evita.</remarks>
    [Fact]
    public async Task O_desligado_fica_na_lista_e_o_filtro_o_separa_do_removido()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Listada");
        var convidado = await fabrica.NovoMembro(turma.Id, PapelNaFormatura.Formando, Ct);
        await Desligar(turma, ana, Pedido());
        (await turma.Presidente.Cliente.DeleteAsync($"{Membros}/{convidado.UsuarioId}", Ct)).EnsureSuccessStatusCode();

        var desligados = await Ler<PaginaDTO<MembroDaFormaturaDTO>>(
            await turma.Presidente.Cliente.GetAsync($"{Membros}?ativo=false&desligado=true", Ct)
        );
        var removidos = await Ler<PaginaDTO<MembroDaFormaturaDTO>>(
            await turma.Presidente.Cliente.GetAsync($"{Membros}?ativo=false&desligado=false", Ct)
        );

        desligados.Itens.Select(m => m.UsuarioId).ShouldBe([ana.Membro.UsuarioId]);
        desligados.Itens[0].MotivoDoDesligamento.ShouldBe(MotivoDeSaida.Trancamento);
        desligados.Itens[0].DesligadoEm.ShouldNotBeNull();
        desligados.Itens[0].TemAdesao.ShouldBeTrue();

        removidos.Itens.Select(m => m.UsuarioId).ShouldBe([convidado.UsuarioId]);
        removidos.Itens[0].DesligadoEm.ShouldBeNull();
        removidos.Itens[0].TemAdesao.ShouldBeFalse();
    }

    private static async Task Desligar(Turma turma, Formando formando, DesligarMembroRequestDTO pedido) =>
        (
            await turma.Presidente.Cliente.PostAsJsonAsync($"{Membros}/{formando.Membro.UsuarioId}/desligar", pedido, Json, Ct)
        ).EnsureSuccessStatusCode();

    private static async Task<DashboardPublicoDTO> Painel(Turma turma) =>
        await Ler<DashboardPublicoDTO>(await turma.Presidente.Cliente.GetAsync("/api/v1/dashboard/publico", Ct));

    /// <summary>O e-mail da conta do formando, que <c>MembroDeTeste</c> não carrega.</summary>
    private async Task<string> EmailDe(Formando formando)
    {
        await using var contexto = fabrica.ContextoDe(null);

        return await contexto.Users.AsNoTracking().Where(u => u.Id == formando.Membro.UsuarioId).Select(u => u.Email!).SingleAsync(Ct);
    }

    private async Task<int> ContarEmails(string email)
    {
        await using var contexto = fabrica.ContextoDe(null);

        return await contexto.EmailsFila.AsNoTracking().CountAsync(e => e.Para == email, Ct);
    }

    private async Task LimparEnvios(Turma turma)
    {
        await using var contexto = fabrica.ContextoDe(turma.Id);

        await contexto.NotificacoesEnviadas.ExecuteDeleteAsync(Ct);
    }

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

    /// <summary>Turma ativa com plano em vigor e termo publicado — o mínimo para existir parcela.</summary>
    private async Task<Turma> TurmaPronta()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var tesoureiro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Tesoureiro, Ct);

        var plano = await Ler<PlanoDeCobrancaDTO>(
            await presidente.Cliente.PostAsJsonAsync(
                "/api/v1/cobrancas/planos",
                new PlanoDeCobrancaRequestDTO("Plano 2027", 200, 100, 0, 0, 0),
                Json,
                Ct
            ),
            HttpStatusCode.Created
        );
        (
            await presidente.Cliente.PostAsJsonAsync(
                $"/api/v1/cobrancas/planos/{plano.Id}/itens",
                new ItemDeCobrancaRequestDTO(
                    TipoDeCobranca.Mensalidade,
                    "Mensalidade",
                    3 * Mensalidade,
                    3,
                    10,
                    new DateOnly(DentroDaJanela.Year + 1, 3, 1)
                ),
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

        return new Formando(membro, vinculoId, parcelas);
    }

    private static async Task<T> Ler<T>(HttpResponseMessage resposta, HttpStatusCode esperado = HttpStatusCode.OK)
    {
        resposta.StatusCode.ShouldBe(esperado, await resposta.Content.ReadAsStringAsync(Ct));

        return (await resposta.Content.ReadFromJsonAsync<T>(Json, Ct))!;
    }
}
