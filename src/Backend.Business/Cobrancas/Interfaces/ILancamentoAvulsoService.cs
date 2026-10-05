using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Cobrancas.Interfaces;

/// <summary>
/// O lançamento avulso no vínculo de um formando — o espelho individual do rateio (Sprint 48, D23).
/// </summary>
/// <remarks>
/// A multa da mesa quebrada, a segunda via, a taxa de emissão — e o crédito: bolsa, desconto, isenção. Vira parcela
/// como qualquer outra, e por isso entra no extrato, no PIX e no caixa sem caminho novo. Desfazer é encerrar o item.
/// </remarks>
public interface ILancamentoAvulsoService
{
    /// <summary>Os lançamentos da turma, do mais novo para o mais antigo.</summary>
    Task<Result<IReadOnlyList<LancamentoResumo>>> Listar(CancellationToken ct = default);

    /// <summary>Lança o valor no vínculo e grava as parcelas.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="dados">Formando, descrição, valor, parcelas e primeiro vencimento.</param>
    /// <param name="autorId">Quem lançou — vai na trilha.</param>
    /// <returns>O lançamento como a lista o mostra.</returns>
    Task<Result<LancamentoResumo>> Lancar(Guid formaturaId, LancamentoAvulso dados, Guid autorId, CancellationToken ct = default);
}
