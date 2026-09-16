using System.Text;
using Backend.Business.Relatorios.Models;
using Backend.Business.Relatorios.Services;
using Shouldly;

namespace Backend.UnitTests.Relatorios;

/// <summary>
/// O balancete em PDF: o que a assembleia precisa ler está lá, o arquivo é válido e a mesma entrada
/// produz os mesmos bytes.
/// </summary>
public sealed class BalanceteEmPdfTests
{
    private static readonly PeriodoDoRelatorio Periodo = new(new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 15));

    [Fact]
    public void O_arquivo_e_um_pdf_valido()
    {
        // Act
        var bytes = BalanceteEmPdf.Gerar(Balancete());

        // Assert
        Encoding.ASCII.GetString(bytes, 0, 5).ShouldBe("%PDF-");
        Texto(bytes).ShouldContain("%%EOF");
    }

    /// <summary>
    /// A capa, o resumo e os três quadros — é o que a decisão 3 chama de "não é dump de tabela".
    /// </summary>
    /// <remarks>
    /// O texto é conferido no fluxo de conteúdo do PDF, onde cada bloco sai como literal entre
    /// parênteses. Acento vira octal no WinAnsi, então o teste procura trechos sem acento.
    /// </remarks>
    [Theory]
    [InlineData("Balancete")]
    [InlineData("Medicina 2027")]
    [InlineData("Resumo do per")]
    [InlineData("Entradas por tipo de cobran")]
    [InlineData("das por categoria")]
    [InlineData("das por fornecedor")]
    [InlineData("Como ler este documento")]
    [InlineData("Rafael Costa Lima")]
    public void O_documento_tem_capa_resumo_e_os_tres_quadros(string trecho) => Texto(BalanceteEmPdf.Gerar(Balancete())).ShouldContain(trecho);

    /// <summary>
    /// Período sem lançamento ganha uma frase, não uma tabela vazia.
    /// </summary>
    /// <remarks>Quadro com cabeçalho e nada embaixo, projetado na parede, parece relatório quebrado.</remarks>
    [Fact]
    public void Periodo_sem_lancamento_avisa_em_vez_de_desenhar_tabela_vazia() =>
        Texto(BalanceteEmPdf.Gerar(Balancete() with { Entradas = [], SaidasPorCategoria = [], SaidasPorFornecedor = [] }))
            .ShouldContain("Nenhum lan");

    /// <summary>O mesmo balancete gera os mesmos bytes: o <c>DocumentoPdf</c> não carimba data de criação.</summary>
    [Fact]
    public void A_mesma_entrada_produz_os_mesmos_bytes() => BalanceteEmPdf.Gerar(Balancete()).ShouldBe(BalanceteEmPdf.Gerar(Balancete()));

    /// <summary>Um balancete com os três quadros preenchidos.</summary>
    private static Balancete Balancete() =>
        new(
            "Medicina 2027",
            "Medicina — UFPR",
            Periodo,
            "Rafael Costa Lima",
            new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc),
            [new LinhaDeBalancete("Mensalidade", 42, 84_000_00L), new LinhaDeBalancete("Adesão", 7, 7_000_00L)],
            [new LinhaDeBalancete("Buffet", 3, 26_000_00L), new LinhaDeBalancete("Espaço", 1, 9_000_00L)],
            [new LinhaDeBalancete("Buffet Sabor", 3, 26_000_00L), new LinhaDeBalancete("Sem fornecedor", 1, 9_000_00L)],
            56_000_00L,
            [new MesDoBalancete(new DateOnly(2026, 1, 1), 91_000_00L, 35_000_00L)],
            new TotaisDoPeriodo(70_000_00L, 30_000_00L)
        );

    /// <summary>O arquivo como texto, para procurar o que o documento diz.</summary>
    /// <param name="bytes">PDF gerado.</param>
    private static string Texto(byte[] bytes) => Encoding.Latin1.GetString(bytes);
}
