using Backend.Business.Recebimentos.Models;

namespace Backend.Business.Recebimentos.Interfaces;

/// <summary>
/// A conta de recebimento da formatura selecionada — uma só, isolada pelo filtro global.
/// </summary>
public interface IContaDeRecebimentoRepository
{
    /// <summary>A conta, com o nome de quem a conferiu, ou nulo se a turma ainda não cadastrou.</summary>
    Task<ContaDeRecebimentoDetalhe?> ObterDetalhe(CancellationToken ct = default);

    /// <summary>A conta, rastreada para alteração, ou nulo.</summary>
    Task<ContaDeRecebimento?> ObterParaEdicao(CancellationToken ct = default);

    /// <summary>Registra a primeira conta da turma.</summary>
    /// <param name="conta">Conta a persistir.</param>
    Task Adicionar(ContaDeRecebimento conta, CancellationToken ct = default);
}
