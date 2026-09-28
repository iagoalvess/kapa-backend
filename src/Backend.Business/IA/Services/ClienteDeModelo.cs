using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Business.Abstractions;
using Backend.Business.IA.Interfaces;
using Backend.Business.IA.Models;
using Backend.Business.IA.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Business.IA.Services;

/// <summary>
/// <see cref="IModeloDeLinguagem"/> sobre o <c>POST chat/completions</c> no formato da OpenAI — o do
/// OpenRouter e da maioria dos provedores.
/// </summary>
/// <remarks>
/// Sem SDK e sem dependência nova: <see cref="HttpClient"/> + <c>System.Net.Http.Json</c>, um objeto de
/// ida e um de volta. O <see cref="HttpClient"/> é único, com conexões recicladas (registro no
/// <c>DependenciasBusiness</c>), e cada chamada tem o próprio tempo limite, em
/// <see cref="IaSettings.SegundosDeEspera"/>.
/// <para>
/// O corpo leva só <c>model</c> e as duas mensagens — instrução e texto. Nada além do que a feature pôs
/// no <see cref="PedidoAoModelo"/> sai da plataforma, e o teste da requisição confere.
/// </para>
/// </remarks>
/// <param name="http">Cliente HTTP compartilhado.</param>
/// <param name="options">Provedor e chave.</param>
/// <param name="logger">Log estruturado — cada modelo que falha deixa uma linha.</param>
public sealed class ClienteDeModelo(HttpClient http, IOptions<IaSettings> options, ILogger<ClienteDeModelo> logger) : IModeloDeLinguagem
{
    private readonly IaSettings _config = options.Value;

    /// <inheritdoc />
    public bool Ligado => _config.Ligado;

    /// <inheritdoc />
    public async Task<Result<RespostaDoModelo>> Completar(PedidoAoModelo pedido, CancellationToken ct = default)
    {
        if (!Ligado)
            return Erro.Indisponivel("ia.desligada", "A IA não está configurada.");

        foreach (var modelo in pedido.Modelos)
        {
            var texto = await Pedir(modelo, pedido, ct);

            if (!string.IsNullOrWhiteSpace(texto))
                return new RespostaDoModelo(texto.Trim(), modelo);
        }

        return Erro.Indisponivel("ia.indisponivel", "Nenhum modelo devolveu resposta.");
    }

    private async Task<string?> Pedir(string modelo, PedidoAoModelo pedido, CancellationToken ct)
    {
        var corpo = new Corpo(modelo, [new Mensagem("system", pedido.Instrucao), new Mensagem("user", pedido.Texto)]);

        using var requisicao = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_config.BaseUrl), "chat/completions"))
        {
            Content = JsonContent.Create(corpo),
        };
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);

        using var espera = CancellationTokenSource.CreateLinkedTokenSource(ct);
        espera.CancelAfter(TimeSpan.FromSeconds(_config.SegundosDeEspera));

        try
        {
            using var resposta = await http.SendAsync(requisicao, espera.Token);

            if (!resposta.IsSuccessStatusCode)
            {
                logger.LogWarning("Modelo {Modelo} recusou o pedido com HTTP {Status}.", modelo, (int)resposta.StatusCode);
                return null;
            }

            var lido = await resposta.Content.ReadFromJsonAsync<Resposta>(espera.Token);
            var texto = lido?.Choices is [var primeira, ..] ? primeira.Message?.Content : null;

            if (string.IsNullOrWhiteSpace(texto))
                logger.LogWarning("Modelo {Modelo} devolveu resposta vazia.", modelo);

            return texto;
        }
        catch (Exception excecao) when (excecao is HttpRequestException or JsonException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(excecao, "Modelo {Modelo} falhou.", modelo);
            return null;
        }
    }

    private sealed record Corpo(string Model, IReadOnlyList<Mensagem> Messages);

    private sealed record Mensagem(string Role, string? Content);

    private sealed record Resposta(IReadOnlyList<Escolha>? Choices);

    private sealed record Escolha(Mensagem? Message);
}
