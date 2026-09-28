using System.Globalization;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Common.Pdf;
using Backend.Business.Common.Texto;
using Backend.Business.Emails.Services;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Models;

namespace Backend.Business.Pagamentos.Services;

/// <summary>
/// O recibo de um recebimento em PDF, remontado a cada pedido a partir do que a baixa gravou (Sprint 22).
/// </summary>
/// <remarks>
/// Uma projeção, e não um documento guardado: nem arquivo, nem tabela, nem numeração própria — o
/// número do recibo é o id do recebimento. Tudo o que entra aqui é fato gravado (o recebimento, a
/// parcela, os meios da conta no instante da baixa), e o <see cref="DocumentoPdf"/> é determinístico:
/// o arquivo de hoje e o de daqui a um ano são o mesmo.
/// <para>
/// Não entra o que muda com o dia: saldo devedor, o que falta pagar, multa e juros recalculados
/// (decisão 4). Um documento que muda conforme o dia em que é baixado não prova nada.
/// </para>
/// </remarks>
public static class ReciboEmPdf
{
    /// <summary>Proporção das colunas do quadro de valores: o que é, e quanto.</summary>
    private static readonly (float, bool)[] ColunasDosValores = [(70, false), (30, true)];

    /// <summary>Monta o PDF.</summary>
    /// <param name="recibo">O recebimento e o que ele carrega.</param>
    /// <param name="meios">Os meios da conta no instante da baixa; nulo, o recibo não nomeia titular.</param>
    /// <param name="mascararCpf">Verdadeiro para a gestão: só o próprio formando vê o CPF inteiro.</param>
    public static byte[] Gerar(DadosDoRecibo recibo, MeiosDaConta? meios, bool mascararCpf)
    {
        var parcela = recibo.Parcela;
        var confirmadoEm = DataUtils.ParaExibicao(recibo.BaixadoEm);

        var pdf = new DocumentoPdf()
            .Capa("Recibo de pagamento", $"{recibo.Turma} · {recibo.Instituicao}", $"Recibo nº {recibo.RecebimentoId}", Mascote.Cofrinho)
            .Destaque(FormatosBrasileiros.Reais(recibo.ValorEmCentavos), corpo: 30, centralizado: true)
            .Paragrafo($"recebido em {Data(recibo.PagoEm)}", discreto: true, centralizado: true);

        pdf.Secao("Recebemos de").Paragrafo(parcela.Nome, negrito: true);

        if (recibo.Cpf is { } cpf)
            pdf.Paragrafo($"CPF: {(mascararCpf ? FormatosBrasileiros.MascararCpf(cpf) : FormatosBrasileiros.FormatarCpf(cpf))}");

        pdf.Secao("Referente a")
            .Paragrafo(
                $"{RotuloDoItem.De(parcela.Tipo, parcela.Descricao)} — parcela {parcela.Numero}/{parcela.De}, "
                    + $"competência {parcela.Vencimento.ToString("MM/yyyy", CultureInfo.InvariantCulture)}"
            );

        pdf.Secao("Pagamento").Tabela(ColunasDosValores, ["Descrição", "Valor"], Valores(recibo));

        if (Divergencia(recibo) is { } divergencia)
            pdf.Paragrafo(divergencia);

        pdf.Paragrafo($"Forma: {FormasDePagamento.Rotulo(recibo.Forma)} · Pago em {Data(recibo.PagoEm)}")
            .Paragrafo($"Recebido por: {QuemRecebeu(recibo, meios)}");

        pdf.Secao("Confirmação")
            .Paragrafo(
                $"Confirmado por {recibo.BaixadoPor} em {Data(DateOnly.FromDateTime(confirmadoEm))} às "
                    + $"{confirmadoEm.ToString("HH:mm", CultureInfo.InvariantCulture)} (horário de Brasília)."
            )
            .Espaco(12)
            .Paragrafo(
                $"Este recibo não é documento fiscal. Quem recebeu o pagamento foi a turma {recibo.Turma}, na conta "
                    + "indicada pela comissão de formatura; o Kapa apenas registra o recebimento.",
                negrito: true
            );

        return pdf.Gerar($"Recibo de pagamento · recebimento {recibo.RecebimentoId}");
    }

    /// <summary>O quadro de valores: o devido do dia, o recebido e, se houver, a diferença.</summary>
    private static IEnumerable<string[]> Valores(DadosDoRecibo recibo)
    {
        yield return [$"Valor devido em {Data(recibo.PagoEm)}", FormatosBrasileiros.Reais(recibo.DevidoEmCentavos)];
        yield return ["Valor recebido", FormatosBrasileiros.Reais(recibo.ValorEmCentavos)];

        if (recibo.ValorEmCentavos != recibo.DevidoEmCentavos)
            yield return ["Diferença", FormatosBrasileiros.Reais(recibo.ValorEmCentavos - recibo.DevidoEmCentavos)];
    }

    /// <summary>
    /// A linha que explica a diferença (decisão 5): recibo que esconde a diferença é o documento que
    /// produz a discussão de dois meses depois.
    /// </summary>
    /// <remarks>Diz o fato do dia do pagamento, e não o que falta hoje — isso muda, e o recibo não.</remarks>
    private static string? Divergencia(DadosDoRecibo recibo)
    {
        var diferenca = recibo.ValorEmCentavos - recibo.DevidoEmCentavos;

        return diferenca switch
        {
            < 0 => $"O valor recebido foi {FormatosBrasileiros.Reais(-diferenca)} menor que o devido no dia do pagamento.",
            > 0 => $"O valor recebido foi {FormatosBrasileiros.Reais(diferenca)} maior que o devido no dia do pagamento.",
            _ => null,
        };
    }

    /// <summary>
    /// O titular do meio por onde o dinheiro entrou, como a conta estava no instante da baixa.
    /// </summary>
    /// <remarks>
    /// Sem o meio correspondente na conta — baixa em dinheiro numa turma que não anuncia dinheiro, ou a
    /// forma "Outro" — o recibo diz que foi a turma, que é o que se sabe.
    /// </remarks>
    private static string QuemRecebeu(DadosDoRecibo recibo, MeiosDaConta? meios) =>
        recibo.Forma switch
        {
            FormaDePagamento.Pix when meios?.Pix is { } pix => ChavePix.DocumentoDoTitular(pix.TipoDeChave, pix.Chave) is { } documento
                ? $"{pix.NomeDoTitular} · {documento}"
                : pix.NomeDoTitular,
            FormaDePagamento.Transferencia when meios?.Transferencia is { } conta => $"{conta.Titular} · {conta.Banco}",
            FormaDePagamento.Dinheiro when meios?.Dinheiro is { } dinheiro => $"{dinheiro.Nome}, em mãos",
            _ => $"a turma {recibo.Turma}",
        };

    private static string Data(DateOnly dia) => dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
