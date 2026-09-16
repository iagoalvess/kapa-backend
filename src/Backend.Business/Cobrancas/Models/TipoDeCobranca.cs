namespace Backend.Business.Cobrancas.Models;

/// <summary>
/// O que um item do plano cobra.
/// </summary>
/// <remarks>
/// Gravado como texto, pelo mesmo motivo de <c>StatusDaFormatura</c>: número mudaria de sentido no
/// dia em que alguém reordenasse o enum, com parcela já gravada apontando para ele.
/// </remarks>
public enum TipoDeCobranca
{
    /// <summary>A parcela mensal do fundo de formatura.</summary>
    Mensalidade,

    /// <summary>Taxa de entrada. No máximo uma por plano.</summary>
    Adesao,

    /// <summary>Cota de rifa que cada formando vende.</summary>
    Rifa,

    /// <summary>Convite a mais para a festa.</summary>
    ConviteExtra,

    /// <summary>
    /// Qualquer outra cobrança pontual.
    /// </summary>
    /// <remarks>
    /// É o único tipo que aceita valor negativo: é o gancho da bolsa e da isenção, cuja tela fica
    /// para depois do lançamento.
    /// </remarks>
    Avulsa,
}

/// <summary>Como o item aparece em documento e e-mail — o front tem a mesma tabela para a tela.</summary>
public static class RotuloDoItem
{
    /// <summary>A descrição que a tesouraria deu; sem ela, o nome do tipo.</summary>
    /// <param name="tipo">Tipo do item.</param>
    /// <param name="descricao">Descrição, se houver.</param>
    public static string De(TipoDeCobranca tipo, string? descricao) =>
        !string.IsNullOrWhiteSpace(descricao)
            ? descricao
            : tipo switch
            {
                TipoDeCobranca.Adesao => "Adesão",
                TipoDeCobranca.ConviteExtra => "Convite extra",
                _ => tipo.ToString(),
            };
}
