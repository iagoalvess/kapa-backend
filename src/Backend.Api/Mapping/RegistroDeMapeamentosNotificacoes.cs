using Backend.Api.DTOs.Notificacoes;
using Backend.Business.Notificacoes.Models;
using Mapster;

namespace Backend.Api.Mapping;

/// <summary>
/// Mapeamento entre os modelos da régua e os DTOs.
/// </summary>
/// <remarks>
/// Tudo casa por nome; o registro existe para o teste de configuração conferir que nenhum campo do
/// DTO ficou sem origem (<c>RequireDestinationMemberSource</c>).
/// </remarks>
public sealed class RegistroDeMapeamentosNotificacoes : IRegister
{
    /// <inheritdoc />
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<RegraResumo, RegraDTO>();
        config.NewConfig<NotificacaoNoHistorico, NotificacaoDTO>();
    }
}
