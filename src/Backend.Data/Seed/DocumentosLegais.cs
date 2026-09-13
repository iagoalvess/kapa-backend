using Microsoft.EntityFrameworkCore.Migrations;

namespace Backend.Data.Seed;

/// <summary>
/// Publicação dos documentos legais a partir dos arquivos em <c>Seed/legal/</c>.
/// </summary>
/// <remarks>
/// Publicar é uma <b>migration</b>, e não o <c>SeedInicial</c>: o seed roda uma vez na primeira
/// subida e é desligado em produção (<c>docs/operacao.md</c>), então uma versão nova dos termos
/// nunca chegaria lá. A migration roda exatamente uma vez por banco, no passo de deploy, e o
/// texto passa por pull request antes.
/// <para>
/// Versão nova: crie <c>Seed/legal/TermosDeUso.2.md</c>, gere uma migration vazia e chame
/// <see cref="Publicar"/> no <c>Up</c> com a data de vigência. Nunca altere um arquivo já
/// publicado — o banco recusa a alteração, e o texto do repositório deixaria de ser o que as
/// pessoas aceitaram.
/// </para>
/// </remarks>
public static class DocumentosLegais
{
    /// <summary>
    /// Insere uma versão de documento lida do arquivo embutido <c>{tipo}.{versao}.md</c>.
    /// </summary>
    /// <param name="migrationBuilder">Construtor da migration.</param>
    /// <param name="id">Identificador fixo da versão — migration não gera valor aleatório.</param>
    /// <param name="tipo">Documento, na grafia de <c>TipoDeDocumento</c>.</param>
    /// <param name="versao">Rótulo da versão.</param>
    /// <param name="vigenteDesde">A partir de quando vale, em UTC.</param>
    public static void Publicar(MigrationBuilder migrationBuilder, Guid id, string tipo, string versao, DateTime vigenteDesde) =>
        migrationBuilder.InsertData(
            table: "documentos_legais",
            columns: ["id", "tipo", "versao", "conteudo", "vigente_desde"],
            values: [id, tipo, versao, Ler(tipo, versao), vigenteDesde]
        );

    /// <summary>Lê o texto de uma versão embutida no assembly.</summary>
    /// <remarks>
    /// Quebras de linha normalizadas para <c>\n</c>: com <c>core.autocrlf</c>, o mesmo arquivo sai
    /// com <c>\r\n</c> no Windows e <c>\n</c> no CI, e o texto publicado dependeria da máquina de
    /// quem gerou o script de deploy.
    /// </remarks>
    /// <param name="tipo">Documento.</param>
    /// <param name="versao">Rótulo da versão.</param>
    /// <exception cref="InvalidOperationException">Se o arquivo não estiver embutido.</exception>
    public static string Ler(string tipo, string versao)
    {
        var nome = $"legal/{tipo}.{versao}.md";

        using var fluxo =
            typeof(DocumentosLegais).Assembly.GetManifestResourceStream(nome)
            ?? throw new InvalidOperationException($"Documento legal '{nome}' não está embutido em Backend.Data.");

        using var leitor = new StreamReader(fluxo);

        return leitor.ReadToEnd().ReplaceLineEndings("\n");
    }
}
