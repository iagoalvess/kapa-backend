using Backend.Api.DTOs.Adesoes;
using Backend.Business.Adesoes.Models;
using Backend.Business.Cobrancas.Models;
using Mapster;

namespace Backend.Api.Mapping;

/// <summary>
/// Mapeamento entre os modelos de adesão e os DTOs.
/// </summary>
/// <remarks>A parcela do plano aceito usa o <c>ParcelaSimulada → ParcelaSimuladaDTO</c> de cobranças.</remarks>
public sealed class RegistroDeMapeamentosAdesoes : IRegister
{
    /// <inheritdoc />
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<VersaoDoTermo, VersaoDoTermoDTO>();
        config.NewConfig<TermoPublicado, TermoPublicadoDTO>();
        config.NewConfig<DadosDoItem, ItemAceitoDTO>();
        config.NewConfig<SnapshotDoPlano, PlanoAceitoDTO>();
        config.NewConfig<ConteudoParaAdesao, ConteudoParaAdesaoDTO>();
        config.NewConfig<CodigoEnviado, CodigoEnviadoDTO>();
        config.NewConfig<AdesaoDetalhe, AdesaoDTO>();
        config.NewConfig<MinhaAdesao, MinhaAdesaoDTO>();
        config.NewConfig<SituacaoDeAdesao, SituacaoDeAdesaoDTO>();
        config.NewConfig<ResumoDeAdesoes, ResumoDeAdesoesDTO>();
    }
}
