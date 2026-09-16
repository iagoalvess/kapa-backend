using System.Text;
using Backend.Business.Common.Pdf;
using Shouldly;

namespace Backend.UnitTests.Common;

/// <summary>
/// A tabela do <see cref="DocumentoPdf"/>: nada sai do papel, nada é cortado e a página seguinte
/// continua tendo cabeçalho.
/// </summary>
/// <remarks>
/// São as três coisas que estavam quebradas quando a largura da coluna era medida em pontos: as oito
/// colunas de parcelas somavam 587pt numa folha de 483pt úteis, a célula que não cabia virava
/// reticências e a página 2 era uma lista de números sem nome em cima.
/// </remarks>
public sealed class DocumentoPdfTests
{
    /// <summary>Colunas com proporções absurdas de propósito: somam muito mais que qualquer folha.</summary>
    private static readonly (float, bool)[] Colunas = [(900, false), (400, true), (700, false)];

    private static readonly string[] Cabecalho = ["Descrição", "Valor", "Fornecedor"];

    /// <summary>
    /// Proporção que estoura a folha não corta nada: o texto longo aparece inteiro, quebrado em
    /// linhas, sem reticências.
    /// </summary>
    [Theory]
    [InlineData(OrientacaoDaPagina.Retrato)]
    [InlineData(OrientacaoDaPagina.Paisagem)]
    public void Coluna_estreita_quebra_a_celula_em_linhas_em_vez_de_cortar(OrientacaoDaPagina orientacao)
    {
        // Arrange
        var longo = "Entrada do buffet completo para 180 convidados com servico de garcom e bebidas";

        // Act
        var pdf = new DocumentoPdf(orientacao)
            .Tabela(
                Colunas,
                Cabecalho,
                [
                    [longo, "R$ 1.234,56", "Buffet Sabor e Arte Eventos"],
                ]
            )
            .Gerar("prova");

        // Assert
        var texto = Texto(pdf);
        texto.ShouldNotContain("\\205", Case.Sensitive);

        foreach (var palavra in longo.Split(' '))
            texto.ShouldContain(palavra);
    }

    /// <summary>Coluna nenhuma fica mais estreita que o próprio título — era o "Competênci" com um "a" sozinho embaixo.</summary>
    [Fact]
    public void Cabecalho_nao_quebra_por_falta_de_largura()
    {
        // Arrange
        (float, bool)[] apertadas = [(95, false), (1, false), (1, false)];

        // Act
        var pdf = new DocumentoPdf()
            .Tabela(
                apertadas,
                ["Descrição", "Competência", "Fornecedor"],
                [
                    ["a", "b", "c"],
                ]
            )
            .Gerar("prova");

        // Assert
        Texto(pdf).ShouldContain("Compet\\352ncia");
    }

    /// <summary>
    /// A tabela que passa da página redesenha o cabeçalho no alto da folha seguinte.
    /// </summary>
    /// <remarks>
    /// O documento tem duas páginas e o título "Fornecedor" aparece duas vezes: uma por cabeçalho.
    /// Nenhuma linha traz esse texto, então toda ocorrência é de um cabeçalho.
    /// </remarks>
    [Fact]
    public void Tabela_longa_repete_o_cabecalho_na_pagina_seguinte()
    {
        // Arrange
        var linhas = Enumerable.Range(0, 120).Select(i => new[] { $"Linha {i}", "R$ 1,00", "Casa" });

        // Act
        var texto = Texto(new DocumentoPdf().Tabela(Colunas, Cabecalho, linhas).Gerar("prova"));

        // Assert
        var paginas = int.Parse(texto.Split("/Count ")[1].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
        paginas.ShouldBeGreaterThan(1);
        Ocorrencias(texto, "Fornecedor").ShouldBe(paginas);
    }

    /// <summary>Tabela nenhuma escreve fora das margens, seja qual for a proporção pedida.</summary>
    [Fact]
    public void Nenhum_texto_e_escrito_alem_da_margem_direita()
    {
        // Arrange
        const float MargemDireitaDoRetrato = 595 - 42;

        // Act
        var texto = Texto(
            new DocumentoPdf()
                .Tabela(
                    Colunas,
                    Cabecalho,
                    [
                        ["Um", "R$ 12.345.678,90", "Outro"],
                    ]
                )
                .Gerar("prova")
        );

        // Assert
        foreach (var x in PosicoesDeTexto(texto))
            x.ShouldBeLessThanOrEqualTo(MargemDireitaDoRetrato);
    }

    /// <summary>O X de cada bloco de texto do fluxo, que é o primeiro número da matriz do <c>Tm</c>.</summary>
    /// <param name="texto">Conteúdo do PDF.</param>
    private static IEnumerable<float> PosicoesDeTexto(string texto) =>
        texto.Split("1 0 0 1 ").Skip(1).Select(trecho => float.Parse(trecho.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture));

    private static int Ocorrencias(string texto, string trecho) => texto.Split(trecho).Length - 1;

    /// <summary>O arquivo como texto, para procurar o que o documento diz.</summary>
    /// <param name="bytes">PDF gerado.</param>
    private static string Texto(byte[] bytes) => Encoding.Latin1.GetString(bytes);
}
