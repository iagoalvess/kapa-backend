using Backend.Api.DTOs.Usuarios;
using Backend.Business.Usuarios.Models;
using Mapster;

namespace Backend.Api.Mapping;

/// <summary>
/// Ajustes de mapeamento entre modelos de domínio e DTOs.
/// </summary>
/// <remarks>
/// O Mapster já converte por nome: <c>UsuarioResumo</c> vira <c>UsuarioResumoDTO</c> sem
/// nenhuma linha aqui. Este arquivo existe para o que **não** casa por nome — renome de campo,
/// achatamento, valor calculado.
/// <para>
/// Crie um <see cref="IRegister"/> por feature. Todos são descobertos pela varredura do
/// assembly em <c>ApiConfig</c>, então não há lista central para manter.
/// </para>
/// <para>
/// <c>RequireDestinationMemberSource</c> está ligado no <c>ApiConfig</c>: se um campo do DTO não
/// tiver origem, o mapeamento falha no teste em vez de devolver <c>null</c> silenciosamente ao
/// front. É a diferença entre descobrir o erro no CI e descobrir na tela do usuário.
/// </para>
/// </remarks>
public sealed class RegistroDeMapeamentosUsuarios : IRegister
{
    /// <inheritdoc />
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<UsuarioDetalhe, UsuarioDetalheDTO>();
        config.NewConfig<UsuarioResumo, UsuarioResumoDTO>();
        config.NewConfig<AtualizarUsuarioRequestDTO, AtualizarUsuario>();
        config.NewConfig<AlterarPerfisRequestDTO, AlterarPerfis>();
    }
}
