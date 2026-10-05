using Backend.Api.DTOs.Cobrancas;
using Backend.Business.Cobrancas.Models;
using Mapster;

namespace Backend.Api.Mapping;

/// <summary>
/// Mapeamento entre os modelos de cobrança e os DTOs.
/// </summary>
public sealed class RegistroDeMapeamentosCobrancas : IRegister
{
    /// <inheritdoc />
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<PlanoDeCobrancaRequestDTO, DadosDoPlano>();
        config.NewConfig<ItemDeCobrancaRequestDTO, DadosDoItem>();
        config.NewConfig<SimularPlanoRequestDTO, SimularPlano>();
        config.NewConfig<PlanoDeCobrancaResumo, PlanoDeCobrancaResumoDTO>();
        config.NewConfig<PlanoDeCobrancaDetalhe, PlanoDeCobrancaDTO>();
        config.NewConfig<ItemDeCobrancaDetalhe, ItemDeCobrancaDTO>();
        config.NewConfig<SimulacaoDoPlano, SimulacaoDoPlanoDTO>();
        config.NewConfig<ParcelaSimulada, ParcelaSimuladaDTO>();
        config.NewConfig<ParcelaResumo, ParcelaDTO>();
        config.NewConfig<ValorDoDia, ValorDoDiaDTO>();
        config.NewConfig<SomaDeParcelas, SomaDeParcelasDTO>();
        config.NewConfig<ResumoDeParcelas, ResumoDeParcelasDTO>();
        config.NewConfig<Alcance, AlcanceDTO>();
        config.NewConfig<LancamentoResumo, LancamentoDTO>();
        config.NewConfig<ResumoDaSolicitacao, SolicitacaoDeCancelamentoDTO>();
    }
}
