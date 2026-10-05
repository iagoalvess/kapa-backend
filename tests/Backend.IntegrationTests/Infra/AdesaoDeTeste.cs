using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Backend.Api.DTOs.Adesoes;
using Backend.Api.DTOs.Formandos;

namespace Backend.IntegrationTests.Infra;

/// <summary>
/// O caminho do formando até ter parcelas: cadastro com nome, CPF e nascimento, e o aceite do termo.
/// </summary>
/// <remarks>
/// Pela API, como a tela faz: a adesão é o único caminho que gera parcela, e o snapshot que ela grava é
/// o que a Sprint 9 lê para multa e juros. Usado pelos testes de adesão e de pagamento.
/// </remarks>
public static partial class AdesaoDeTeste
{
    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Preenche o que a adesão exige no cadastro do próprio membro.</summary>
    /// <param name="cliente">Cliente do membro.</param>
    /// <param name="cpf">CPF, diferente por teste (<see cref="NovoCpf"/>).</param>
    /// <param name="nascimento">Data de nascimento; ausente, um adulto.</param>
    /// <param name="nome">Nome civil.</param>
    public static async Task PreencherCadastro(HttpClient cliente, string cpf, DateOnly? nascimento = null, string nome = "Ana Souza") =>
        (
            await cliente.PutAsJsonAsync(
                "/api/v1/formandos/eu",
                new AtualizarPerfilRequestDTO(
                    new DadosPessoaisDTO(nome, null, cpf, null, null, "(41) 99876-5432", nascimento ?? new DateOnly(2000, 5, 20), null),
                    null,
                    null
                ),
                Json,
                Ct
            )
        ).EnsureSuccessStatusCode();

    /// <summary>
    /// O que a tela faz: lê o conteúdo vigente, pede o código por e-mail e adere com os dois.
    /// </summary>
    /// <remarks>
    /// O código é lido da fila de e-mail, como o formando o leria na caixa de entrada — é o caminho
    /// inteiro, do <c>GenerateUserTokenAsync</c> ao corpo da mensagem, e não um atalho pelo provedor.
    /// </remarks>
    /// <param name="fabrica">API de teste, para ler o e-mail que trouxe o código.</param>
    /// <param name="cliente">Cliente do membro.</param>
    /// <param name="codigo">Código a enviar; ausente, o que chegou no e-mail.</param>
    /// <param name="pacotes">A cesta; ausente, o primeiro pacote do catálogo.</param>
    public static async Task<(HttpResponseMessage Resposta, string? Hash)> Aderir(
        ApiFactory fabrica,
        HttpClient cliente,
        string? codigo = null,
        IReadOnlyList<Guid>? pacotes = null
    )
    {
        var catalogo = (await cliente.GetFromJsonAsync<ConteudoParaAdesaoDTO>("/api/v1/adesoes/termos/vigente", Json, Ct))!.Catalogo;
        IReadOnlyList<Guid> cesta = pacotes ?? (catalogo.Count > 0 ? [catalogo[0].Id] : []);
        var conteudo = await cliente.GetFromJsonAsync<ConteudoParaAdesaoDTO>(Termo(cesta), Json, Ct);
        var corpo = new AderirRequestDTO(conteudo!.HashDoConteudo, codigo ?? await PedirCodigo(fabrica, cliente), cesta);

        return (await cliente.PostAsJsonAsync("/api/v1/adesoes", corpo, Json, Ct), conteudo.HashDoConteudo);
    }

    /// <summary>A rota do termo vigente com a cesta na query — é ela que dá o hash do que se aceita.</summary>
    /// <param name="pacotes">Pacotes escolhidos.</param>
    public static string Termo(IEnumerable<Guid> pacotes) =>
        "/api/v1/adesoes/termos/vigente" + (pacotes.Any() ? "?" + string.Join("&", pacotes.Select(id => $"pacotes={id}")) : string.Empty);

    /// <summary>Pede o código do aceite e o lê do e-mail que entrou na fila.</summary>
    /// <param name="fabrica">API de teste, para ler a fila de e-mail.</param>
    /// <param name="cliente">Cliente do membro.</param>
    public static async Task<string?> PedirCodigo(ApiFactory fabrica, HttpClient cliente)
    {
        var envio = await cliente.PostAsync("/api/v1/adesoes/codigo", null, Ct);
        if (!envio.IsSuccessStatusCode)
            return null;

        var email = await fabrica.UltimoEmailDaFila(Ct);

        return email is null ? null : SeisDigitos().Match(email).Value;
    }

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex SeisDigitos();

    /// <summary>Um CPF válido e diferente a cada chamada: o teste de CPF repetido não pode esbarrar noutro teste.</summary>
    public static string NovoCpf()
    {
        var digitos = new List<int>(11);

        do
        {
            digitos.Clear();
            digitos.AddRange(Enumerable.Range(0, 9).Select(_ => Random.Shared.Next(10)));
        } while (digitos.Distinct().Count() == 1);

        for (var quantos = 9; quantos <= 10; quantos++)
        {
            var resto = digitos.Select((digito, i) => digito * (quantos + 1 - i)).Sum() % 11;
            digitos.Add(resto < 2 ? 0 : 11 - resto);
        }

        return string.Concat(digitos);
    }
}
