using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;

namespace Backend.Business.Cobrancas.Services;

/// <summary>
/// Grava, em nome de um formando, as parcelas do plano vigente.
/// </summary>
/// <remarks>
/// Idempotente por construção: lê o que o vínculo já tem e só marca o que falta. A corrida — dois
/// aceites da mesma pessoa ao mesmo tempo — fica com o índice único
/// <c>(vinculo_id, item_de_cobranca_id, numero)</c>, que recusa a segunda gravação.
/// </remarks>
/// <param name="parcelaRepository">Parcelas já geradas.</param>
public sealed class GeracaoDeParcelasService(IParcelaRepository parcelaRepository) : IGeracaoDeParcelasService
{
    /// <inheritdoc />
    public async Task<Result<int>> Gerar(Guid vinculoId, PlanoDeCobranca plano, CancellationToken ct = default)
    {
        if (plano.Status != StatusDoPlano.Vigente)
            return Erro.Conflito("cobranca.sem_plano_vigente", "A turma ainda não tem plano de cobrança em vigor.");

        var hoje = DataUtils.Hoje();
        var novas = new List<Parcela>();

        foreach (var item in plano.ItensAtivos)
        {
            var geradas = await parcelaRepository.ListarNumerosGerados(vinculoId, item.Id, ct);

            novas.AddRange(
                GradeDeParcelas
                    .DeQuemAdereEm(item.ParaDados(), hoje)
                    .Where(prevista => !geradas.Contains(prevista.Numero))
                    .Select(prevista => Parcela.Nova(vinculoId, item.Id, prevista))
            );
        }

        if (novas.Count > 0)
            await parcelaRepository.Adicionar(novas, ct);

        return novas.Count;
    }

    /// <inheritdoc />
    public async Task<Result<int>> GerarDoItem(IReadOnlyCollection<Guid> vinculoIds, ItemDeCobranca item, CancellationToken ct = default)
    {
        if (vinculoIds.Count == 0)
            return 0;

        var grade = GradeDeParcelas.DeQuemAdereEm(item.ParaDados(), DataUtils.Hoje());
        List<Parcela> novas = [.. vinculoIds.SelectMany(vinculoId => grade.Select(prevista => Parcela.Nova(vinculoId, item.Id, prevista)))];

        await parcelaRepository.Adicionar(novas, ct);

        return novas.Count;
    }
}
