using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Cobrancas.Interfaces;

/// <summary>As condições financeiras da turma (Sprint 54).</summary>
public interface ICondicaoFinanceiraRepository
{
    /// <summary>A condição de quem já está na turma, só para leitura.</summary>
    /// <param name="vinculoId">Vínculo.</param>
    Task<CondicaoFinanceira?> ObterDoVinculo(Guid vinculoId, CancellationToken ct = default);

    /// <summary>A condição de quem já está na turma, rastreada para alterar ou remover.</summary>
    /// <param name="vinculoId">Vínculo.</param>
    Task<CondicaoFinanceira?> ObterDoVinculoParaEdicao(Guid vinculoId, CancellationToken ct = default);

    /// <summary>A condição preparada para o e-mail de um convite pessoal, rastreada.</summary>
    /// <param name="email">E-mail já normalizado.</param>
    Task<CondicaoFinanceira?> ObterDoConvidadoParaEdicao(string email, CancellationToken ct = default);

    /// <summary>
    /// A condição preparada para o e-mail, rastreada, sem a formatura na sessão — o aceite do convite acontece antes dela.
    /// </summary>
    /// <param name="formaturaId">Turma do convite.</param>
    /// <param name="email">E-mail já normalizado.</param>
    Task<CondicaoFinanceira?> ObterDoConvidadoDeTodasAsFormaturas(Guid formaturaId, string email, CancellationToken ct = default);

    /// <summary>Marca uma condição nova para inclusão.</summary>
    /// <param name="condicao">Condição.</param>
    Task Adicionar(CondicaoFinanceira condicao, CancellationToken ct = default);

    /// <summary>Marca a condição para remoção.</summary>
    /// <param name="condicao">Condição rastreada.</param>
    void Remover(CondicaoFinanceira condicao);
}
