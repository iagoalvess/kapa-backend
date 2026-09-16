using System.Globalization;
using Backend.Business.Common.Datas;
using Backend.Business.Common.Pdf;
using Backend.Business.Common.Texto;
using Backend.Business.Relatorios.Models;

namespace Backend.Business.Relatorios.Services;

/// <summary>
/// O balancete em PDF — o documento que a comissão projeta na assembleia.
/// </summary>
/// <remarks>
/// Abre com a mesma <see cref="DocumentoPdf.Capa"/> e fecha com o mesmo rodapé de
/// <see cref="RelatorioEmPdf"/>: os quatro relatórios do produto têm uma cara só, e o que muda daqui
/// para baixo é o miolo — este tem resumo e três quadros, os outros três são uma lista.
/// <para>
/// A cor é a da marca (<c>--brand</c> e companhia, os mesmos tokens do front), em tom lavado no
/// cabeçalho da tabela e em faixa clara nas linhas alternadas. <b>Continua legível impresso em preto
/// e branco</b>: todo o texto é escuro sobre fundo claro — nenhuma informação está na cor.
/// </para>
/// <para>
/// Ordem do documento: capa, resumo, entradas, saídas por categoria, saídas por fornecedor e o
/// fechamento com o saldo. O número vem antes do detalhe, porque a pergunta da assembleia é "quanto
/// sobrou" — a abertura por categoria é a resposta à pergunta seguinte.
/// </para>
/// </remarks>
public static class BalanceteEmPdf
{
    /// <summary>Proporção das colunas de um quadro: rótulo, lançamentos, valor.</summary>
    /// <remarks>São proporções, e não pontos — quem as converte na largura da folha é o documento.</remarks>
    private static readonly (float, bool)[] Colunas = [(56, false), (19, true), (25, true)];

    /// <summary>Proporção das colunas do resumo: movimento e valor. Sem a coluna de contagem — não há o que contar.</summary>
    private static readonly (float, bool)[] ColunasDoResumo = [(75, false), (25, true)];

    /// <summary>Monta o arquivo.</summary>
    /// <param name="balancete">O consolidado do período.</param>
    public static byte[] Gerar(Balancete balancete)
    {
        var emitidoEm = DataUtils.ParaExibicao(balancete.EmitidoEm);

        var pdf = new DocumentoPdf().Capa(
            "Balancete",
            $"{balancete.Formatura} · {balancete.Instituicao}",
            $"Período de {Data(balancete.Periodo.De)} a {Data(balancete.Periodo.Ate)} · emitido por {balancete.EmitidoPor} "
                + $"em {Data(emitidoEm)} às {Hora(emitidoEm)}, horário de Brasília."
        );

        pdf.Secao("Resumo do período")
            .Tabela(
                ColunasDoResumo,
                ["Movimento", "Valor"],
                [
                    ["Entradas", FormatosBrasileiros.Reais(balancete.EntradasEmCentavos)],
                    ["Saídas", FormatosBrasileiros.Reais(balancete.SaidasEmCentavos)],
                    ["Resultado do período", FormatosBrasileiros.Reais(balancete.SaldoDoPeriodoEmCentavos)],
                ]
            )
            .Paragrafo($"Saldo em caixa na data de emissão: {FormatosBrasileiros.Reais(balancete.SaldoAcumuladoEmCentavos)}", negrito: true)
            .Paragrafo(
                "O saldo em caixa é o arrecadado menos o gasto desde o início da turma, e não o resultado do período "
                    + "acima — ele inclui o que entrou e saiu antes desta data inicial."
            );

        Quadro(pdf, "Entradas por tipo de cobrança", balancete.Entradas, balancete.EntradasEmCentavos);
        Quadro(pdf, "Saídas por categoria", balancete.SaidasPorCategoria, balancete.SaidasEmCentavos);
        Quadro(pdf, "Saídas por fornecedor", balancete.SaidasPorFornecedor, balancete.SaidasEmCentavos);

        pdf.Secao("Como ler este documento")
            .Item("Entrada é pagamento de parcela confirmado pela tesouraria, pelo valor que entrou na conta.")
            .Item("Saída é despesa paga, com comprovante. Despesa prevista e não paga não aparece aqui.")
            .Item("Os dois quadros de saída somam o mesmo total, abertos de formas diferentes.")
            .Item("Valores em reais. Nenhum número deste documento identifica um formando.");

        return pdf.Gerar($"Balancete · {balancete.Formatura} · {Data(balancete.Periodo.De)} a {Data(balancete.Periodo.Ate)}");
    }

    /// <summary>Um quadro do balancete, com o total no pé e o aviso de quadro vazio.</summary>
    /// <remarks>
    /// Período sem lançamento ganha uma frase, e não uma tabela com o cabeçalho e nada embaixo: quadro
    /// vazio na parede parece relatório quebrado, e a comissão vai ser perguntada sobre isso.
    /// </remarks>
    /// <param name="pdf">Documento em montagem.</param>
    /// <param name="titulo">Título da seção.</param>
    /// <param name="linhas">Linhas agrupadas.</param>
    /// <param name="total">Soma do lado, para o percentual e o fecho.</param>
    private static void Quadro(DocumentoPdf pdf, string titulo, IReadOnlyList<LinhaDeBalancete> linhas, long total)
    {
        pdf.Secao(titulo);

        if (linhas.Count == 0)
        {
            pdf.Paragrafo("Nenhum lançamento no período.");

            return;
        }

        pdf.Tabela(
                Colunas,
                ["Descrição", "Lançamentos", "Valor"],
                linhas.Select(linha =>
                    new[]
                    {
                        linha.Rotulo,
                        linha.Quantidade.ToString(CultureInfo.InvariantCulture),
                        $"{FormatosBrasileiros.Reais(linha.ValorEmCentavos)}  ({Percentual(linha.ValorEmCentavos, total)})",
                    }
                )
            )
            .Paragrafo($"Total: {FormatosBrasileiros.Reais(linhas.Sum(linha => linha.ValorEmCentavos))}", negrito: true);
    }

    /// <summary>A fatia da linha no total, em uma casa. Total zero não vira divisão por zero.</summary>
    /// <param name="valor">Valor da linha.</param>
    /// <param name="total">Soma do quadro.</param>
    private static string Percentual(long valor, long total) =>
        total == 0 ? "—" : $"{(valor * 100m / total).ToString("0.#", CultureInfo.GetCultureInfo("pt-BR"))}%";

    private static string Data(DateOnly dia) => dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Data(DateTime local) => local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Hora(DateTime local) => local.ToString("HH:mm", CultureInfo.InvariantCulture);
}
