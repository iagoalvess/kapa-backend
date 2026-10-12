using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Cobrancas.Services;

/// <summary>
/// O desconto de cada pacote na adesão (Sprint 54) — a mesma conta para a prévia e para o aceite.
/// </summary>
/// <remarks>
/// Quem adere pela primeira vez recebe o que a condição financeira dá hoje. Quem já tem cesta (re-adesão a uma versão
/// nova do termo, D4 da Sprint 47) mantém o desconto que aceitou: a condição de hoje pode ter mudado, mas o contrato não.
/// </remarks>
/// <param name="condicoes">A condição da pessoa.</param>
/// <param name="planos">A cesta já contratada.</param>
public sealed class DescontosDaAdesao(ICondicaoFinanceiraRepository condicoes, IPlanoDeCobrancaRepository planos)
{
    /// <summary>Desconto por pacote, em centavos; só os pacotes alcançados.</summary>
    /// <param name="vinculoId">Quem adere.</param>
    /// <param name="plano">Plano vigente.</param>
    /// <param name="cesta">Pacotes da adesão, já conferidos.</param>
    /// <param name="jaContratada">Se a cesta é a que a pessoa já contratou.</param>
    public async Task<IReadOnlyDictionary<Guid, long>> Da(
        Guid vinculoId,
        PlanoDeCobranca plano,
        IReadOnlyCollection<ItemDeCobranca> cesta,
        bool jaContratada,
        CancellationToken ct = default
    ) =>
        jaContratada
            ? (await planos.ListarEscolhas(vinculoId, ct))
                .Where(escolha => escolha.DescontoEmCentavos > 0)
                .ToDictionary(escolha => escolha.ItemDeCobrancaId, escolha => escolha.DescontoEmCentavos)
            : CondicaoFinanceira.Descontos(await condicoes.ObterDoVinculo(vinculoId, ct), cesta, plano.Itens);
}
