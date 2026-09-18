using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Leads;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Backend.IntegrationTests.Leads;

/// <summary>
/// O formulário de contato da página institucional: anônimo para escrever, Administrador para ler.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class LeadEndpointsTests(ApiFactory fabrica)
{
    private const string Leads = "/api/v1/leads";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static NovoLeadRequestDTO Formulario(string email, string? sobrenome = null, bool aceita = true) =>
        new(
            "Ana Souza",
            email,
            "(41) 99876-5432",
            "UFPR",
            "Medicina",
            82,
            "2027-12",
            "Queremos organizar a formatura.",
            aceita,
            "instagram",
            "social",
            "lancamento",
            sobrenome
        );

    private static string EmailNovo() => $"lead-{Guid.CreateVersion7():N}@exemplo.com";

    /// <summary>Quem preenche o formulário ainda não tem conta — é por isso que está preenchendo.</summary>
    [Fact]
    public async Task Contato_e_aceito_sem_autenticacao()
    {
        var email = EmailNovo();

        var resposta = await fabrica.CreateClient().PostAsJsonAsync(Leads, Formulario(email), Json, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var contexto = fabrica.ContextoDe(null);
        var lead = await contexto.Leads.SingleOrDefaultAsync(l => l.Email == email, Ct);

        lead.ShouldNotBeNull();
        lead.Instituicao.ShouldBe("UFPR");
        lead.TamanhoDaTurma.ShouldBe(82);
    }

    /// <summary>
    /// Quem deixa um lead também é titular de dados: o consentimento fica gravado com a versão
    /// vigente da Política, o momento, o IP e o navegador.
    /// </summary>
    /// <remarks>
    /// Vai com o <c>IpFixo</c> pelo mesmo motivo do teste de consentimento do cadastro: o
    /// <c>TestServer</c> não preenche o endereço remoto, e IP é parte da prova.
    /// </remarks>
    [Fact]
    public async Task Contato_grava_a_prova_do_consentimento()
    {
        // Arrange
        await using var comIp = fabrica.WithWebHostBuilder(host =>
            host.ConfigureTestServices(servicos => servicos.AddSingleton<IStartupFilter>(new IpFixo("203.0.113.7")))
        );
        var cliente = comIp.CreateClient();
        cliente.DefaultRequestHeaders.UserAgent.ParseAdd("NavegadorDeTeste/1.0");
        var email = EmailNovo();

        // Act
        await cliente.PostAsJsonAsync(Leads, Formulario(email), Json, Ct);

        // Assert
        await using var contexto = fabrica.ContextoDe(null);
        var lead = await contexto.Leads.SingleAsync(l => l.Email == email, Ct);

        lead.PrivacidadeVersao.ShouldNotBeNullOrWhiteSpace();
        lead.ConsentidoEm.ShouldNotBe(default);
        lead.EnderecoIp.ShouldBe("203.0.113.7");
        lead.UserAgent.ShouldBe("NavegadorDeTeste/1.0");
    }

    /// <summary>Um 400 ensinaria o robô qual campo deixar em branco na próxima tentativa.</summary>
    [Fact]
    public async Task Honeypot_preenchido_responde_204_e_nao_grava()
    {
        var email = EmailNovo();

        var resposta = await fabrica.CreateClient().PostAsJsonAsync(Leads, Formulario(email, sobrenome: "robô"), Json, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var contexto = fabrica.ContextoDe(null);
        (await contexto.Leads.AnyAsync(l => l.Email == email, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Sem_marcar_a_privacidade_responde_400_com_o_codigo_do_contrato()
    {
        var resposta = await fabrica.CreateClient().PostAsJsonAsync(Leads, Formulario(EmailNovo(), aceita: false), Json, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>Critério de aceite da Sprint 16: <c>GET /leads</c> só para <c>Administrador</c>.</summary>
    [Fact]
    public async Task Listar_sem_token_responde_401()
    {
        var resposta = await fabrica.CreateClient().GetAsync(Leads, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Listar_como_usuario_comum_responde_403()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);

        var resposta = await cliente.ComToken(tokens.AccessToken).GetAsync(Leads, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Nem o Presidente de uma turma lê a lista: é cadastro de gente que nem cliente é.</summary>
    [Fact]
    public async Task Listar_como_presidente_de_turma_responde_403()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.GetAsync(Leads, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Administrador_le_a_lista_e_acha_pelo_termo()
    {
        // Arrange
        var email = EmailNovo();
        await fabrica.CreateClient().PostAsJsonAsync(Leads, Formulario(email), Json, Ct);

        var cliente = fabrica.CreateClient();
        var tokens = await cliente.AutenticarComoAdministrador(Ct);

        // Act
        var pagina = await cliente
            .ComToken(tokens.AccessToken)
            .GetFromJsonAsync<PaginaDTO<LeadDTO>>($"{Leads}?busca={Uri.EscapeDataString(email)}", Json, Ct);

        // Assert
        pagina.ShouldNotBeNull();
        var lead = pagina.Itens.ShouldHaveSingleItem();
        lead.Email.ShouldBe(email);
        lead.Origem.ShouldBe("instagram");
        lead.Telefone.ShouldBe("+5541998765432");
    }
}
