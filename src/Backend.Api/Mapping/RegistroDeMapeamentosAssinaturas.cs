using Backend.Api.DTOs.Assinaturas;
using Backend.Business.Assinaturas.Models;
using Mapster;

namespace Backend.Api.Mapping;

/// <summary>
/// Mapeamento entre os modelos de assinatura e os DTOs.
/// </summary>
public sealed class RegistroDeMapeamentosAssinaturas : IRegister
{
    /// <inheritdoc />
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<PlanoResumo, PlanoDTO>();
        config.NewConfig<AssinaturaDetalhe, AssinaturaDTO>();
        config.NewConfig<SessaoDeCheckout, CheckoutDTO>();
        config.NewConfig<ReciboDeWebhook, ReciboDeWebhookDTO>();
    }
}
