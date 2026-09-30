namespace Backend.Business.Recebimentos.Models;

/// <summary>Uma cobrança que a conciliação precisa consultar, e de qual turma ela é.</summary>
/// <param name="CobrancaId">Cobrança.</param>
/// <param name="FormaturaId">Turma, para o job apontar o escopo.</param>
public sealed record CobrancaAConciliar(Guid CobrancaId, Guid FormaturaId);
