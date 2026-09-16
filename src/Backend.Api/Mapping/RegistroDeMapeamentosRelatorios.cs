using Backend.Api.DTOs.Relatorios;
using Backend.Business.Relatorios.Interfaces;
using Backend.Business.Relatorios.Models;
using Mapster;

namespace Backend.Api.Mapping;

/// <summary>
/// Mapeamento entre os modelos de relatório e os DTOs.
/// </summary>
/// <remarks>
/// <c>EmAtrasoEmCentavos</c>, <c>PercentualBaseDezMil</c> e os três totais do balancete são
/// propriedades calculadas dos modelos e casam por nome — nenhum deles é coluna, e nenhum é
/// recalculado aqui.
/// </remarks>
public sealed class RegistroDeMapeamentosRelatorios : IRegister
{
    /// <inheritdoc />
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Adimplencia, AdimplenciaDTO>();
        config.NewConfig<GastoPorFornecedor, GastoPorFornecedorDTO>();
        config.NewConfig<LinhaDeBalancete, LinhaDeBalanceteDTO>();
        config.NewConfig<MesDoBalancete, MesDoBalanceteDTO>();
        config.NewConfig<TotaisDoPeriodo, TotaisDoPeriodoDTO>();
        config.NewConfig<IndicadoresPublicos, DashboardPublicoDTO>();

        config
            .NewConfig<Balancete, BalanceteDTO>()
            .Map(destino => destino.De, origem => origem.Periodo.De)
            .Map(destino => destino.Ate, origem => origem.Periodo.Ate);

        config.NewConfig<SolicitacaoResumo, SolicitacaoDTO>();
        config.NewConfig<OpcaoDeFiltro, OpcaoDeFiltroDTO>();
        config.NewConfig<OpcoesDeFiltro, OpcoesDeFiltroDTO>();
    }
}
