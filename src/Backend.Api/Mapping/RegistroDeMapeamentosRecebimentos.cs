using Backend.Api.DTOs.Recebimentos;
using Backend.Business.Recebimentos.Models;
using Mapster;

namespace Backend.Api.Mapping;

/// <summary>Mapeamento entre os modelos de recebimento e os DTOs.</summary>
public sealed class RegistroDeMapeamentosRecebimentos : IRegister
{
    /// <inheritdoc />
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<ContaDeRecebimentoDetalhe, ContaDeRecebimentoDTO>();
        config.NewConfig<ContaDeRecebimentoDaTurma, ContaDeRecebimentoDaTurmaDTO>();
        config.NewConfig<PixDeTeste, PixDeTesteDTO>();
    }
}
