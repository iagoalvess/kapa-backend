using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Cobrancas.Services;

/// <summary>
/// Põe o valor do dia nas parcelas lidas, pelas regras que cada formando aceitou.
/// </summary>
/// <remarks>
/// Um lugar só para a lista da gestão, o extrato e a fila da conferência: as regras de todos os
/// vínculos da lista numa consulta, e a mesma conta de <see cref="ValorDoDia"/> em cada linha.
/// </remarks>
public static class ValorDasParcelas
{
    /// <summary>As parcelas com o valor do dia; a que não está em aberto fica sem.</summary>
    /// <param name="repositorio">Parcelas, para as regras aceitas.</param>
    /// <param name="parcelas">Parcelas lidas.</param>
    /// <param name="dia">Dia de referência — hoje.</param>
    public static async Task<IReadOnlyList<ParcelaResumo>> ComValorDoDia(
        this IParcelaRepository repositorio,
        IReadOnlyList<ParcelaResumo> parcelas,
        DateOnly dia,
        CancellationToken ct = default
    )
    {
        if (!parcelas.Any(parcela => parcela.EmAberto))
            return parcelas;

        var regras = await repositorio.ObterRegrasDeAtraso([.. parcelas.Where(p => p.EmAberto).Select(p => p.VinculoId).Distinct()], ct);

        return [.. parcelas.Select(parcela => parcela.ComValorDoDia(dia, regras.GetValueOrDefault(parcela.VinculoId, RegrasDeAtraso.Nenhuma)))];
    }
}
