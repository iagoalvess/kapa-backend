using Backend.Api.DTOs.Pagamentos;
using Backend.Business.Pagamentos.Models;
using Mapster;

namespace Backend.Api.Mapping;

/// <summary>
/// Mapeamento entre os modelos de pagamento e os DTOs.
/// </summary>
public sealed class RegistroDeMapeamentosPagamentos : IRegister
{
    /// <inheritdoc />
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<ExtratoDoFormando, ExtratoDTO>();
        config.NewConfig<ProximasParcelas, ProximasParcelasDTO>();
        config.NewConfig<CobrancaDaParcela, CobrancaDaParcelaDTO>();
        config.NewConfig<InformeNaFila, InformeDTO>();
        config.NewConfig<ResultadoDaConferencia, ResultadoDaConferenciaDTO>();
        config.NewConfig<ConfirmacaoDeInformeDTO, ConfirmacaoDeInforme>();
        config.NewConfig<Divergencia, DivergenciaDTO>();
    }
}
