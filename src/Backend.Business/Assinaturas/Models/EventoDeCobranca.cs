namespace Backend.Business.Assinaturas.Models;

/// <summary>
/// Evento recebido do provedor de assinatura, gravado uma vez por id.
/// </summary>
/// <remarks>
/// É a trava de idempotência do webhook: <see cref="IdExterno"/> tem índice único, e o provedor
/// reenviar um evento — o comportamento correto dele em timeout — não reprocessa nada.
/// <para>
/// <b>Não</b> herda de <c>EntidadeDaFormatura</c>, ao contrário do que a Sprint 3 previa. O
/// webhook não tem sessão nem formatura selecionada, evento de tipo desconhecido pode nem trazer
/// formatura, e a pergunta "este id já chegou?" precisa enxergar a tabela inteira — um filtro por
/// turma ali deixaria o mesmo evento passar duas vezes. A formatura fica gravada como dado
/// (<see cref="FormaturaId"/>), para consulta.
/// </para>
/// <para>
/// Também não herda de <c>Entity</c>: é append-only, como <c>Evento</c>. Nasce já processado, na
/// mesma transação que aplica o efeito.
/// </para>
/// </remarks>
public class EventoDeCobranca
{
    /// <summary>Identificador interno.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Id do evento no provedor. Único.</summary>
    public string IdExterno { get; init; } = string.Empty;

    /// <summary>Tipo, no vocabulário de <see cref="TiposDeEvento"/> (ou o que o provedor mandou, se desconhecido).</summary>
    public string Tipo { get; init; } = string.Empty;

    /// <summary>Assinatura a que o evento se refere, quando o provedor informa.</summary>
    public Guid? AssinaturaId { get; init; }

    /// <summary>Formatura da assinatura, quando encontrada.</summary>
    public Guid? FormaturaId { get; init; }

    /// <summary>Corpo recebido, já verificado. Nunca vai para log — só para esta tabela.</summary>
    public string Payload { get; init; } = string.Empty;

    /// <summary>Chegada, em UTC.</summary>
    public DateTime RecebidoEm { get; init; } = DateTime.UtcNow;

    /// <summary>Quando o efeito foi aplicado. Nulo para evento ignorado (tipo desconhecido, assinatura inexistente).</summary>
    public DateTime? ProcessadoEm { get; init; }
}

/// <summary>
/// Vocabulário de eventos que o sistema trata.
/// </summary>
/// <remarks>
/// É o contrato entre o provedor e o domínio: cada implementação de <c>IProvedorDeAssinatura</c>
/// traduz os nomes do PSP para estes. Tipo fora da lista é gravado e ignorado com 200 — devolver
/// erro para evento que não interessa faz o provedor reentregar para sempre.
/// </remarks>
public static class TiposDeEvento
{
    /// <summary>Primeiro pagamento confirmado.</summary>
    public const string PagamentoConfirmado = "pagamento.confirmado";

    /// <summary>Pagamento recusado (cartão, saldo).</summary>
    public const string PagamentoRecusado = "pagamento.recusado";

    /// <summary>Ciclo seguinte cobrado com sucesso.</summary>
    public const string AssinaturaRenovada = "assinatura.renovada";

    /// <summary>Renovação cancelada no provedor.</summary>
    public const string AssinaturaCancelada = "assinatura.cancelada";

    /// <summary>Provedor desistiu de cobrar.</summary>
    public const string AssinaturaVencida = "assinatura.vencida";
}
