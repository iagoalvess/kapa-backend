using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Auth;
using Backend.Api.DTOs.Marketing;
using Backend.Api.DTOs.Privacidade;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Marketing.Interfaces;
using Backend.Business.Marketing.Models;
using Backend.Business.Marketing.Services;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Backend.IntegrationTests.Marketing;

/// <summary>
/// O marketing do Kapa de ponta a ponta (Sprint 40): a caixa do cadastro, "Minha privacidade", o descadastro
/// sem login e as jornadas contra o Postgres — inclusive a regra que não tem volta: formando nunca recebe.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class ComunicacaoDoKapaTests(ApiFactory fabrica)
{
    private const string Descadastro = "/api/v1/privacidade/descadastro";

    private const string Preferencia = "/api/v1/privacidade/comunicacao-do-kapa";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Um dia útil às 14h de Brasília, quatro dias à frente ou mais: a turma "criada há 4 dias" nasce depois dos
    /// refresh tokens que o cadastro de teste acabou de emitir, e ninguém da comissão conta como quem voltou.
    /// </summary>
    private static readonly DateTime Agora = DiaUtil(DateTime.UtcNow.Date.AddDays(5)).AddHours(17);

    [Fact]
    public async Task Cadastro_com_a_caixa_marcada_liga_e_registra_o_aceite_e_sem_ela_nasce_desligado()
    {
        var marcou = await Registrar(receber: true);
        var naoMarcou = await Registrar(receber: false);

        await using var contexto = fabrica.ContextoDe(null);

        (await contexto.Users.SingleAsync(u => u.Id == marcou.UsuarioId, Ct)).ReceberComunicacaoDoKapa.ShouldBeTrue();
        (await contexto.Users.SingleAsync(u => u.Id == naoMarcou.UsuarioId, Ct)).ReceberComunicacaoDoKapa.ShouldBeFalse();

        var registro = await contexto.ConsentimentosDeMarketing.SingleAsync(c => c.UsuarioId == marcou.UsuarioId, Ct);
        registro.Aceito.ShouldBeTrue();
        registro.Origem.ShouldBe(OrigemDoConsentimentoDeMarketing.Cadastro);
        registro.VersaoDoTexto.ShouldBe(TextoDoConsentimentoDeMarketing.Versao);

        (await contexto.ConsentimentosDeMarketing.AnyAsync(c => c.UsuarioId == naoMarcou.UsuarioId, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Minha_privacidade_mostra_e_muda_a_preferencia_e_o_historico_so_cresce()
    {
        var membro = await Registrar(receber: false);

        (await membro.Cliente.PutAsJsonAsync(Preferencia, new ComunicacaoDoKapaRequestDTO(true), Json, Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NoContent
        );
        (await membro.Cliente.PutAsJsonAsync(Preferencia, new ComunicacaoDoKapaRequestDTO(true), Json, Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NoContent
        );
        (await membro.Cliente.PutAsJsonAsync(Preferencia, new ComunicacaoDoKapaRequestDTO(false), Json, Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NoContent
        );

        var dados = await membro.Cliente.GetFromJsonAsync<MeusDadosDTO>("/api/v1/privacidade/meus-dados", Json, Ct);

        dados!.Comunicacoes.DoKapa.Receber.ShouldBeFalse();
        dados.Comunicacoes.DoKapa.Historico.Select(r => r.Aceito).ShouldBe([false, true]);
        dados.Comunicacoes.DoKapa.Historico.ShouldAllBe(r => r.Origem == OrigemDoConsentimentoDeMarketing.MinhaPrivacidade);
    }

    [Fact]
    public async Task Preferencia_exige_login()
    {
        var resposta = await fabrica.CreateClient().PutAsJsonAsync(Preferencia, new ComunicacaoDoKapaRequestDTO(true), Json, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Critério de aceite: sem login, a mesma resposta para token válido, vencido e adulterado — e só o válido
    /// desliga. Depois dele, o marketing daquela pessoa não passa mais pela conferência do envio.
    /// </summary>
    [Fact]
    public async Task Descadastro_responde_igual_para_token_valido_vencido_e_adulterado_e_so_o_valido_desliga()
    {
        var membro = await Registrar(receber: true);
        var link = fabrica.Services.GetRequiredService<LinkDeDescadastro>();
        var valido = link.Token(membro.UsuarioId, DateTime.UtcNow);
        var vencido = link.Token(membro.UsuarioId, DateTime.UtcNow.AddDays(-400));
        var adulterado = valido[..^2] + (valido[^2..] == "AA" ? "BB" : "AA");
        var anonimo = fabrica.CreateClient();

        foreach (var token in new[] { vencido, adulterado, "lixo" })
        {
            (await UmClique(anonimo, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await Recebe(membro.UsuarioId)).ShouldBeTrue();
        }

        (await UmClique(anonimo, valido)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Recebe(membro.UsuarioId)).ShouldBeFalse();

        using var escopo = fabrica.EscopoDoWorker(Guid.CreateVersion7());
        (await escopo.ServiceProvider.GetRequiredService<IEmailFilaRepository>().ListarQueRecebemMarketing([membro.UsuarioId], Ct)).ShouldBeEmpty();

        await using var contexto = fabrica.ContextoDe(null);
        var saida = await contexto.ConsentimentosDeMarketing.Where(c => c.UsuarioId == membro.UsuarioId).OrderBy(c => c.RegistradoEm).LastAsync(Ct);
        saida.Aceito.ShouldBeFalse();
        saida.Origem.ShouldBe(OrigemDoConsentimentoDeMarketing.DescadastroPeloEmail);
    }

    /// <summary>
    /// Critério de aceite: a regra é pelo papel no vínculo. Quem é Presidente numa turma e formando em outra recebe
    /// só sobre a turma em que é comissão; quem é só formando não recebe nada, com a caixa marcada ou não.
    /// </summary>
    [Fact]
    public async Task Jornada_fala_com_a_comissao_e_nunca_com_quem_e_formando()
    {
        var presidenteEFormando = await Registrar(receber: true);
        var soFormando = await Registrar(receber: true);
        var turmaDaComissao = await TurmaDoGratuito(diasAntes: 4, presidenteEFormando.UsuarioId);
        var turmaDoFormando = await TurmaDoGratuito(diasAntes: 4);

        await Vincular(presidenteEFormando.UsuarioId, turmaDaComissao, PapelNaFormatura.Presidente);
        await Vincular(presidenteEFormando.UsuarioId, turmaDoFormando, PapelNaFormatura.Formando);
        await Vincular(soFormando.UsuarioId, turmaDaComissao, PapelNaFormatura.Formando);
        await Vincular(soFormando.UsuarioId, turmaDoFormando, PapelNaFormatura.Formando);

        await RodarJornadas(Agora);

        var envios = await Envios(presidenteEFormando.UsuarioId, soFormando.UsuarioId);
        var envio = envios.ShouldHaveSingleItem();
        envio.UsuarioId.ShouldBe(presidenteEFormando.UsuarioId);
        envio.FormaturaId.ShouldBe(turmaDaComissao);
        envio.Jornada.ShouldBe(JornadaDeMarketing.CriouENaoVoltou);

        await using var contexto = fabrica.ContextoDe(null);
        var email = await contexto.EmailsFila.SingleAsync(e => e.UsuarioId == presidenteEFormando.UsuarioId, Ct);
        email.Categoria.ShouldBe(ECategoriaDeEmail.Marketing);
        email.LinkDeDescadastro.ShouldNotBeNull().ShouldContain(Descadastro);
        email.CorpoHtml.ShouldContain("porque criou a turma");
        (await contexto.EmailsFila.AnyAsync(e => e.UsuarioId == soFormando.UsuarioId, Ct)).ShouldBeFalse();
    }

    /// <summary>Critério de aceite: a turma que contratou não recebe mais a jornada de venda.</summary>
    [Fact]
    public async Task Turma_que_contratou_nao_recebe()
    {
        var presidente = await Registrar(receber: true);
        var turma = await TurmaDoGratuito(diasAntes: 4, presidente.UsuarioId);
        await Vincular(presidente.UsuarioId, turma, PapelNaFormatura.Presidente);
        await fabrica.Contratar(turma, Ct);

        await RodarJornadas(Agora);

        (await Envios(presidente.UsuarioId)).ShouldBeEmpty();
    }

    /// <summary>
    /// Critério de aceite (P3): "criou e não voltou" sai, e "montou e parou", que vence no dia seguinte, espera os
    /// 14 dias da trava — e só depois sai. A rodada é duas semanas à frente para a turma, de 13 dias e meio, ainda
    /// nascer depois do login do presidente.
    /// </summary>
    [Fact]
    public async Task Duas_jornadas_na_mesma_semana_mandam_uma_so()
    {
        var turma = await fabrica.TurmaComPlano();
        await fabrica.ConfirmarEmail(turma.Presidente, Ct);
        (await turma.Presidente.Cliente.PutAsJsonAsync(Preferencia, new ComunicacaoDoKapaRequestDTO(true), Json, Ct)).EnsureSuccessStatusCode();

        await using (var contexto = fabrica.ContextoDe(turma.FormaturaId))
            await contexto.Assinaturas.ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, StatusDaAssinatura.Vencida), Ct);

        var agora = DiaUtil(DateTime.UtcNow.Date.AddDays(14)).AddHours(17);
        await Envelhecer(turma.FormaturaId, agora.AddDays(-13.5));

        await RodarJornadas(agora);
        await RodarJornadas(DiaUtil(agora.AddDays(1)));

        (await Envios(turma.Presidente.UsuarioId)).Select(e => e.Jornada).ShouldBe([JornadaDeMarketing.CriouENaoVoltou]);

        await RodarJornadas(DiaUtil(agora.AddDays(14)));

        (await Envios(turma.Presidente.UsuarioId))
            .Select(e => e.Jornada)
            .ShouldBe([JornadaDeMarketing.CriouENaoVoltou, JornadaDeMarketing.MontouEParou], ignoreOrder: true);
    }

    private static DateTime DiaUtil(DateTime dia)
    {
        while (dia.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            dia = dia.AddDays(1);

        return dia;
    }

    private static Task<HttpResponseMessage> UmClique(HttpClient cliente, string token) =>
        cliente.PostAsync(
            $"{Descadastro}?token={Uri.EscapeDataString(token)}",
            new FormUrlEncodedContent([new("List-Unsubscribe", "One-Click")]),
            Ct
        );

    /// <summary>Registra uma conta pela API, com ou sem a caixa, e confirma o e-mail.</summary>
    private async Task<MembroDeTeste> Registrar(bool receber)
    {
        var cliente = fabrica.CreateClient();
        var email = $"marketing-{Guid.CreateVersion7():N}@testes.local";
        var corpo = await cliente.CorpoDeCadastro("Ana Souza", email, "Senha@Teste123", Ct) with { ReceberComunicacaoDoKapa = receber };

        var resposta = await cliente.PostAsJsonAsync("/api/v1/auth/registrar", corpo, Json, Ct);
        resposta.EnsureSuccessStatusCode();

        var tokens = (await resposta.Content.ReadFromJsonAsync<TokenResponseDTO>(Json, Ct))!;
        await fabrica.ConfirmarEmail(email, Ct);

        return new MembroDeTeste(cliente.ComToken(tokens.AccessToken), FormaturaDeTeste.IdDoUsuario(tokens.AccessToken));
    }

    /// <summary>Uma turma do gratuito criada <paramref name="diasAntes"/> dias antes da rodada.</summary>
    private async Task<Guid> TurmaDoGratuito(double diasAntes, Guid? criador = null)
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct, contratada: false);

        await Envelhecer(formaturaId, Agora.AddDays(-diasAntes), criador);

        return formaturaId;
    }

    private async Task Envelhecer(Guid formaturaId, DateTime criadaEm, Guid? criador = null)
    {
        await using var contexto = fabrica.ContextoDe(null);

        await contexto
            .Formaturas.Where(f => f.Id == formaturaId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(f => f.CriadoEm, criadaEm).SetProperty(f => f.CriadoPorUsuarioId, f => criador ?? f.CriadoPorUsuarioId),
                Ct
            );
    }

    private async Task Vincular(Guid usuarioId, Guid formaturaId, string papel)
    {
        await using var contexto = fabrica.ContextoDe(null);

        contexto.Vinculos.Add(
            new VinculoDeFormatura
            {
                UsuarioId = usuarioId,
                FormaturaId = formaturaId,
                Papel = papel,
            }
        );

        await contexto.SaveChangesAsync(Ct);
    }

    private async Task RodarJornadas(DateTime agoraUtc)
    {
        using var escopo = fabrica.EscopoDoWorker(Guid.CreateVersion7());

        await escopo.ServiceProvider.GetRequiredService<IJornadasDeMarketingService>().Executar(agoraUtc, Ct);
    }

    private async Task<List<EnvioDeMarketing>> Envios(params Guid[] usuarios)
    {
        await using var contexto = fabrica.ContextoDe(null);

        return await contexto.EnviosDeMarketing.Where(e => usuarios.Contains(e.UsuarioId)).ToListAsync(Ct);
    }

    private async Task<bool> Recebe(Guid usuarioId)
    {
        await using var contexto = fabrica.ContextoDe(null);

        return await contexto.Users.Where(u => u.Id == usuarioId).Select(u => u.ReceberComunicacaoDoKapa).SingleAsync(Ct);
    }
}
