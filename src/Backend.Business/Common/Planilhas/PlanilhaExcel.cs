using Backend.Business.Relatorios.Models;
using ClosedXML.Excel;

namespace Backend.Business.Common.Planilhas;

/// <summary>
/// Uma <see cref="TabelaDoRelatorio"/> escrita como planilha do Excel (.xlsx).
/// </summary>
/// <remarks>
/// Substituiu o CSV: o arquivo chega com número que soma, data que ordena e coluna na largura
/// certa, sem depender de o Excel adivinhar separador e codificação — que era a origem do BOM e do
/// ponto e vírgula do exportador antigo.
/// <para>
/// As cores são os mesmos tokens da marca do PDF e do front (<c>--brand-wash</c>, <c>--brand-text</c>),
/// para a planilha e o documento impresso não parecerem de dois produtos.
/// </para>
/// <para>
/// ClosedXML, e não OOXML à mão: o formato é um pacote ZIP com quatro XMLs que se referenciam por
/// índice, e escrever isso à mão é onde se perde uma semana. É a única dependência de arquivo do
/// projeto.
/// </para>
/// </remarks>
public static class PlanilhaExcel
{
    /// <summary>Linha em que o cabeçalho da tabela fica — acima dele, título e subtítulo.</summary>
    private const int LinhaDoCabecalho = 4;

    /// <summary>Caracteres que o Excel recusa no nome de uma aba.</summary>
    private static readonly char[] ProibidosNaAba = ['[', ']', ':', '*', '?', '/', '\\'];

    /// <summary>Laranja escuro da marca (<c>--brand-text</c>) — título e cabeçalho, como no PDF.</summary>
    private static readonly XLColor MarcaEscrita = XLColor.FromArgb(0xA6, 0x54, 0x0A);

    /// <summary>Laranja lavado da marca (<c>--brand-wash</c>) — fundo do cabeçalho.</summary>
    private static readonly XLColor MarcaLavada = XLColor.FromArgb(0xFD, 0xF4, 0xEA);

    /// <summary>Laranja suave da marca (<c>--brand-soft</c>) — o fio sob o cabeçalho.</summary>
    private static readonly XLColor MarcaSuave = XLColor.FromArgb(0xF6, 0xC8, 0x8E);

    /// <summary>Cinza de apoio (<c>--text-secondary</c>) — o subtítulo.</summary>
    private static readonly XLColor Apoio = XLColor.FromArgb(0x6B, 0x6B, 0x66);

    /// <summary>Monta o arquivo.</summary>
    /// <param name="tabela">O relatório já reduzido a colunas e linhas.</param>
    public static byte[] Gerar(TabelaDoRelatorio tabela)
    {
        using var livro = new XLWorkbook();
        var planilha = livro.AddWorksheet(Aba(tabela.Titulo));

        planilha.Cell(1, 1).Value = tabela.Titulo;
        planilha.Cell(1, 1).Style.Font.Bold = true;
        planilha.Cell(1, 1).Style.Font.FontSize = 14;
        planilha.Cell(1, 1).Style.Font.FontColor = MarcaEscrita;
        planilha.Cell(2, 1).Value = tabela.Subtitulo;
        planilha.Cell(2, 1).Style.Font.FontColor = Apoio;

        for (var coluna = 0; coluna < tabela.Colunas.Count; coluna++)
        {
            var celula = planilha.Cell(LinhaDoCabecalho, coluna + 1);
            celula.Value = tabela.Colunas[coluna].Nome;
            celula.Style.Font.Bold = true;
            celula.Style.Font.FontColor = MarcaEscrita;
            celula.Style.Fill.BackgroundColor = MarcaLavada;
            celula.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            celula.Style.Border.BottomBorderColor = MarcaSuave;
        }

        for (var linha = 0; linha < tabela.Linhas.Count; linha++)
        for (var coluna = 0; coluna < tabela.Colunas.Count; coluna++)
            Escrever(planilha.Cell(LinhaDoCabecalho + 1 + linha, coluna + 1), tabela.Linhas[linha][coluna]);

        // O cabeçalho fica preso no topo e vira filtro: quem abre uma planilha de parcelas vai
        // filtrar por situação antes de olhar qualquer número.
        planilha.SheetView.FreezeRows(LinhaDoCabecalho);
        if (tabela.Linhas.Count > 0)
            planilha.Range(LinhaDoCabecalho, 1, LinhaDoCabecalho + tabela.Linhas.Count, tabela.Colunas.Count).SetAutoFilter();

        planilha.Columns().AdjustToContents(LinhaDoCabecalho, LinhaDoCabecalho + tabela.Linhas.Count, 10, 60);

        using var memoria = new MemoryStream();
        livro.SaveAs(memoria);

        return memoria.ToArray();
    }

    /// <summary>Escreve a célula no tipo dela, com o formato que o Excel entende.</summary>
    /// <param name="destino">Célula da planilha.</param>
    /// <param name="celula">Valor do relatório.</param>
    private static void Escrever(IXLCell destino, Celula celula)
    {
        if (celula.Dia is { } dia)
        {
            destino.Value = dia.ToDateTime(TimeOnly.MinValue);
            destino.Style.DateFormat.Format = "dd/mm/yyyy";

            return;
        }

        if (celula.Numero is { } numero)
        {
            destino.Value = numero;
            destino.Style.NumberFormat.Format = celula.Dinheiro ? "R$ #,##0.00" : "0";

            return;
        }

        destino.Value = celula.Texto;
    }

    /// <summary>O nome da aba dentro do limite do Excel: 31 caracteres e sem os símbolos proibidos.</summary>
    /// <param name="titulo">Título do relatório.</param>
    private static string Aba(string titulo)
    {
        var limpo = new string([.. titulo.Where(caractere => !ProibidosNaAba.Contains(caractere))]).Trim();

        return limpo.Length <= 31 ? limpo : limpo[..31];
    }
}
