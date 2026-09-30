using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Admin;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Backend.IntegrationTests.Admin;

/// <summary>
/// Verifica o analytics do painel do Kapa e a fronteira que o protege (Sprint 44).
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class AdminEndpointsTests(ApiFactory fabrica)
{
    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sem_token_o_painel_responde_401()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/api/v1/admin/analytics", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>Critério de aceite 2: a conta recém-criada (<c>Usuario</c>) não lê o analytics.</summary>
    [Fact]
    public async Task Usuario_comum_nao_acessa_o_painel()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);

        var resposta = await cliente.ComToken(tokens.AccessToken).GetAsync("/api/v1/admin/analytics", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Critério de aceite 3 (D4): o administrador não é coringa — numa rota de gestão de turma ele recebe 403.
    /// </summary>
    [Theory]
    [InlineData("/api/v1/formaturas/atual/membros")]
    [InlineData("/api/v1/financeiro/despesas")]
    [InlineData("/api/v1/formaturas/atual")]
    public async Task Administrador_recebe_403_numa_rota_de_gestao_de_turma(string rota)
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.AutenticarComoAdministrador(Ct);

        var resposta = await cliente.ComToken(tokens.AccessToken).GetAsync(rota, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// A turma nova e o pagamento do plano entram nos números do período, e cada bloco fecha com ele mesmo.
    /// </summary>
    /// <remarks>
    /// A suíte divide o banco com os outros testes, então as asserções são de <b>diferença</b> e de coerência — o
    /// total exato é o critério 4, conferido contra SQL no banco de demonstração.
    /// </remarks>
    [Fact]
    public async Task Analytics_conta_a_turma_nova_e_o_recebido_do_kapa()
    {
        // Arrange
        var suporte = await Suporte();
        var antes = await Analytics(suporte);
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct, contratada: false);
        await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        await PagarPlano(formaturaId, 4990);

        // Act
        var depois = await Analytics(suporte);

        // Assert
        depois.Formaturas.Total.ShouldBe(antes.Formaturas.Total + 1);
        depois.Formaturas.NovasNoPeriodo.ShouldBe(antes.Formaturas.NovasNoPeriodo + 1);
        depois.Formaturas.Pagantes.ShouldBe(antes.Formaturas.Pagantes + 1);
        depois.Formaturas.PorLicenca.Sum(l => l.Turmas).ShouldBe(depois.Formaturas.Total);
        depois.Kapa.RecebidoEmCentavos.ShouldBe(antes.Kapa.RecebidoEmCentavos + 4990);
        depois.Kapa.MrrEmCentavos.ShouldBeGreaterThan(antes.Kapa.MrrEmCentavos);
        depois.Contas.Total.ShouldBeGreaterThanOrEqualTo(depois.Contas.Confirmadas);
        depois.Uso.ShouldAllBe(u => u.Eventos >= u.Usuarios && !u.Recurso.Contains('.'));
    }

    [Fact]
    public async Task Analytics_recusa_periodo_invertido()
    {
        var suporte = await Suporte();

        var resposta = await suporte.GetAsync("/api/v1/admin/analytics?de=2026-09-10&ate=2026-09-01", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await resposta.Codigo(Ct)).ShouldBe("analytics.periodo_invertido");
    }

    /// <summary>A série tem um mês por linha, em ordem, terminando no mês corrente.</summary>
    [Fact]
    public async Task Serie_mensal_vem_com_um_mes_por_linha_ate_o_mes_corrente()
    {
        var suporte = await Suporte();

        var serie = await suporte.GetFromJsonAsync<MesDaPlataformaDTO[]>("/api/v1/admin/analytics/serie?meses=12", Json, Ct);

        serie.ShouldNotBeNull();
        serie.Length.ShouldBe(12);
        var hoje = DateTime.UtcNow.AddHours(-3);
        (serie[^1].Ano, serie[^1].Mes).ShouldBe((hoje.Year, hoje.Month));
        serie.Sum(m => m.Cadastros).ShouldBeGreaterThan(0);
    }

    private static async Task<AnalyticsDaPlataformaDTO> Analytics(HttpClient suporte) =>
        (await suporte.GetFromJsonAsync<AnalyticsDaPlataformaDTO>("/api/v1/admin/analytics", Json, Ct))!;

    private async Task<HttpClient> Suporte()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.AutenticarComoAdministrador(Ct);

        return cliente.ComToken(tokens.AccessToken);
    }

    /// <summary>Uma assinatura ativa com o pagamento do ciclo confirmado agora — o que o webhook deixaria gravado.</summary>
    private async Task PagarPlano(Guid formaturaId, long valorEmCentavos)
    {
        await using var contexto = fabrica.ContextoDe(formaturaId);

        var plano = await contexto.Planos.FirstAsync(p => p.Codigo == "premium", Ct);
        var assinatura = new Assinatura { PlanoId = plano.Id };
        assinatura.ConfirmarPagamento(DateTime.UtcNow, plano.Ciclo);
        contexto.Assinaturas.Add(assinatura);

        var cobranca = CobrancaDaAssinatura.Abrir(assinatura.Id, plano.Id, MotivoDaCobranca.Ciclo, MeioDePagamento.Cartao, valorEmCentavos);
        cobranca.Pagar($"pag-{Guid.NewGuid():N}", valorEmCentavos, DateTime.UtcNow);
        contexto.CobrancasDaAssinatura.Add(cobranca);

        await contexto.SaveChangesAsync(Ct);
    }
}
