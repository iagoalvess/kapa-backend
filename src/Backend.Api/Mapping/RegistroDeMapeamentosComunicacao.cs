using Backend.Api.DTOs.Comunicacao;
using Backend.Business.Comunicacao.Models;
using Mapster;

namespace Backend.Api.Mapping;

/// <summary>Mapeamento entre os modelos do mural e do acervo e os DTOs.</summary>
public sealed class RegistroDeMapeamentosComunicacao : IRegister
{
    /// <inheritdoc />
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<AvisoResumo, AvisoDTO>();
        config.NewConfig<DocumentoResumo, DocumentoDTO>();
        config.NewConfig<DocumentosNaCategoria, DocumentosNaCategoriaDTO>();
        config.NewConfig<ResumoDoAcervo, ResumoDoAcervoDTO>();
    }
}
