using Backend.Business.Abstractions;
using Backend.Business.Common.Datas;
using Backend.Business.Relatorios.Interfaces;
using Backend.Business.Relatorios.Models;

namespace Backend.Business.Relatorios.Services;

/// <summary>
/// O painel da turma.
/// </summary>
/// <remarks>
/// <b>Fonte única</b> (decisão 4): saldo, arrecadado, gasto, a receber e o fluxo mês a mês são do
/// <c>ICaixaService</c>, e a tela os lê de <c>GET /financeiro/caixa</c>. Este serviço devolve só o que
/// o caixa não conhece — se repetisse o caixa, um dia o dashboard e o balancete discordariam.
/// <para>
/// <see cref="Publico"/> não tem como devolver nome de inadimplente: ele não consulta lista nenhuma
/// de pessoa. É a garantia mais barata que existe contra o risco jurídico da sprint — não é uma
/// condição que alguém pode inverter numa refatoração distraída.
/// </para>
/// </remarks>
/// <param name="relatorioRepository">O que o caixa não conhece: adimplência e fornecedores.</param>
public sealed class DashboardService(IRelatorioRepository relatorioRepository) : IDashboardService
{
    /// <inheritdoc />
    public async Task<Result<IndicadoresPublicos>> Publico(CancellationToken ct = default) =>
        new IndicadoresPublicos(await relatorioRepository.Adimplencia(DataUtils.Hoje(), ct), await relatorioRepository.PorFornecedor(null, ct));
}
