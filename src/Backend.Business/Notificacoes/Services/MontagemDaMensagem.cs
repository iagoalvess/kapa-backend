using System.Globalization;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common;
using Backend.Business.Common.Texto;
using Backend.Business.Emails.Services;
using Backend.Business.Notificacoes.Models;

namespace Backend.Business.Notificacoes.Services;

/// <summary>
/// Uma parcela e quanto ela vale hoje — o que entra na mensagem.
/// </summary>
/// <param name="Parcela">A parcela alcançada pelo degrau.</param>
/// <param name="Valor">O valor do dia, pelas regras do snapshot da adesão do dono.</param>
public sealed record ParcelaNaMensagem(ParcelaParaCobranca Parcela, ValorDoDia Valor);

/// <summary>
/// Transforma o texto do degrau na mensagem que sai.
/// </summary>
/// <remarks>
/// Uma pessoa com três parcelas vencidas recebe <b>um</b> texto com as três (decisão 3). O texto é
/// o começo da mensagem; a lista das parcelas é montada aqui e vai abaixo dele, porque
/// <c>{vencimento}</c> e <c>{valor}</c> não sabem falar no plural.
/// <para>
/// Nesse caso <c>{valor}</c> é a soma do valor de hoje e <c>{vencimento}</c> é o vencimento mais
/// antigo: é o que a pessoa precisa saber para pagar, e é o que a lista detalha logo em seguida.
/// </para>
/// </remarks>
public static class MontagemDaMensagem
{
    /// <summary>O extrato no front — o mesmo caminho de <c>ROTAS.extrato</c>.</summary>
    /// <param name="aplicacao">Identidade da aplicação.</param>
    public static string LinkDoExtrato(AplicacaoSettings aplicacao) => aplicacao.Link(RotasDoFront.MinhasParcelas);

    /// <summary>A fila de conferência no front — o mesmo caminho de <c>ROTAS.conferencia</c>.</summary>
    /// <param name="aplicacao">Identidade da aplicação.</param>
    public static string LinkDaConferencia(AplicacaoSettings aplicacao) => aplicacao.Link(RotasDoFront.Conferencia);

    /// <summary>A mensagem de cobrança de uma pessoa, com todas as parcelas que o dia alcançou.</summary>
    /// <param name="regra">Degrau que define o texto.</param>
    /// <param name="nome">Nome de quem recebe.</param>
    /// <param name="email">Endereço de quem recebe.</param>
    /// <param name="formatura">Nome da turma.</param>
    /// <param name="itens">Parcelas e o valor de hoje de cada uma.</param>
    /// <param name="link">Destino do botão.</param>
    public static MensagemDeNotificacao Cobranca(
        RegraResumo regra,
        string nome,
        string email,
        string formatura,
        IReadOnlyList<ParcelaNaMensagem> itens,
        string link
    )
    {
        var total = itens.Sum(item => item.Valor.TotalEmCentavos);
        var vencimento = itens.Min(item => item.Parcela.Vencimento);

        var valores = Valores(nome, formatura, FormatosBrasileiros.Reais(total), Dia(vencimento), itens.Count);

        var corpo = TemplateDeNotificacao.Renderizar(regra.Degrau.Texto.Corpo, valores) + Lista(itens);

        return new MensagemDeNotificacao(email, regra.Degrau.Texto.Assunto, corpo, link, "Ver meu extrato");
    }

    /// <summary>O resumo que vai à tesouraria: um texto, um número e o link da fila.</summary>
    /// <param name="texto">Assunto e corpo do resumo.</param>
    /// <param name="email">Endereço de quem recebe.</param>
    /// <param name="formatura">Nome da turma.</param>
    /// <param name="quantidade">O número que a variável <c>{quantidade}</c> recebe.</param>
    /// <param name="link">Destino do botão.</param>
    /// <param name="textoDoLink">Texto do botão.</param>
    public static MensagemDeNotificacao Resumo(
        TextoDaMensagem texto,
        string email,
        string formatura,
        int quantidade,
        string link,
        string textoDoLink
    ) =>
        new(
            email,
            texto.Assunto,
            TemplateDeNotificacao.Renderizar(texto.Corpo, Valores("tesouraria", formatura, string.Empty, string.Empty, quantidade)),
            link,
            textoDoLink
        );

    private static Dictionary<string, string> Valores(string nome, string formatura, string valor, string vencimento, int quantidade) =>
        new(StringComparer.Ordinal)
        {
            ["nome"] = nome,
            ["formatura"] = formatura,
            ["valor"] = valor,
            ["vencimento"] = vencimento,
            ["quantidade"] = quantidade.ToString(CultureInfo.InvariantCulture),
        };

    /// <summary>A lista das parcelas, abaixo do texto. Com uma só, o texto já disse tudo.</summary>
    private static string Lista(IReadOnlyList<ParcelaNaMensagem> itens)
    {
        if (itens.Count < 2)
            return string.Empty;

        var linhas = itens
            .OrderBy(item => item.Parcela.Vencimento)
            .Select(item =>
                $"<li>{ModeloDeEmail.Texto(item.Parcela.Descricao ?? "Parcela")} — venceu em {Dia(item.Parcela.Vencimento)}: "
                + $"<strong>{FormatosBrasileiros.Reais(item.Valor.TotalEmCentavos)}</strong></li>"
            );

        return $"""<ul style="line-height:1.8;margin:16px 0 0;padding-left:20px">{string.Concat(linhas)}</ul>""";
    }

    private static string Dia(DateOnly dia) => dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
