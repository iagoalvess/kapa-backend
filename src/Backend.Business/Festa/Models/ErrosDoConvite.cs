using Backend.Business.Abstractions;

namespace Backend.Business.Festa.Models;

/// <summary>As falhas do convite da festa que mais de um service devolve.</summary>
public static class ErrosDoConvite
{
    /// <summary>
    /// Genérico de propósito: código inexistente, assinatura errada, convite revogado na página
    /// pública e convite de outra turma respondem a mesma coisa — a rota não diz se o código existe.
    /// </summary>
    public static readonly Erro NaoEncontrado = Erro.NaoEncontrado("festa.convite_nao_encontrado", "Convite não encontrado.");

    /// <summary>Sem o evento na agenda, ou sem hora ou local: convite impresso sem endereço é papel inútil (P6).</summary>
    public static readonly Erro EventoIncompleto = Erro.Conflito(
        "festa.evento_incompleto",
        "O evento precisa estar na agenda com data, hora e local antes de qualquer convite sair."
    );

    /// <summary>O convite deixou de valer — a portaria mostra o motivo (decisão 8).</summary>
    public static Erro Revogado(string? motivo) =>
        Erro.Conflito("festa.convite_revogado", $"Este convite foi revogado{(string.IsNullOrWhiteSpace(motivo) ? "." : $": {motivo}.")}");

    /// <summary>O convite é de outro evento da turma — informação, não "código inválido" (decisão 13).</summary>
    public static readonly Erro OutroEvento = Erro.Conflito("festa.outro_evento", "Este convite é de outro evento.");

    /// <summary>Convite sem nome ou documento não entra como anônimo (P5): a Gestão nomeia antes.</summary>
    public static readonly Erro SemTitular = Erro.Conflito(
        "festa.convite_sem_titular",
        "Este convite ainda não tem nome e documento do convidado. Complete antes de validar a entrada."
    );

    /// <summary>
    /// O convite do pacote está preso: o dono tem parcela vencida além da carência (Sprint 47, D24).
    /// </summary>
    /// <remarks>Não é revogação: regularizar — ou a comissão liberar — solta o mesmo convite, com o mesmo código.</remarks>
    public static readonly Erro Preso = Erro.Conflito(
        "festa.convite_preso",
        "Este convite está preso por parcela em atraso do formando. Ele volta a valer quando o pagamento for regularizado ou a comissão liberar."
    );
}
