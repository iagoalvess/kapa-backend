using Backend.Api.DTOs.Financeiro;
using Backend.Business.Financeiro.Models;
using Mapster;

namespace Backend.Api.Mapping;

/// <summary>
/// Mapeamento entre os modelos do financeiro e os DTOs.
/// </summary>
/// <remarks>
/// <c>SaldoEmCentavos</c> e <c>SaldoProjetadoEmCentavos</c> são propriedades calculadas do
/// <see cref="CaixaConsolidado"/> e casam por nome — o saldo continua sem existir como coluna.
/// </remarks>
public sealed class RegistroDeMapeamentosFinanceiro : IRegister
{
    /// <inheritdoc />
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<FornecedorResumo, FornecedorDTO>();
        config.NewConfig<DespesaResumo, DespesaDTO>();
        config.NewConfig<SomaDeLancamentos, SomaDeLancamentosDTO>();
        config.NewConfig<ResumoDeDespesas, ResumoDeDespesasDTO>();
        config.NewConfig<GastoPorCategoria, GastoPorCategoriaDTO>();
        config.NewConfig<OutraReceitaPorCategoria, OutraReceitaPorCategoriaDTO>();
        config.NewConfig<OutraReceitaResumo, OutraReceitaDTO>();
        config.NewConfig<ResumoDeOutrasReceitas, ResumoDeOutrasReceitasDTO>();
        config.NewConfig<LancamentoDoCaixa, LancamentoDTO>();
        config.NewConfig<CaixaConsolidado, CaixaDTO>();
        config.NewConfig<MesDoCaixa, MesDoCaixaDTO>();
        config.NewConfig<ProjecaoDoCaixa, ProjecaoDTO>();
    }
}
