using System.Net;
using System.Net.Http.Json;
using Backend.Api.DTOs.Privacidade;
using Backend.Business.Enquetes.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Privacidade.Interfaces;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Backend.IntegrationTests.Enquetes;

/// <summary>API e PostgreSQL reais: sigilo, concorrência, isolamento e preservação após anonimizar.</summary>
[Collection(ColecaoDeApi.Nome)]
public sealed class EnquetesEndpointsTests(ApiFactory fabrica)
{
    private const string Rota = "/api/v1/enquetes";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DadosDaEnquete Dados(TipoDeEnquete tipo = TipoDeEnquete.UnicaEscolha) =>
        new(
            "Qual tema?",
            null,
            tipo,
            tipo == TipoDeEnquete.MultiplaEscolha ? 2 : null,
            100,
            DateTime.UtcNow.AddDays(1),
            ["Clássico", "Tropical", "Futurista"]
        );

    private static async Task<EnqueteDetalhe> Criar(MembroDeTeste gestao, DadosDaEnquete? dados = null)
    {
        var resposta = await gestao.Cliente.PostAsJsonAsync(Rota, dados ?? Dados(), JsonDaApi.Opcoes, Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.Created, await resposta.Content.ReadAsStringAsync(Ct));
        return (await resposta.Content.ReadFromJsonAsync<EnqueteDetalhe>(JsonDaApi.Opcoes, Ct))!;
    }

    private static Task<EnqueteDetalhe?> Obter(MembroDeTeste membro, Guid id) =>
        membro.Cliente.GetFromJsonAsync<EnqueteDetalhe>($"{Rota}/{id}", JsonDaApi.Opcoes, Ct);

