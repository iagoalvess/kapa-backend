using Backend.Business.Abstractions;
using Backend.Business.Common.Datas;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Relatorios.Interfaces;
using Backend.Business.Relatorios.Models;

namespace Backend.Business.Relatorios.Services;

/// <summary>
/// O painel da turma.
/// </summary>
/// <remarks>
/// <b>Fonte única</b> (decisão 4): saldo, arrecadado, gasto, a receber, o quadro por categoria e o
/// fluxo mês a mês são pedidos ao <see cref="ICaixaService"/> e repassados como vieram. Este serviço
/// não soma centavo nenhum — se somasse, um dia o dashboard e o balancete discordariam, e aí ninguém
/// confiaria em nenhum dos dois.
/// <para>
/// <see cref="Publico"/> não tem como devolver nome de inadimplente: ele não consulta lista nenhuma
/// de pessoa. É a garantia mais barata que existe contra o risco jurídico da sprint — não é uma
/// condição que alguém pode inverter numa refatoração distraída.
/// </para>
/// </remarks>
/// <param name="caixaService">A consolidação do caixa — a origem de todo indicador.</param>
/// <param name="relatorioRepository">O que o caixa não conhece: adimplência e fornecedores.</param>
public sealed class DashboardService(ICaixaService caixaService, IRelatorioRepository relatorioRepository) : IDashboardService
{
    /// <inheritdoc />
    public async Task<Result<IndicadoresPublicos>> Publico(Guid formaturaId, CancellationToken ct = default)
    {
        var caixa = await caixaService.Consolidado(ct);
        var projecao = await caixaService.Projecao(formaturaId, ct);

        if (caixa.Falhou || projecao.Falhou)
            return Result.Falha<IndicadoresPublicos>(caixa.Falhou ? caixa.Erros : projecao.Erros);

        return new IndicadoresPublicos(
            caixa.Valor,
            await relatorioRepository.Adimplencia(DataUtils.Hoje(), ct),
            await relatorioRepository.PorFornecedor(null, ct),
            projecao.Valor.Meses
        );
    }
}
