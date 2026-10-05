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

    /// <summary>Foto individual, álbum, ensaio.</summary>
    /// <remarks>
    /// Este e os tipos seguintes são só dos opcionais (22/09): categorias do que cada formando compra
    /// para si. Nenhum tem comportamento — servem ao ícone, ao filtro e ao relatório. Quem gera
    /// convite continua sendo só o <see cref="ConviteExtra"/>.
    /// </remarks>
    FotoEAlbum,

    /// <summary>Vídeo da colação, clipe da turma.</summary>
    Filmagem,

    /// <summary>Aluguel ou compra da beca.</summary>
    Beca,

    /// <summary>Camiseta, moletom, jaqueta da turma.</summary>
    Vestuario,

    /// <summary>Kit do formando, caneca, lembranças.</summary>
    Kit,

    /// <summary>Uma mesa inteira na festa ou no jantar.</summary>
    Mesa,

    /// <summary>Anel de formatura, pingente.</summary>
    Joia,

    /// <summary>O opcional que não cabe nos outros — o <see cref="Avulsa"/> dos opcionais, sem valor negativo.</summary>
    Outro,

    /// <summary>A festa de formatura — o pacote cuja faixa diz quantas pessoas vão (Sprint 47, D32).</summary>
    Festa,

    /// <summary>A colação de grau — o pacote que concede os convites dela (Sprint 47).</summary>
    Colacao,
}

/// <summary>Que tipo cabe onde: o plano cobra a turma inteira, o opcional só quem pede.</summary>
public static class TiposDeCobranca
{
    /// <summary>
    /// Os tipos de um rateio extraordinário — o item que alcança quem já aderiu: todos. A assembleia pode ratear a
    /// festa, a colação ou um avulso, inclusive negativo (a bolsa).
    /// </summary>
    public static readonly IReadOnlySet<TipoDeCobranca> DosRateios = Enum.GetValues<TipoDeCobranca>().ToHashSet();

    /// <summary>
    /// Os tipos de um pacote do catálogo (Sprint 47): todos, menos o <see cref="TipoDeCobranca.Avulsa"/> — o pacote é
    /// o que o formando escolhe comprar, e o avulso é o gancho do valor negativo.
    /// </summary>
    public static readonly IReadOnlySet<TipoDeCobranca> DosPacotes = Enum.GetValues<TipoDeCobranca>()
        .Where(tipo => tipo != TipoDeCobranca.Avulsa)
        .ToHashSet();

    /// <summary>Os tipos de um item opcional. O convite extra é dos dois: a turma o vende nos dois jeitos.</summary>
    public static readonly IReadOnlySet<TipoDeCobranca> DosOpcionais = new HashSet<TipoDeCobranca>
    {
        TipoDeCobranca.ConviteExtra,
        TipoDeCobranca.FotoEAlbum,
        TipoDeCobranca.Filmagem,
        TipoDeCobranca.Beca,
        TipoDeCobranca.Vestuario,
        TipoDeCobranca.Kit,
        TipoDeCobranca.Mesa,
        TipoDeCobranca.Joia,
        TipoDeCobranca.Outro,
    };
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
                TipoDeCobranca.FotoEAlbum => "Foto e álbum",
                TipoDeCobranca.Vestuario => "Vestuário",
                TipoDeCobranca.Joia => "Joia",
                TipoDeCobranca.Colacao => "Colação",
                _ => tipo.ToString(),
            };
}
