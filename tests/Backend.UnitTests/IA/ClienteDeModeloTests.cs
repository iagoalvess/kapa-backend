using System.Net;
using System.Text.Json;
using Backend.Business.IA.Models;
using Backend.Business.IA.Services;
using Backend.Business.IA.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Backend.UnitTests.IA;

/// <summary>
/// O cliente do provedor sobre um <see cref="HttpMessageHandler"/> de mentira: o que vai na requisição,
/// a queda para o próximo modelo e o desligamento pela chave vazia.
/// </summary>
public sealed class ClienteDeModeloTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly PedidoAoModelo Pedido = new("Resuma.", "# Termo\n\nMulta de 2%.", ["primeiro:free", "segundo:free"]);

    private static ClienteDeModelo Cliente(Provedor provedor, string chave = "sk-teste") =>
        new(new HttpClient(provedor), Options.Create(new IaSettings { ApiKey = chave }), NullLogger<ClienteDeModelo>.Instance);

    [Fact]
    public async Task Chave_vazia_nao_chama_ninguem()
    {
        var provedor = new Provedor(_ => Resposta("não devia chegar aqui"));

        var resultado = await Cliente(provedor, chave: "").Completar(Pedido, Ct);

        resultado.PrimeiroErro.Codigo.ShouldBe("ia.desligada");
        provedor.Corpos.ShouldBeEmpty();
    }

    /// <summary>Critério da Sprint 24: o que sai é o modelo, a instrução e o texto — e mais nada.</summary>
    [Fact]
    public async Task A_requisicao_leva_so_o_modelo_a_instrucao_e_o_texto()
    {
        var provedor = new Provedor(_ => Resposta("  Você paga 12 parcelas.  "));

        var resultado = await Cliente(provedor).Completar(Pedido, Ct);

        resultado.Valor.ShouldBe(new RespostaDoModelo("Você paga 12 parcelas.", "primeiro:free"));
        provedor.Enderecos.ShouldBe(["https://openrouter.ai/api/v1/chat/completions"]);
        provedor.Autorizacoes.ShouldBe(["Bearer sk-teste"]);

        using var corpo = JsonDocument.Parse(provedor.Corpos.Single());
        corpo.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["model", "messages"]);
        corpo.RootElement.GetProperty("model").GetString().ShouldBe("primeiro:free");
        var mensagens = corpo.RootElement.GetProperty("messages").EnumerateArray().ToList();
        mensagens.Select(m => m.GetProperty("role").GetString()).ShouldBe(["system", "user"]);
        mensagens[0].GetProperty("content").GetString().ShouldBe("Resuma.");
        mensagens[1].GetProperty("content").GetString().ShouldBe(Pedido.Texto);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "{}")]
    [InlineData(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"   \"}}]}")]
    [InlineData(HttpStatusCode.OK, "isto não é json")]
    public async Task Falha_no_primeiro_modelo_cai_para_o_segundo(HttpStatusCode status, string corpoDoPrimeiro)
    {
        var provedor = new Provedor(modelo =>
            modelo == "primeiro:free" ? new HttpResponseMessage(status) { Content = new StringContent(corpoDoPrimeiro) } : Resposta("Resumo.")
        );

        var resultado = await Cliente(provedor).Completar(Pedido, Ct);

        resultado.Valor.Modelo.ShouldBe("segundo:free");
        provedor.Corpos.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Lista_esgotada_devolve_indisponivel()
    {
        var provedor = new Provedor(_ => throw new HttpRequestException("fora do ar"));

        var resultado = await Cliente(provedor).Completar(Pedido, Ct);

        resultado.PrimeiroErro.Codigo.ShouldBe("ia.indisponivel");
        provedor.Corpos.Count.ShouldBe(2);
    }

    private static HttpResponseMessage Resposta(string texto) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = texto } } } })
            ),
        };

    /// <summary>Guarda cada requisição e responde conforme o modelo pedido.</summary>
    private sealed class Provedor(Func<string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> Corpos { get; } = [];

        public List<string> Enderecos { get; } = [];

        public List<string> Autorizacoes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var corpo = await request.Content!.ReadAsStringAsync(cancellationToken);
            Corpos.Add(corpo);
            Enderecos.Add(request.RequestUri!.ToString());
            Autorizacoes.Add(request.Headers.Authorization!.ToString());

            using var json = JsonDocument.Parse(corpo);

            return responder(json.RootElement.GetProperty("model").GetString()!);
        }
    }
}
