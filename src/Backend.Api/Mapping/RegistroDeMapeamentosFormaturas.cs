using Backend.Api.DTOs.Formaturas;
using Backend.Business.Formaturas.Models;
using Mapster;

namespace Backend.Api.Mapping;

/// <summary>
/// Mapeamento dos modelos de leitura de formatura para os DTOs.
/// </summary>
public sealed class RegistroDeMapeamentosFormaturas : IRegister
{
    /// <inheritdoc />
    public void Register(TypeAdapterConfig config) => config.NewConfig<FormaturaDoUsuario, FormaturaDoUsuarioDTO>();
}
