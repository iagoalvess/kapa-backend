using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;

namespace Backend.Business.Cobrancas.Services;

/// <summary>
/// As parcelas da turma como a gestão as consulta: a lista e a faixa de totais, com o valor do dia.
/// </summary>
/// <param name="parcelaRepository">Parcelas geradas e as regras que cada formando aceitou.</param>
public sealed class ConsultaDeParcelasService(IParcelaRepository parcelaRepository) : IConsultaDeParcelasService
{
    /// <inheritdoc />
    /// <remarks>Aberta e vencida saem com o valor do dia, pelas regras que cada formando aceitou.</remarks>
    public async Task<Result<PaginaDe<ParcelaResumo>>> Listar(PaginacaoRequest paginacao, FiltroDeParcelas filtro, CancellationToken ct = default)
    {
        var hoje = DataUtils.Hoje();
        var pagina = await parcelaRepository.Listar(paginacao.Normalizar(), filtro, hoje, ct);

        return pagina with
        {
            Itens = await parcelaRepository.ComValorDoDia(pagina.Itens, hoje, ct),
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// A contagem é uma consulta agrupada; o vencido atualizado soma o valor do dia de cada vencida,
    /// pelas regras de cada formando — multa e juros nunca estão gravados, então são somados aqui.
    /// </remarks>
    public async Task<Result<ResumoDeParcelas>> Resumir(FiltroDeParcelas filtro, CancellationToken ct = default)
    {
        var hoje = DataUtils.Hoje();
        var contagem = await parcelaRepository.Contar(filtro, hoje, ct);
        var emAtraso = await parcelaRepository.ListarEmAtraso(filtro, hoje, ct);
        IReadOnlyDictionary<Guid, RegrasDeAtraso> regras =
            emAtraso.Count == 0
                ? new Dictionary<Guid, RegrasDeAtraso>()
                : await parcelaRepository.ObterRegrasDeAtraso([.. emAtraso.Select(p => p.VinculoId).Distinct()], ct);

        SomaDeParcelas Somar(StatusDaParcela status, bool peloPago = false) =>
            contagem.FirstOrDefault(c => c.Status == status) is { } linha
                ? new SomaDeParcelas(linha.Quantidade, peloPago ? linha.PagoEmCentavos : linha.OriginalEmCentavos)
                : SomaDeParcelas.Zero;

        return new ResumoDeParcelas(
            new SomaDeParcelas(contagem.Sum(c => c.Quantidade), contagem.Sum(c => c.OriginalEmCentavos)),
            Somar(StatusDaParcela.Aberta),
            Somar(StatusDaParcela.Vencida),
            Somar(StatusDaParcela.Paga, peloPago: true),
            Somar(StatusDaParcela.Cancelada),
            emAtraso.Sum(parcela =>
                ValorDoDia
                    .Calcular(
                        parcela.ValorOriginalEmCentavos,
                        parcela.Vencimento,
                        hoje,
                        regras.GetValueOrDefault(parcela.VinculoId, RegrasDeAtraso.Nenhuma)
                    )
                    .TotalEmCentavos
            )
        );
    }
}
