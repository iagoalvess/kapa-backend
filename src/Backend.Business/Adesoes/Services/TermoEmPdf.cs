using System.Globalization;
using System.Text.RegularExpressions;
using Backend.Business.Adesoes.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Common.Pdf;
using Backend.Business.Common.Texto;

namespace Backend.Business.Adesoes.Services;

/// <summary>
/// O termo assinado em PDF, remontado a cada pedido a partir do que a adesão gravou.
/// </summary>
/// <remarks>
/// Gerado sob demanda, e não guardado: 80 PDFs por turma que ninguém abre ainda precisariam de
/// backup. Tudo o que entra aqui é imutável — o texto da versão, o plano congelado, os dados do
/// aceite — e o <see cref="DocumentoPdf"/> é determinístico, então o arquivo de hoje e o de daqui a um
/// ano são o mesmo. <c>ponytail:</c> se um dia o PDF precisar ser imutável por exigência jurídica,
/// guarde o hash do PDF gerado — não o arquivo.
/// <para>
/// Ordem do documento: o resumo financeiro vem antes do termo, como na tela — termo de seis páginas
/// antes do número é como se esconde o número.
/// </para>
/// </remarks>
public static partial class TermoEmPdf
{
    /// <summary>Proporção das colunas de itens: nome, total, parcelas, primeiro vencimento, dia.</summary>
    /// <remarks>São proporções, e não pontos — quem as converte na largura da folha é o documento.</remarks>
    private static readonly (float, bool)[] ColunasDosItens = [(36, false), (19, true), (14, true), (18, true), (13, true)];

    /// <summary>Proporção das colunas da grade: posição, item, vencimento, valor.</summary>
    private static readonly (float, bool)[] ColunasDaGrade = [(14, false), (37, false), (22, true), (27, true)];

    /// <summary>Monta o PDF.</summary>
    /// <remarks>
    /// A linha do código de confirmação só sai quando a adesão tem o e-mail gravado: nas adesões
    /// anteriores ao código ela some, em vez de mentir com um traço.
    /// </remarks>
    /// <param name="adesao">Adesão com o texto da versão aceita.</param>
    /// <param name="mascararCpf">Verdadeiro para quem não é o titular: a comissão vê o CPF mascarado.</param>
    public static byte[] Gerar(AdesaoComTermo adesao, bool mascararCpf)
    {
        var registro = adesao.Adesao;
        var plano = registro.LerPlano();
        var aceitoEm = DataUtils.ParaExibicao(registro.AceitoEm);

        var pdf = new DocumentoPdf().Capa(
            "Termo de adesão",
            $"Versão {registro.Versao} do termo da turma, aceita por {registro.NomeCompleto} em {Data(aceitoEm)} às {Hora(aceitoEm)}."
        );

        ResumoFinanceiro(pdf, plano);
        Markdown(pdf.Secao("Termo"), adesao.ConteudoDoTermo);

        pdf.Secao("Registro do aceite")
            .Paragrafo($"Nome completo: {registro.NomeCompleto}")
            .Paragrafo($"CPF: {(mascararCpf ? FormatosBrasileiros.MascararCpf(registro.Cpf) : FormatosBrasileiros.FormatarCpf(registro.Cpf))}")
            .Paragrafo(
                $"Aceito em: {Data(aceitoEm)} às {Hora(aceitoEm)} (horário de Brasília) — {registro.AceitoEm.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}"
            )
            .Paragrafo($"Endereço IP: {registro.EnderecoIp}");

        if (registro.EmailDoAceite.Length > 0)
            pdf.Paragrafo($"Confirmado por código enviado a: {registro.EmailDoAceite}");

        pdf.Paragrafo($"Navegador: {registro.UserAgent}", discreto: true)
            .Paragrafo($"SHA-256 do termo e do plano aceitos: {registro.HashDoConteudo}", discreto: true);

        return pdf.Gerar($"Termo de adesão · versão {registro.Versao} · adesão {registro.Id}");
    }

