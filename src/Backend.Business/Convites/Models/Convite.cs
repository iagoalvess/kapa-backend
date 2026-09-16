using Backend.Business.Abstractions;
using Backend.Business.Formaturas.Models;

namespace Backend.Business.Convites.Models;

/// <summary>
/// Convite para entrar numa formatura — nominal (um e-mail, um uso) ou o link aberto da turma.
/// </summary>
/// <remarks>
/// Um modelo só para os dois usos: o que muda entre eles é <see cref="Email"/>,
/// <see cref="UsosMaximos"/> e a validade, não a regra. Duas entidades seriam dois fluxos de
/// aceite, de expiração e de auditoria, com a mesma regra escrita duas vezes.
/// <para>
/// O aceite procura sempre pelo SHA-256 do token, como no refresh token. O nominal grava só o
/// hash: vazamento do banco não entrega convite pessoal utilizável. O link da turma grava também
/// o token puro (<see cref="Token"/>), para a comissão copiar de novo.
/// </para>
/// <para>
/// A turma tem um link vigente por vez: gerar outro revoga o anterior.
/// </para>
/// <para>
/// <c>ponytail:</c> aprovação manual de quem entra pelo link aberto só se uma turma pedir. O
/// gancho é um campo <c>ExigeAprovacao</c> aqui.
/// </para>
/// </remarks>
public class Convite : EntidadeDaFormatura
{
    /// <summary>Validade do convite nominal. Fixa: a tela informa em texto, não pergunta.</summary>
    public const int DiasDeValidadeDoNominal = 7;

    /// <summary>
    /// Validade do link da turma. Fixa, como o limite de entradas — o número estimado de formandos
    /// no momento em que o link é gerado.
    /// </summary>
    public const int DiasDeValidadeDoLink = 30;

    /// <summary>SHA-256 do token, em hexadecimal. É por ele que o aceite encontra o convite.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Token puro do link da turma, para mostrar o link de novo. Nulo no nominal.</summary>
    /// <remarks>
    /// Troca aceita: quem lê o banco entra na turma como Formando — o mesmo que qualquer um do grupo
    /// de WhatsApp onde o link já circula —, dentro do limite de entradas e até alguém revogar. O
    /// nominal não grava: ele vale papel de comissão e é de uma pessoa só.
    /// </remarks>
    public string? Token { get; set; }

    /// <summary>E-mail convidado. Nulo é o link aberto da turma.</summary>
    public string? Email { get; set; }

    /// <summary>Papel que o vínculo recebe no aceite. Ver <see cref="PapelNaFormatura"/>.</summary>
    public string Papel { get; set; } = PapelNaFormatura.Formando;

    /// <summary>Até quando o convite vale, em UTC.</summary>
    public DateTime ExpiraEm { get; set; }

    /// <summary>Quantos aceites o convite admite. Nulo é ilimitado até expirar.</summary>
    public int? UsosMaximos { get; set; }

    /// <summary>
    /// Aceites já feitos.
    /// </summary>
    /// <remarks>
    /// Só muda pelo <c>UPDATE</c> condicional do aceite (<c>IConviteRepository.ConsumirUsoDeTodasAsFormaturas</c>):
    /// ler, somar e salvar deixa dois cliques no mesmo segundo passarem pelo limite de um.
    /// </remarks>
    public int UsosFeitos { get; set; }

    /// <summary>Quando o convite foi revogado, em UTC.</summary>
    public DateTime? RevogadoEm { get; private set; }

    /// <summary>Quem criou o convite.</summary>
    public Guid CriadoPorUsuarioId { get; set; }

    /// <summary>Situação do convite no instante informado.</summary>
    /// <param name="agoraUtc">Momento da verificação.</param>
    public StatusDoConvite StatusEm(DateTime agoraUtc) => Situacao(RevogadoEm, UsosMaximos, UsosFeitos, ExpiraEm, agoraUtc);

    /// <summary>
    /// A regra da situação, sobre os campos soltos.
    /// </summary>
    /// <remarks>
    /// Estática para caber na projeção da listagem, que não materializa a entidade. Revogado vence
    /// tudo; usos esgotados vencem a validade — convite aceito que depois expirou continua aceito.
    /// </remarks>
    /// <param name="revogadoEm">Revogação.</param>
    /// <param name="usosMaximos">Limite de aceites.</param>
    /// <param name="usosFeitos">Aceites feitos.</param>
    /// <param name="expiraEm">Validade.</param>
    /// <param name="agoraUtc">Momento da verificação.</param>
    public static StatusDoConvite Situacao(DateTime? revogadoEm, int? usosMaximos, int usosFeitos, DateTime expiraEm, DateTime agoraUtc) =>
        revogadoEm is not null ? StatusDoConvite.Revogado
        : usosMaximos is { } maximo && usosFeitos >= maximo ? StatusDoConvite.Aceito
        : expiraEm <= agoraUtc ? StatusDoConvite.Expirado
        : StatusDoConvite.Pendente;

    /// <summary>Revoga o convite. Idempotente: revogar de novo mantém a data da primeira vez.</summary>
    /// <param name="agoraUtc">Momento da revogação.</param>
    public void Revogar(DateTime agoraUtc) => RevogadoEm ??= agoraUtc;
}

/// <summary>Situação de um convite.</summary>
public enum StatusDoConvite
{
    /// <summary>Ainda aceita entrada.</summary>
    Pendente,

    /// <summary>Usos esgotados — no nominal, a pessoa entrou.</summary>
    Aceito,

    /// <summary>Passou da validade sem esgotar.</summary>
    Expirado,

    /// <summary>A comissão cancelou.</summary>
    Revogado,
}
