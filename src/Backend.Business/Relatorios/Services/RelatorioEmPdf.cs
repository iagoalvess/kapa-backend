using Backend.Business.Common.Pdf;
using Backend.Business.Relatorios.Models;

namespace Backend.Business.Relatorios.Services;

/// <summary>
/// A rotina padrão de relatório: uma <see cref="TabelaDoRelatorio"/> impressa com a capa, a tabela e
/// o rodapé que todo relatório do produto tem.
/// </summary>
/// <remarks>
/// Serve despesas, parcelas e fornecedores — as três listas. O balancete tem documento próprio
/// (<see cref="BalanceteEmPdf"/>) porque não é uma tabela: tem resumo e três quadros; mas abre com a
/// mesma <see cref="DocumentoPdf.Capa"/> e fecha com o mesmo rodapé, então os quatro chegam à
/// assembleia com a mesma cara.
/// <para>
/// A orientação é <b>consequência do número de colunas</b>, e não escolha de quem escreveu o
/// relatório: de seis colunas para cima não há folha em pé que caiba a lista sem espremer a
/// descrição em três linhas. Quatro colunas ou menos ficam em retrato, que é o que se imprime e se
/// arquiva.
/// </para>
/// </remarks>
public static class RelatorioEmPdf
{
    /// <summary>De quantas colunas em diante a folha vira paisagem.</summary>
    private const int ColunasQuePedemPaisagem = 6;

    /// <summary>Monta o arquivo.</summary>
    /// <remarks>
    /// Período sem lançamento ganha uma frase, e não um cabeçalho com nada embaixo: tabela vazia
    /// parece relatório quebrado, e quem recebeu vai perguntar.
    /// </remarks>
    /// <param name="tabela">O relatório já reduzido a colunas e linhas.</param>
    public static byte[] Gerar(TabelaDoRelatorio tabela)
    {
        var pdf = new DocumentoPdf(tabela.Colunas.Count >= ColunasQuePedemPaisagem ? OrientacaoDaPagina.Paisagem : OrientacaoDaPagina.Retrato).Capa(
            tabela.Titulo,
            tabela.Subtitulo,
            Contagem(tabela.Linhas.Count)
        );

        if (tabela.Linhas.Count == 0)
            pdf.Paragrafo("Nenhum lançamento no período.");
        else
            pdf.Tabela(
                [.. tabela.Colunas.Select(coluna => (coluna.Largura, coluna.Direita))],
                [.. tabela.Colunas.Select(coluna => coluna.Nome)],
                tabela.Linhas.Select(linha => linha.Select(celula => celula.Texto).ToArray())
            );

        return pdf.Gerar($"{tabela.Titulo} · {tabela.Subtitulo}");
    }

    /// <summary>Quantos registros o arquivo traz — quem confere lista confere antes a contagem.</summary>
    /// <param name="linhas">Registros.</param>
    private static string? Contagem(int linhas) =>
        linhas switch
        {
            0 => null,
            1 => "1 registro.",
            _ => $"{linhas} registros.",
        };
}
