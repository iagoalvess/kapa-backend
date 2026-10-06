using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Formaturas;
using Backend.Api.DTOs.Recebimentos;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Shouldly;

namespace Backend.IntegrationTests.Formaturas;

/// <summary>
/// Os primeiros passos da comissão, calculados numa consulta só: quem lê e o que cada passo enxerga no banco.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class PrimeirosPassosEndpointsTests(ApiFactory fabrica)
{
    private const string Rota = "/api/v1/formaturas/atual/primeiros-passos";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Da Tesouraria, que é quem vê o bloco no Início.</summary>
    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task So_a_tesouraria_le(string papel, HttpStatusCode esperado)
    {
        // Arrange
        var membro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), papel, Ct);

        // Act
        var resposta = await membro.Cliente.GetAsync(Rota, Ct);

        // Assert
        resposta.StatusCode.ShouldBe(esperado);
    }

    /// <summary>A turma recém-criada no gratuito, só com o Presidente: nenhum passo feito, e o contrato em snake_case.</summary>
    [Fact]
    public async Task Turma_nova_no_gratuito_nao_tem_passo_feito()
    {
        // Arrange
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct, contratada: false);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        // Act
        var corpo = await presidente.Cliente.GetStringAsync(Rota, Ct);

        // Assert
        corpo.ShouldBe(
            """{"comissao_montada":false,"plano_de_cobranca_em_vigor":false,"termo_publicado":false,"recebimentos_configurados":false,"plano_contratado":false,"formandos_na_turma":false,"concluidos":false}"""
        );
    }

    /// <summary>
    /// Cada passo enxerga o seu: plano em vigor e termo, a transferência (que dispensa conferência), o formando que
    /// entrou e a tesouraria na comissão. Com todos, o bloco some.
    /// </summary>
    [Fact]
    public async Task Turma_montada_conclui_os_passos()
    {
        // Arrange
        var turma = await fabrica.TurmaComPlano();
        var presidente = turma.Presidente.Cliente;
        var antes = await presidente.GetFromJsonAsync<PrimeirosPassosDTO>(Rota, Json, Ct);
        (
            await presidente.GravarMeios(
                fabrica,
                new MeiosDaContaDTO(null, new DadosBancariosDTO("Banco do Brasil", "1234", "5678-9", "Corrente", "Comissão"), null),
                Ct
            )
        ).EnsureSuccessStatusCode();
        await fabrica.FormandoComAdesao(turma.FormaturaId);
        await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Tesoureiro, Ct);

        // Act
        var depois = await presidente.GetFromJsonAsync<PrimeirosPassosDTO>(Rota, Json, Ct);

        // Assert
        antes.ShouldBe(new PrimeirosPassosDTO(false, true, true, false, true, false, false));
        depois.ShouldBe(new PrimeirosPassosDTO(true, true, true, true, true, true, true));
    }

    /// <summary>O PIX só conta depois de o titular ser conferido: sem a conferência, o recebimento continua pendente.</summary>
    [Fact]
    public async Task Pix_sem_conferencia_nao_conta()
    {
        // Arrange
        var turma = await fabrica.TurmaComPlano();
        await turma.Presidente.Cliente.CadastrarChavePix(fabrica, Ct);

        // Act
        var passos = await turma.Presidente.Cliente.GetFromJsonAsync<PrimeirosPassosDTO>(Rota, Json, Ct);

        // Assert
        passos!.RecebimentosConfigurados.ShouldBeFalse();
    }
}
