using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Shouldly;

namespace Backend.UnitTests.Documentacao;

/// <summary>
/// Trava o <c>docs/erros.md</c> contra o código: todo <c>codigo</c> emitido tem âncora, e toda âncora
/// é de um código que ainda existe.
/// </summary>
/// <remarks>
/// O <c>type</c> do problem details aponta para <c>docs/erros.md#{codigo}</c>. A doc é escrita à mão,
/// então código novo sem linha lá é um link quebrado que ninguém percebe — até alguém clicar.
/// <para>
/// Lê os literais de <c>src/</c> por regex, sem compilar nada: <c>Erro.Tipo("recurso.motivo", …)</c>,
/// <c>new Erro("recurso.motivo", …)</c>, <c>.WithErrorCode("recurso.motivo")</c> (ou a constante
/// passada a ele) e o <c>AuthConfig.EscreverProblema</c> do 401/403 do pipeline. Código montado de
/// outro jeito entra nas exceções abaixo, com o motivo.
/// </para>
/// </remarks>
public sealed partial class CatalogoDeErrosTests
{
    /// <summary>
    /// Códigos que não passam por nenhum dos formatos acima: vêm de uma constante fora do domínio.
    /// </summary>
    /// <remarks>
    /// <c>erro.inesperado</c> é o <c>DocDeErros.Inesperado</c>, do <c>GlobalExceptionHandler</c>;
    /// <c>rate_limit.excedido</c> é o <c>CodigoDoExcesso</c> do <c>RateLimitConfig</c>.
    /// </remarks>
    private static readonly string[] ForaDoDominio = ["erro.inesperado", "rate_limit.excedido"];

    /// <summary>
    /// Prefixo dos erros do ASP.NET Identity, montados em tempo de execução.
    /// </summary>
    /// <remarks>
    /// <c>AuthService</c> e <c>ContaService</c> emitem <c>$"identity.{ParaSnakeCase(erro.Code)}"</c>: o
    /// código vem do Identity e não aparece como literal. A doc lista os que o projeto pode disparar, e
    /// o teste não os confere em nenhum dos dois sentidos.
    /// </remarks>
    private const string PrefixoDoIdentity = "identity.";

    [Fact]
    public async Task Todo_codigo_emitido_tem_ancora_na_doc()
    {
        var (emitidos, ancoras) = await Levantar();

        var semAncora = emitidos.Where(codigo => !ancoras.Contains(codigo)).Order(StringComparer.Ordinal).ToList();

        semAncora.ShouldBeEmpty($"Códigos sem linha em docs/erros.md: {string.Join(", ", semAncora)}");
    }

    [Fact]
    public async Task Toda_ancora_da_doc_e_de_um_codigo_emitido()
    {
        var (emitidos, ancoras) = await Levantar();

        var mortas = ancoras
            .Where(ancora => !emitidos.Contains(ancora) && !ancora.StartsWith(PrefixoDoIdentity, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        mortas.ShouldBeEmpty($"Códigos em docs/erros.md que nada mais emite: {string.Join(", ", mortas)}");
    }

    private static async Task<(HashSet<string> Emitidos, HashSet<string> Ancoras)> Levantar()
    {
        var ct = TestContext.Current.CancellationToken;
        var raiz = Raiz();

        var fontes = new List<string>();
        foreach (var arquivo in Directory.EnumerateFiles(Path.Combine(raiz, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var segmentos = arquivo.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!segmentos.Contains("bin") && !segmentos.Contains("obj"))
                fontes.Add(await File.ReadAllTextAsync(arquivo, ct));
        }

        var constantes = fontes
            .SelectMany(fonte => Constante().Matches(fonte))
            .GroupBy(m => m.Groups["nome"].Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Groups["codigo"].Value, StringComparer.Ordinal);

        var emitidos = new HashSet<string>(ForaDoDominio, StringComparer.Ordinal);
        foreach (var fonte in fontes)
        {
            emitidos.UnionWith(Literal().Matches(fonte).Select(m => m.Groups["codigo"].Value));

            foreach (Match m in CodigoPorConstante().Matches(fonte))
                emitidos.Add(constantes.TryGetValue(m.Groups["nome"].Value, out var codigo) ? codigo : $"?{m.Groups["nome"].Value}");
        }

        var doc = await File.ReadAllTextAsync(Path.Combine(raiz, "docs", "erros.md"), ct);
        var ancoras = Ancora().Matches(doc).Select(m => m.Groups["codigo"].Value).ToHashSet(StringComparer.Ordinal);

        return (emitidos, ancoras);
    }

    /// <summary>A raiz do repositório: a pasta do <c>Backend.slnx</c>.</summary>
    /// <remarks>
    /// Sobe da saída do teste; se o build foi para fora do repositório (<c>--artifacts-path</c>), sobe
    /// da pasta deste arquivo-fonte.
    /// </remarks>
    private static string Raiz([CallerFilePath] string esteArquivo = "")
    {
        foreach (var inicio in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(esteArquivo) })
        {
            for (var pasta = string.IsNullOrEmpty(inicio) ? null : new DirectoryInfo(inicio); pasta is not null; pasta = pasta.Parent)
            {
                if (File.Exists(Path.Combine(pasta.FullName, "Backend.slnx")))
                    return pasta.FullName;
            }
        }

        throw new InvalidOperationException("Backend.slnx não encontrado acima da saída do teste.");
    }

    [GeneratedRegex(
        """(?:\bErro\.(?:Validacao|NaoEncontrado|Conflito|NaoAutenticado|Proibido|Indisponivel|Excesso)|\bnew Erro|\.WithErrorCode|\bEscreverProblema\(\s*[^,()]+,\s*[^,()]+,)\(?\s*"(?<codigo>[a-z_]+\.[a-z_]+)"(?=[\s,)])"""
    )]
    private static partial Regex Literal();

    [GeneratedRegex(@"\.WithErrorCode\(\s*(?:\w+\.)*(?<nome>\w+)\s*\)")]
    private static partial Regex CodigoPorConstante();

    [GeneratedRegex("""const string (?<nome>\w+) = "(?<codigo>[a-z_]+\.[a-z_]+)";""")]
    private static partial Regex Constante();

    [GeneratedRegex("""<a id="(?<codigo>[^"]+)"></a>""")]
    private static partial Regex Ancora();
}