    private static void ResumoFinanceiro(DocumentoPdf pdf, SnapshotDoPlano plano)
    {
        pdf.Secao("O que foi aceito")
            .Tabela(
                ColunasDosItens,
                ["Item", "Total", "Parcelas", "1º vencimento", "Dia"],
                plano.Itens.Select(item =>
                {
                    var parcelas = plano.ParcelasDo(item);
                    var primeira = parcelas[0];

                    return new[]
                    {
                        RotuloDoItem.De(item.Tipo, item.Descricao),
                        FormatosBrasileiros.Reais(item.ValorEmCentavos),
                        $"{parcelas.Count}×",
                        Data(primeira.Vencimento),
                        item.DiaDeVencimento.ToString(CultureInfo.InvariantCulture),
                    };
                })
            )
            .Paragrafo($"Total: {FormatosBrasileiros.Reais(plano.TotalEmCentavos)} em {plano.Parcelas.Count} parcelas.", negrito: true)
            .Paragrafo(plano.RegrasDeAtrasoPorExtenso());

        pdf.Secao("Parcelas")
            .Tabela(
                ColunasDaGrade,
                ["Parcela", "Item", "Vencimento", "Valor"],
                plano.Parcelas.Select(parcela =>
                    new[]
                    {
                        $"{parcela.Numero}/{parcela.De}",
                        RotuloDoItem.De(parcela.Tipo, parcela.Descricao),
                        Data(parcela.Vencimento),
                        FormatosBrasileiros.Reais(parcela.ValorEmCentavos),
                    }
                )
            );
    }

    /// <summary>
    /// O markdown do termo em blocos do PDF: títulos, itens e parágrafos. Negrito, itálico e código
    /// viram texto simples; link vira "texto (endereço)"; linha de tabela vira uma linha de texto.
    /// Linhas seguidas de texto formam um parágrafo, como no markdown.
    /// </summary>
    /// <param name="pdf">Documento em montagem.</param>
    /// <param name="markdown">Texto da versão.</param>
    internal static void Markdown(DocumentoPdf pdf, string markdown)
    {
        var paragrafo = new List<string>();

        void Fechar()
        {
            if (paragrafo.Count > 0)
                pdf.Paragrafo(string.Join(' ', paragrafo));

            paragrafo.Clear();
        }

        foreach (var bruta in markdown.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var linha = bruta.Trim();

            if (Titulo().Match(linha) is { Success: true } titulo)
            {
                Fechar();
                pdf.Secao(Limpar(titulo.Groups[1].Value));
            }
            else if (ItemDeLista().Match(linha) is { Success: true } item)
            {
                Fechar();
                pdf.Item(Limpar(item.Groups[1].Value));
            }
            else if (ItemNumerado().IsMatch(linha))
            {
                Fechar();
                pdf.Paragrafo(Limpar(linha));
            }
            else if (linha.StartsWith('|'))
            {
                Fechar();

                if (!SeparadorDeTabela().IsMatch(linha))
                    pdf.Paragrafo(string.Join("  ·  ", linha.Trim('|').Split('|').Select(celula => Limpar(celula.Trim()))));
            }
            else if (linha.Length == 0 || linha is "---" or "***")
            {
                Fechar();
            }
            else
            {
                paragrafo.Add(Limpar(linha.TrimStart('>', ' ')));
            }
        }

        Fechar();
    }

    private static string Limpar(string texto) =>
        Enfase().Replace(Link().Replace(texto, match => $"{match.Groups[1].Value} ({match.Groups[2].Value})"), string.Empty);

    private static string Data(DateTime local) => local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Data(DateOnly dia) => dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Hora(DateTime local) => local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^#{1,6}\s+(.+)$")]
    private static partial Regex Titulo();

    [GeneratedRegex(@"^[-*+]\s+(.+)$")]
    private static partial Regex ItemDeLista();

    /// <summary>Item numerado mantém o número — em contrato, "3." é a cláusula três.</summary>
    [GeneratedRegex(@"^\d+[.)]\s+")]
    private static partial Regex ItemNumerado();

    [GeneratedRegex(@"^\|?[\s:|-]+\|?$")]
    private static partial Regex SeparadorDeTabela();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"\*\*|__|`|(?<![\w*])\*(?=\S)|(?<=\S)\*(?![\w*])")]
    private static partial Regex Enfase();
}
