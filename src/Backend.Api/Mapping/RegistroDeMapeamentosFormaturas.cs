using Backend.Api.DTOs.Formaturas;
using Backend.Business.Formaturas.Models;
using Mapster;

namespace Backend.Api.Mapping;

/// <summary>
/// Mapeamento entre os modelos de formatura e os DTOs.
/// </summary>
public sealed class RegistroDeMapeamentosFormaturas : IRegister
{
    /// <inheritdoc />
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<FormaturaDoUsuario, FormaturaDoUsuarioDTO>();
        config.NewConfig<FormaturaDetalhe, FormaturaDetalheDTO>();
        config.NewConfig<DadosDaFormaturaRequestDTO, DadosDaFormatura>();
    }
}