    private static Task<HttpResponseMessage> Votar(MembroDeTeste membro, EnqueteDetalhe enquete, params Guid[] opcoes) =>
        membro.Cliente.PutAsJsonAsync($"{Rota}/{enquete.Enquete.Id}/resposta", new { opcoes }, JsonDaApi.Opcoes, Ct);

    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.Created)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.Created)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Created)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Membros_leem_e_so_gestao_publica(string papel, HttpStatusCode esperado)
    {
        var membro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), papel, Ct);
        (await membro.Cliente.GetAsync(Rota, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await membro.Cliente.PostAsJsonAsync(Rota, Dados(), JsonDaApi.Opcoes, Ct)).StatusCode.ShouldBe(esperado);
    }

    [Fact]
    public async Task Votos_simultaneos_substituem_uma_resposta_sigilo_e_retirada_preservam_a_trava()
    {
        var turma = await fabrica.CriarFormaturaQueConvida(Ct);
        var aluno = await fabrica.FormandoComAdesao(turma);
        await fabrica.ConfirmarEmail(aluno, Ct);
        var gestor = await fabrica.NovoMembro(turma, PapelNaFormatura.Comissao, Ct);
        var enquete = await Criar(gestor);
        var segunda = await Criar(gestor);
        (await Votar(aluno, enquete, segunda.Opcoes[0].Id)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var simultaneas = await Task.WhenAll(Votar(aluno, enquete, enquete.Opcoes[0].Id), Votar(aluno, enquete, enquete.Opcoes[1].Id));
        simultaneas.Select(r => r.StatusCode).ShouldAllBe(c => c == HttpStatusCode.NoContent);
        var meu = (await Obter(aluno, enquete.Enquete.Id))!;
        meu.Enquete.TotalRespostas.ShouldBe(1);
        meu.Enquete.MinhaResposta!.Count.ShouldBe(1);
        meu.Opcoes.Sum(o => o.Respostas).ShouldBe(1);
        meu.Participantes.ShouldBeNull();
        var daGestao = (await Obter(gestor, enquete.Enquete.Id))!;
        daGestao.Enquete.MinhaResposta.ShouldBeNull();
        daGestao.Participantes!.Count.ShouldBe(1);
        (await aluno.Cliente.PostAsJsonAsync(Rota, Dados(), JsonDaApi.Opcoes, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await aluno.Cliente.DeleteAsync($"{Rota}/{enquete.Enquete.Id}/resposta", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var retirada = (await Obter(aluno, enquete.Enquete.Id))!;
        retirada.Enquete.TotalRespostas.ShouldBe(0);
        retirada.Opcoes.Sum(o => o.Respostas).ShouldBe(0);
        retirada.Enquete.OpcoesTravadas.ShouldBeTrue();
        (
            await gestor.Cliente.PutAsJsonAsync($"{Rota}/{enquete.Enquete.Id}", Dados() with { Pergunta = "Mudou?" }, JsonDaApi.Opcoes, Ct)
        ).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Multipla_conta_pessoas_exporta_so_o_titular_e_anonimiza_sem_perder_placar()
    {
        var turma = await fabrica.CriarFormaturaQueConvida(Ct);
        var aluno = await fabrica.FormandoComAdesao(turma);
        var outro = await fabrica.FormandoComAdesao(turma);
        await fabrica.ConfirmarEmail(aluno, Ct);
        await fabrica.ConfirmarEmail(outro, Ct);
        var gestor = await fabrica.NovoMembro(turma, PapelNaFormatura.Comissao, Ct);
        var enquete = await Criar(gestor, Dados(TipoDeEnquete.MultiplaEscolha));
        (await Votar(aluno, enquete, enquete.Opcoes[0].Id, enquete.Opcoes[1].Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var detalhe = (await Obter(gestor, enquete.Enquete.Id))!;
        detalhe.Enquete.TotalRespostas.ShouldBe(1);
        detalhe.Enquete.TotalAptos.ShouldBe(2);
        detalhe.Enquete.QuorumAtingido.ShouldBeFalse();
        detalhe.Opcoes.Sum(o => o.Percentual).ShouldBe(200);
        var dados = (await aluno.Cliente.GetFromJsonAsync<MeusDadosDTO>("/api/v1/privacidade/meus-dados", JsonDaApi.Opcoes, Ct))!;
        dados.Turmas.Single().Enquetes!.Single().Escolhas.ShouldBe(["Clássico", "Tropical"]);
        (await Votar(outro, enquete, enquete.Opcoes[2].Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await gestor.Cliente.PostAsync($"{Rota}/{enquete.Enquete.Id}/encerrar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Votar(aluno, enquete, enquete.Opcoes[0].Id)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await aluno.Cliente.DeleteAsync($"{Rota}/{enquete.Enquete.Id}/resposta", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var escopo = fabrica.Services.CreateScope();
        (await escopo.ServiceProvider.GetRequiredService<IAnonimizacaoDeTitular>().Anonimizar(aluno.UsuarioId, Ct)).Sucesso.ShouldBeTrue();
        (await escopo.ServiceProvider.GetRequiredService<IAnonimizacaoDeTitular>().Anonimizar(outro.UsuarioId, Ct)).Sucesso.ShouldBeTrue();
        var preservada = (await Obter(gestor, enquete.Enquete.Id))!;
        preservada.Enquete.TotalRespostas.ShouldBe(2);
        preservada.Enquete.TotalAptos.ShouldBe(2);
        preservada.Participantes.ShouldBeEmpty();
        preservada.Opcoes.Sum(o => o.Respostas).ShouldBe(3);
        await using var contexto = fabrica.ContextoDe(turma);
        var anonimas = await contexto.RespostasDeEnquetes.ToListAsync(Ct);
        anonimas.Count.ShouldBe(2);
        anonimas.ShouldAllBe(r => r.VinculoId == null && r.RespondidaEm == DateTime.UnixEpoch && r.CriadoEm == DateTime.UnixEpoch);
    }

    [Fact]
    public async Task Outra_turma_nao_le_nem_muta_e_gratuito_nao_acessa_modulo()
    {
        var gestor = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Comissao, Ct);
        var enquete = await Criar(gestor);
        var vizinho = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Comissao, Ct);
        (await vizinho.Cliente.GetAsync($"{Rota}/{enquete.Enquete.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await vizinho.Cliente.PostAsync($"{Rota}/{enquete.Enquete.Id}/cancelar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var gratuito = await fabrica.NovoMembro(
            await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct, contratada: false),
            PapelNaFormatura.Presidente,
            Ct
        );
        (await gratuito.Cliente.GetAsync(Rota, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await gratuito.Cliente.PostAsJsonAsync(Rota, Dados(), JsonDaApi.Opcoes, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cancelamento_oculta_resultado_do_aluno_e_gestao_sem_adesao_nao_vota()
    {
        var turma = await fabrica.CriarFormaturaQueConvida(Ct);
        var aluno = await fabrica.FormandoComAdesao(turma);
        await fabrica.ConfirmarEmail(aluno, Ct);
        var gestor = await fabrica.NovoMembro(turma, PapelNaFormatura.Comissao, Ct);
        await fabrica.ConfirmarEmail(gestor, Ct);
        var enquete = await Criar(gestor);
        (await Votar(gestor, enquete, enquete.Opcoes[0].Id)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Votar(aluno, enquete, enquete.Opcoes[0].Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await gestor.Cliente.PostAsync($"{Rota}/{enquete.Enquete.Id}/cancelar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var ocultada = (await Obter(aluno, enquete.Enquete.Id))!;
        ocultada.Enquete.TotalRespostas.ShouldBe(0);
        ocultada.Opcoes.ShouldAllBe(o => o.Respostas == 0 && o.Percentual == 0);
        (await Obter(gestor, enquete.Enquete.Id))!.Enquete.TotalRespostas.ShouldBe(1);
    }
}
