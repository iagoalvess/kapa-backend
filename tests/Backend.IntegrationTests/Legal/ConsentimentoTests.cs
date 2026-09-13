using System.Net;
using System.Net.Http.Json;
using Backend.Api.DTOs.Auth;
using Backend.Api.DTOs.Legal;
using Backend.Business.Abstractions;
using Backend.Business.Legal.Interfaces;
using Backend.Business.Legal.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Backend.IntegrationTests.Legal;

/// <summary>
/// Consentimento auditável: documentos versionados, cadastro com aceite na mesma transação e
/// pendência quando sai versão nova.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class ConsentimentoTests(ApiFactory fabrica)
{
    private const string Registrar = "/api/v1/auth/registrar";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Vigentes_abrem_sem_sessao_com_os_dois_documentos()
    {
        var vigentes = await fabrica.CreateClient().GetFromJsonAsync<DocumentoLegalDTO[]>("/api/v1/legal/vigentes", Ct);

        vigentes.ShouldNotBeNull();
        vigentes.Select(d => d.Tipo).ShouldBe([TipoDeDocumento.PoliticaDePrivacidade, TipoDeDocumento.TermosDeUso], ignoreOrder: true);
        vigentes.ShouldAllBe(d => d.Conteudo.Length > 0);
    }

    /// <summary>O registro de aceite aponta para uma versão; ela precisa continuar abrindo, vigente ou não.</summary>
    [Fact]
    public async Task Versao_especifica_tem_link_permanente_e_aceita_qualquer_caixa()
    {
        var cliente = fabrica.CreateClient();

        var documento = await cliente.GetFromJsonAsync<DocumentoLegalDTO>("/api/v1/legal/termosdeuso/1", Ct);
        var inexistente = await cliente.GetAsync("/api/v1/legal/TermosDeUso/nao-existe", Ct);

        documento!.Tipo.ShouldBe(TipoDeDocumento.TermosDeUso);
        documento.Conteudo.ShouldContain("Termos de Uso");
        inexistente.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await inexistente.Codigo(Ct)).ShouldBe("legal.documento_nao_encontrado");
    }

    [Fact]
    public async Task Cadastro_sem_aceite_devolve_400_e_nao_cria_o_usuario()
    {
        var email = NovoEmail();

        var resposta = await fabrica
            .CreateClient()
            .PostAsJsonAsync(Registrar, new RegistrarRequestDTO("Sem aceite", email, "Senha@Teste123", null), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await resposta.Codigo(Ct)).ShouldBe("legal.aceite_obrigatorio");
        (await UsuarioExiste(email)).ShouldBeFalse();
    }

    [Fact]
    public async Task Cadastro_aceitando_so_os_termos_devolve_400()
    {
        var cliente = fabrica.CreateClient();
        var corpo = await cliente.CorpoDeCadastro("Meio aceite", NovoEmail(), "Senha@Teste123", Ct);
        var soOsTermos = corpo with { Aceites = [.. corpo.Aceites!.Where(a => a.Tipo == TipoDeDocumento.TermosDeUso)] };

        var resposta = await cliente.PostAsJsonAsync(Registrar, soOsTermos, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await resposta.Codigo(Ct)).ShouldBe("legal.aceite_obrigatorio");
    }

    /// <summary>
    /// IP e User-Agent são o que transforma o clique em prova. O IP é fixado por um filtro de
    /// inicialização porque o <c>TestServer</c> não preenche o endereço remoto.
    /// </summary>
    [Fact]
    public async Task Cadastro_grava_versao_data_ip_e_user_agent_do_aceite()
    {
        await using var comIp = fabrica.WithWebHostBuilder(host =>
            host.ConfigureTestServices(servicos => servicos.AddSingleton<IStartupFilter>(new IpFixo("203.0.113.7")))
        );
        var cliente = comIp.CreateClient();
        cliente.DefaultRequestHeaders.UserAgent.ParseAdd("NavegadorDeTeste/1.0");
        var email = NovoEmail();
        var antes = DateTime.UtcNow;

        (
            await cliente.PostAsJsonAsync(Registrar, await cliente.CorpoDeCadastro("Com prova", email, "Senha@Teste123", Ct), Ct)
        ).EnsureSuccessStatusCode();

        var consentimentos = await ConsentimentosDe(email);
        consentimentos.Count.ShouldBe(2);
        consentimentos.ShouldAllBe(c => c.EnderecoIp == "203.0.113.7" && c.UserAgent == "NavegadorDeTeste/1.0" && !c.Revogado);
        consentimentos.ShouldAllBe(c => c.AceitoEm >= antes.AddSeconds(-1) && c.AceitoEm <= DateTime.UtcNow);
        consentimentos.ShouldAllBe(c => c.Versao.Length > 0);
    }

    /// <summary>
    /// Derruba o segundo passo — o aceite — com uma falha imprevista, depois de o Identity já ter
    /// gravado o usuário. A transação precisa levar a conta junto.
    /// </summary>
    [Fact]
    public async Task Falha_no_aceite_desfaz_a_criacao_do_usuario()
    {
        await using var quebrada = fabrica.WithWebHostBuilder(host =>
            host.ConfigureTestServices(servicos => servicos.AddScoped<ILegalService, LegalQueCai>())
        );
        var cliente = quebrada.CreateClient();
        var email = NovoEmail();

        var resposta = await cliente.PostAsJsonAsync(Registrar, await cliente.CorpoDeCadastro("Vai cair", email, "Senha@Teste123", Ct), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await UsuarioExiste(email)).ShouldBeFalse();
    }

    /// <summary>
    /// Quem abriu o cadastro antes de uma publicação envia a versão velha. A falha prevista também
    /// desfaz a transação — sem exceção, só o <c>Result</c> de falha.
    /// </summary>
    [Fact]
    public async Task Cadastro_com_versao_desatualizada_devolve_409_e_nao_cria_o_usuario()
    {
        var cliente = fabrica.CreateClient();
        var email = NovoEmail();
        var corpoAntigo = await cliente.CorpoDeCadastro("Atrasado", email, "Senha@Teste123", Ct);

        await PublicarNovaVersaoDosTermos();

        var resposta = await cliente.PostAsJsonAsync(Registrar, corpoAntigo, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("legal.versao_desatualizada");
        (await UsuarioExiste(email)).ShouldBeFalse();
    }

    [Fact]
    public async Task Publicar_versao_nova_nao_altera_consentimentos_e_gera_pendencia()
    {
        // Arrange
        var cliente = fabrica.CreateClient();
        var email = NovoEmail();
        var tokens = await cliente.RegistrarComEmail(email, Ct);
        cliente.ComToken(tokens.AccessToken);
        var antes = await ConsentimentosDe(email);
        (await MeusAceites(cliente)).Pendencias.ShouldBeEmpty();

        // Act
        var versaoNova = await PublicarNovaVersaoDosTermos();
        var pendente = await MeusAceites(cliente);

        // Assert
        var depois = await ConsentimentosDe(email);
        depois.Select(Retrato).ShouldBe(antes.Select(Retrato));
        pendente.Pendencias.ShouldBe([new AceitePendenteDTO(TipoDeDocumento.TermosDeUso, versaoNova)]);
    }

    [Fact]
    public async Task Aceitar_a_versao_nova_resolve_a_pendencia_e_soma_ao_historico()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        cliente.ComToken(tokens.AccessToken);
        var versaoNova = await PublicarNovaVersaoDosTermos();

        var resposta = await cliente.PostAsJsonAsync(
            "/api/v1/legal/aceites",
            new RegistrarAceitesRequestDTO([new AceiteDeDocumentoDTO(TipoDeDocumento.TermosDeUso, versaoNova)]),
            Ct
        );

        resposta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var aceites = await MeusAceites(cliente);
        aceites.Pendencias.ShouldBeEmpty();
        aceites.Historico.Count.ShouldBe(3);
        aceites.Historico[0].Versao.ShouldBe(versaoNova);
    }

    [Fact]
    public async Task Aceite_vazio_devolve_400_e_sem_sessao_devolve_401()
    {
        var cliente = fabrica.CreateClient();
        var anonimo = await cliente.PostAsJsonAsync("/api/v1/legal/aceites", new RegistrarAceitesRequestDTO([]), Ct);

        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        var vazio = await cliente.ComToken(tokens.AccessToken).PostAsJsonAsync("/api/v1/legal/aceites", new RegistrarAceitesRequestDTO([]), Ct);

        anonimo.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        vazio.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await vazio.Codigo(Ct)).ShouldBe("legal.aceite_obrigatorio");
    }

    /// <summary>A barreira final não é o código: nem um <c>UPDATE</c> escrito à mão altera uma prova.</summary>
    [Fact]
    public async Task O_banco_recusa_alterar_ou_apagar_consentimento()
    {
        var email = NovoEmail();
        await fabrica.CreateClient().RegistrarComEmail(email, Ct);
        var usuarioId = (await ConsentimentosDe(email))[0].UsuarioId;

        await using var contexto = fabrica.ContextoDe(null);

        await Should.ThrowAsync<PostgresException>(() =>
            contexto.Database.ExecuteSqlAsync($"UPDATE consentimentos SET revogado = true WHERE usuario_id = {usuarioId}", Ct)
        );
        await Should.ThrowAsync<PostgresException>(() =>
            contexto.Database.ExecuteSqlAsync($"DELETE FROM consentimentos WHERE usuario_id = {usuarioId}", Ct)
        );
        await Should.ThrowAsync<PostgresException>(() =>
            contexto.Database.ExecuteSqlAsync($"UPDATE documentos_legais SET conteudo = 'só uma vírgula' WHERE versao = '1'", Ct)
        );
    }

    private static (Guid, Guid, string, DateTime, string, string, bool) Retrato(ConsentimentoRegistrado c) =>
        (c.Id, c.DocumentoLegalId, c.Versao, c.AceitoEm, c.EnderecoIp, c.UserAgent, c.Revogado);

    private static string NovoEmail() => $"legal-{Guid.CreateVersion7():N}@testes.local";

    private static async Task<MeusAceitesDTO> MeusAceites(HttpClient cliente) =>
        (await cliente.GetFromJsonAsync<MeusAceitesDTO>("/api/v1/legal/meus-aceites", Ct))!;

    /// <summary>
    /// Publica uma versão nova dos Termos, já vigente.
    /// </summary>
    /// <remarks>
    /// Rótulo único por teste: a suíte compartilha o banco, e duas publicações com o mesmo rótulo
    /// esbarrariam no índice único. Os testes seguintes aceitam a nova porque o cadastro de teste
    /// lê as vigentes a cada chamada.
    /// </remarks>
    private async Task<string> PublicarNovaVersaoDosTermos()
    {
        var versao = $"teste-{Guid.CreateVersion7():N}";

        await using var contexto = fabrica.ContextoDe(null);

        contexto.DocumentosLegais.Add(
            new DocumentoLegal
            {
                Tipo = TipoDeDocumento.TermosDeUso,
                Versao = versao,
                Conteudo = "# Termos de Uso\n\nVersão publicada por teste.",
                VigenteDesde = DateTime.UtcNow,
            }
        );

        await contexto.SaveChangesAsync(Ct);

        return versao;
    }

    private async Task<List<ConsentimentoRegistrado>> ConsentimentosDe(string email)
    {
        await using var contexto = fabrica.ContextoDe(null);

        return await (
            from consentimento in contexto.Consentimentos.AsNoTracking()
            join usuario in contexto.Users on consentimento.UsuarioId equals usuario.Id
            where usuario.Email == email
            orderby consentimento.Id
            select consentimento
        ).ToListAsync(Ct);
    }

    private async Task<bool> UsuarioExiste(string email)
    {
        await using var contexto = fabrica.ContextoDe(null);

        return await contexto.Users.AnyAsync(u => u.Email == email, Ct);
    }

    /// <summary>Fixa o IP remoto antes do pipeline da aplicação, como o proxy reverso faria.</summary>
    /// <param name="ip">IP a atribuir a toda requisição.</param>
    private sealed class IpFixo(string ip) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> proximo) =>
            app =>
            {
                app.Use(
                    (contexto, seguir) =>
                    {
                        contexto.Connection.RemoteIpAddress = IPAddress.Parse(ip);
                        return seguir(contexto);
                    }
                );
                proximo(app);
            };
    }

    /// <summary>Aceite que falha de forma imprevista, depois de o usuário já existir na transação.</summary>
    private sealed class LegalQueCai : ILegalService
    {
        public Task<Result<IReadOnlyList<VersaoDeDocumento>>> ListarVigentes(CancellationToken ct = default) =>
            Task.FromResult(
                Result.Ok<IReadOnlyList<VersaoDeDocumento>>([
                    new VersaoDeDocumento(Guid.Empty, TipoDeDocumento.TermosDeUso, "1", "", DateTime.UtcNow),
                    new VersaoDeDocumento(Guid.Empty, TipoDeDocumento.PoliticaDePrivacidade, "1", "", DateTime.UtcNow),
                ])
            );

        public Task<Result<VersaoDeDocumento>> ObterVersao(string tipo, string versao, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result> RegistrarAceites(
            Guid usuarioId,
            IReadOnlyList<AceiteDeDocumento> aceites,
            OrigemDoAceite origem,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("Banco caiu no meio do cadastro.");

        public Task<Result<MeusAceites>> ObterMeusAceites(Guid usuarioId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
