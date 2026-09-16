using System.Globalization;
using Backend.Business.Common.Texto;

namespace Backend.Business.Relatorios.Models;

/// <summary>
/// Uma célula de relatório, com o texto já pronto e, quando é número ou data, o valor cru ao lado.
/// </summary>
/// <remarks>
/// O texto serve ao PDF, que é papel; o valor cru serve ao Excel, para a coluna somar, ordenar e
/// filtrar como número de verdade. Dinheiro escrito como texto numa planilha é o que faz o contador
/// redigitar o arquivo inteiro.
/// </remarks>
/// <param name="Texto">Como a célula aparece no papel.</param>
/// <param name="Numero">O valor, quando é numérico — em reais, não em centavos.</param>
/// <param name="Dia">A data, quando é data.</param>
/// <param name="Dinheiro">Se o número é dinheiro, para a planilha aplicar o formato de moeda.</param>
public readonly record struct Celula(string Texto, decimal? Numero = null, DateOnly? Dia = null, bool Dinheiro = false)
{
    /// <summary>Célula vazia — o campo que não se aplica àquela linha.</summary>
    public static readonly Celula Vazia = new(string.Empty);

    /// <summary>Texto puro.</summary>
    /// <param name="texto">Conteúdo; nulo vira vazio.</param>
    public static Celula De(string? texto) => new(texto ?? string.Empty);

    /// <summary>Dinheiro, dos centavos do contrato para os reais da planilha.</summary>
    /// <param name="centavos">Valor em centavos.</param>
    public static Celula Reais(long centavos) => new(FormatosBrasileiros.Reais(centavos), centavos / 100m, Dinheiro: true);

    /// <summary>Dinheiro que pode não existir — a parcela ainda não paga.</summary>
    /// <param name="centavos">Valor em centavos, ou nulo.</param>
    public static Celula Reais(long? centavos) => centavos is { } valor ? Reais(valor) : Vazia;

    /// <summary>Uma contagem.</summary>
    /// <param name="valor">Quantidade.</param>
    public static Celula Inteiro(int valor) => new(valor.ToString(CultureInfo.InvariantCulture), valor);

    /// <summary>Um dia, no formato brasileiro no papel e como data na planilha.</summary>
    /// <param name="dia">Data, ou nulo.</param>
    public static Celula Data(DateOnly? dia) =>
        dia is { } valor ? new(valor.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), Dia: valor) : Vazia;
}

/// <summary>
/// Uma coluna do relatório.
/// </summary>
/// <remarks>
/// A largura é uma <b>proporção</b>, não uma medida: o PDF a redistribui na largura útil da folha, de
/// modo que a tabela caiba sempre — em pé, deitada, com quatro colunas ou com oito. Enquanto ela era
/// medida em pontos, as oito colunas de parcelas somavam 587pt numa folha de 483pt úteis, e a última
/// saía do papel. A planilha ignora o campo e ajusta pelo conteúdo.
/// </remarks>
/// <param name="Nome">Título da coluna.</param>
/// <param name="Largura">Peso da coluna em relação às irmãs — "a descrição é o dobro da data".</param>
/// <param name="Direita">Se o conteúdo encosta à direita — valores e datas.</param>
public sealed record ColunaDoRelatorio(string Nome, float Largura, bool Direita = false);

/// <summary>
/// Um relatório reduzido a título, colunas e linhas — a forma de que o PDF e o Excel saem.
/// </summary>
/// <remarks>
/// É o que impede quatro relatórios em dois formatos de virarem oito implementações: cada relatório
/// monta a sua tabela uma vez, e cada formato sabe escrever <b>uma</b> tabela.
/// <para>
/// As linhas vêm materializadas, e não como <see cref="IAsyncEnumerable{T}"/>: nenhum dos dois
/// formatos escreve em streaming — o PDF precisa paginar e o Excel monta o pacote inteiro em
/// memória. <c>ponytail:</c> o teto é o tamanho de uma turma, centenas de linhas; se um dia um
/// relatório passar de dezenas de milhares, o caminho é escrever o XLSX em streaming (SAX), e aí
/// esta lista vira de novo um fluxo.
/// </para>
/// </remarks>
/// <param name="Titulo">Nome do relatório, no topo do arquivo.</param>
/// <param name="Subtitulo">A linha embaixo dele — turma e período.</param>
/// <param name="Colunas">Colunas, na ordem.</param>
/// <param name="Linhas">Registros, cada um do tamanho das colunas.</param>
public sealed record TabelaDoRelatorio(
    string Titulo,
    string Subtitulo,
    IReadOnlyList<ColunaDoRelatorio> Colunas,
    IReadOnlyList<IReadOnlyList<Celula>> Linhas
);
