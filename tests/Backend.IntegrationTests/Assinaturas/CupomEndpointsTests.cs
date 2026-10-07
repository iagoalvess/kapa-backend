using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Assinaturas;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Backend.IntegrationTests.Assinaturas;

/// <summary>
/// Sprint 51 de ponta a ponta: o Administrador cria o cupom, a turma consulta e contrata, e as travas seguram.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class CupomEndpointsTests(ApiFactory fabrica)
{
    private const string Cupons = "/api/v1/admin/cupons";

    private const string Assinatura = "/api/v1/formaturas/atual/assinatura";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Administrador_cria_e_a_turma_contrata_com_desconto()
    {
        // Arrange
        var suporte = await Suporte();
        var cupom = await Criar(suporte, 50, 3);
        var (presidente, _) = await Turma();

        // Act
        var consulta = await presidente.GetFromJsonAsync<CupomAplicavelDTO>($"{Assinatura}/cupom/{cupom.Codigo.ToLowerInvariant()}", Json, Ct);
        var checkout = await presidente.PostAsJsonAsync(
            $"{Assinatura}/checkout",
            new IniciarCheckoutRequestDTO("essencial", CupomCodigo: cupom.Codigo),
            Json,
            Ct
        );
        var lista = await suporte.GetFromJsonAsync<CupomDTO[]>(Cupons, Json, Ct);

        // Assert
        consulta!.Percentual.ShouldBe(50);
        checkout.StatusCode.ShouldBe(HttpStatusCode.OK);
        lista!.Single(c => c.Id == cupom.Id).Usos.ShouldBe(1);
        (await presidente.GetFromJsonAsync<AssinaturaDTO>(Assinatura, Json, Ct))!.Cupom!.Percentual.ShouldBe(50);
    }

    /// <summary>Critério 2: com o último uso, dois checkouts ao mesmo tempo — um passa, o outro recebe a resposta única.</summary>
    [Fact]
    public async Task Ultimo_uso_disputado_por_duas_turmas_so_uma_leva()
    {
        // Arrange
        var cupom = await Criar(await Suporte(), 30, 1);
        var (primeira, _) = await Turma();
        var (segunda, _) = await Turma();

        // Act
        var respostas = await Task.WhenAll(
            primeira.PostAsJsonAsync($"{Assinatura}/checkout", new IniciarCheckoutRequestDTO("essencial", CupomCodigo: cupom.Codigo), Json, Ct),
            segunda.PostAsJsonAsync($"{Assinatura}/checkout", new IniciarCheckoutRequestDTO("essencial", CupomCodigo: cupom.Codigo), Json, Ct)
        );

        // Assert
        respostas.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        var recusada = respostas.Single(r => r.StatusCode != HttpStatusCode.OK);
        recusada.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await recusada.Content.ReadFromJsonAsync<ProblemDetails>(Json, Ct))!.Extensions["codigo"]!.ToString().ShouldBe("cupom.invalido");
        await using var contexto = fabrica.ContextoDe(null);
        (await contexto.Cupons.SingleAsync(c => c.Id == cupom.Id, Ct)).Usos.ShouldBe(1);
    }

    /// <summary>Critérios 3 e 4: turma que já pagou recebe a resposta única; retomar a pendente não gasta outro uso.</summary>
    [Fact]
    public async Task Turma_que_ja_pagou_nao_usa_e_retomar_nao_gasta_outro_uso()
    {
        // Arrange
        var cupom = await Criar(await Suporte(), 20, 5);
        var (pagante, formaturaPagante) = await Turma();
        await Pagar(formaturaPagante);
        var (nova, _) = await Turma();

        // Act
        var recusada = await pagante.GetAsync($"{Assinatura}/cupom/{cupom.Codigo}", Ct);
        await nova.PostAsJsonAsync($"{Assinatura}/checkout", new IniciarCheckoutRequestDTO("essencial", CupomCodigo: cupom.Codigo), Json, Ct);
        var retomada = await nova.PostAsJsonAsync($"{Assinatura}/checkout", new IniciarCheckoutRequestDTO("premium"), Json, Ct);

        // Assert
        recusada.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        retomada.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var contexto = fabrica.ContextoDe(null);
        (await contexto.Cupons.SingleAsync(c => c.Id == cupom.Id, Ct)).Usos.ShouldBe(1);
    }

    /// <summary>Critério 8: 51% é recusado na API e no banco.</summary>
    [Fact]
    public async Task Cupom_acima_de_50_por_cento_e_recusado_na_api_e_no_banco()
    {
        // Act
        var resposta = await (await Suporte()).PostAsJsonAsync(Cupons, Novo(51, 1), Json, Ct);
        await using var contexto = fabrica.ContextoDe(null);
        contexto.Cupons.Add(
            new Cupom
            {
                Codigo = Codigo(),
                Percentual = 51,
                ValidoAte = DateTime.UtcNow.AddDays(1),
                LimiteDeUsos = 1,
            }
        );

        // Assert
        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await Should.ThrowAsync<DbUpdateException>(() => contexto.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Desativado_deixa_de_valer_na_hora()
    {
        // Arrange
        var suporte = await Suporte();
        var cupom = await Criar(suporte, 10, 5);
        var (presidente, _) = await Turma();

        // Act
        (await suporte.PostAsync($"{Cupons}/{cupom.Id}/desativar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var consulta = await presidente.GetAsync($"{Assinatura}/cupom/{cupom.Codigo}", Ct);

        // Assert
        consulta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>Fronteira de autorização: cupom é do Administrador; consultar é do Presidente.</summary>
    [Fact]
    public async Task Cupons_sao_so_do_administrador_e_consultar_so_do_presidente()
    {
        // Arrange
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct, contratada: false);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var tesoureiro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Tesoureiro, Ct);

        // Act & Assert
        (await fabrica.CreateClient().GetAsync(Cupons, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await presidente.Cliente.GetAsync(Cupons, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await presidente.Cliente.PostAsJsonAsync(Cupons, Novo(10, 1), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await tesoureiro.Cliente.GetAsync($"{Assinatura}/cupom/QUALQUER1", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Critério 5: errar o cupom em sequência esbarra no limite por usuário — o campo não vira adivinhador.</summary>
    [Fact]
    public async Task Tentativas_seguidas_de_cupom_recebem_429()
    {
        // Arrange
        await using var apertada = fabrica.WithWebHostBuilder(host => host.UseSetting("RateLimit:CupomPorMinuto", "3"));
        var (presidente, _) = await Turma();
        var cliente = apertada.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = presidente.DefaultRequestHeaders.Authorization;

        // Act
        var respostas = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            respostas.Add((await cliente.GetAsync($"{Assinatura}/cupom/CHUTE-{i}000", Ct)).StatusCode);

        // Assert
        respostas.Take(3).ShouldAllBe(status => status == HttpStatusCode.BadRequest);
        respostas.Last().ShouldBe(HttpStatusCode.TooManyRequests);
    }

    private static string Codigo() => $"C{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    private static NovoCupomRequestDTO Novo(int percentual, int limite) => new(Codigo(), percentual, DataUtils.Hoje().AddDays(10), limite);

    private static async Task<CupomDTO> Criar(HttpClient suporte, int percentual, int limite)
    {
        var resposta = await suporte.PostAsJsonAsync(Cupons, Novo(percentual, limite), Json, Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync(Ct));

        return (await resposta.Content.ReadFromJsonAsync<CupomDTO>(Json, Ct))!;
    }

    private async Task<(HttpClient Presidente, Guid FormaturaId)> Turma()
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct, contratada: false);

        return ((await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct)).Cliente, formaturaId);
    }

    /// <summary>Uma assinatura paga gravada direto no banco — o que o webhook deixaria.</summary>
    private async Task Pagar(Guid formaturaId)
    {
        await using var contexto = fabrica.ContextoDe(formaturaId);
        var assinatura = new Assinatura { PlanoId = (await contexto.Planos.SingleAsync(p => p.Codigo == "essencial", Ct)).Id };
        assinatura.ConfirmarPagamento(DateTime.UtcNow, CicloDeCobranca.Mensal);
        contexto.Assinaturas.Add(assinatura);
        await contexto.SaveChangesAsync(Ct);
    }

    private async Task<HttpClient> Suporte()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.AutenticarComoAdministrador(fabrica, Ct);

        return cliente.ComToken(tokens.AccessToken);
    }
}
