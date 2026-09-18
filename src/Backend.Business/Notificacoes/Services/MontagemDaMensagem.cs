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
/// Transforma o template gravado na mensagem que sai.
/// </summary>
/// <remarks>
/// Uma pessoa com três parcelas vencidas recebe <b>um</b> texto com as três (decisão 3). O template é
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
    public static string LinkDoExtrato(AplicacaoSettings aplicacao) => $"{aplicacao.UrlDoFrontend.TrimEnd('/')}/minhas-parcelas";

    /// <summary>A fila de conferência no front — o mesmo caminho de <c>ROTAS.conferencia</c>.</summary>
    /// <param name="aplicacao">Identidade da aplicação.</param>
    public static string LinkDaConferencia(AplicacaoSettings aplicacao) => $"{aplicacao.UrlDoFrontend.TrimEnd('/')}/financeiro/conferencia";

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

        var valores = Valores(nome, formatura, FormatosBrasileiros.Reais(total), Dia(vencimento), link, itens.Count);

        var corpo = TemplateDeNotificacao.Renderizar(regra.Template, valores) + Lista(itens);

        return new MensagemDeNotificacao(email, TemplateDeNotificacao.RenderizarTexto(regra.Assunto, valores), corpo, link, "Ver meu extrato");
    }

    /// <summary>O resumo que vai à tesouraria: um texto, um número e o link da fila.</summary>
    /// <param name="regra">Degrau que define o texto.</param>
    /// <param name="email">Endereço de quem recebe.</param>
    /// <param name="formatura">Nome da turma.</param>
    /// <param name="quantidade">O número que a variável <c>{quantidade}</c> recebe.</param>
    /// <param name="link">Destino do botão.</param>
    /// <param name="textoDoLink">Texto do botão.</param>
    public static MensagemDeNotificacao Resumo(RegraResumo regra, string email, string formatura, int quantidade, string link, string textoDoLink)
    {
        var valores = Valores("tesouraria", formatura, string.Empty, string.Empty, link, quantidade);

        return new MensagemDeNotificacao(
            email,
            TemplateDeNotificacao.RenderizarTexto(regra.Assunto, valores),
            TemplateDeNotificacao.Renderizar(regra.Template, valores),
            link,
            textoDoLink
        );
    }

    /// <summary>Uma prévia com dados de exemplo — o que o botão "testar" manda para quem clicou.</summary>
    /// <param name="regra">Degrau a testar.</param>
    /// <param name="nome">Nome de quem clicou.</param>
    /// <param name="email">Endereço de quem clicou.</param>
    /// <param name="formatura">Nome da turma.</param>
    /// <param name="link">Destino do botão.</param>
    public static MensagemDeNotificacao Exemplo(RegraResumo regra, string nome, string email, string formatura, string link)
    {
        var valores = Valores(nome, formatura, FormatosBrasileiros.Reais(35_000), Dia(DateOnly.FromDateTime(DateTime.UtcNow)), link, 3);

        return new MensagemDeNotificacao(
            email,
            $"[Teste] {TemplateDeNotificacao.RenderizarTexto(regra.Assunto, valores)}",
            TemplateDeNotificacao.Renderizar(regra.Template, valores)
                + """<p style="color:#666;font-size:13px;margin-top:24px">Esta é uma prévia com dados de exemplo. A turma não recebeu nada.</p>""",
            link,
            "Ver a régua"
        );
    }

    private static Dictionary<string, string> Valores(string nome, string formatura, string valor, string vencimento, string link, int quantidade) =>
        new(StringComparer.Ordinal)
        {
            ["nome"] = nome,
            ["formatura"] = formatura,
            ["valor"] = valor,
            ["vencimento"] = vencimento,
            ["link"] = link,
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
