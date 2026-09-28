using Backend.Api.DTOs.Festa;
using Backend.Business.Festa.Models;
using Mapster;

namespace Backend.Api.Mapping;

/// <summary>
/// Mapeamento dos convites da festa para os DTOs.
/// </summary>
/// <remarks>
/// O evento é o único que pede configuração: os horários do contrato saem de propriedades calculadas
/// do modelo (<c>FechamentoEmUtc</c>, <c>JanelaAbreEmUtc</c>), e o contrato não leva o sufixo — datas
/// da API são sempre UTC.
/// </remarks>
public sealed class RegistroDeMapeamentosFesta : IRegister
{
    /// <inheritdoc />
    public void Register(TypeAdapterConfig config)
    {
        config
            .NewConfig<EventoDoConvite, EventoDoConviteDTO>()
            .MapWith(evento => new EventoDoConviteDTO(
                evento.Id,
                evento.Tipo,
                evento.Titulo,
                evento.Data,
                evento.Hora,
                evento.Local,
                evento.Completo,
                evento.FechamentoEmUtc,
                evento.JanelaAbreEmUtc,
                evento.JanelaFechaEmUtc
            ));
        config.NewConfig<ConvitePublico, ConvitePublicoDTO>();
        config.NewConfig<MeuConvite, MeuConviteDTO>();
        config.NewConfig<MeusConvites, MeusConvitesDTO>();
        config.NewConfig<EntradaNaPortaria, EntradaNaPortariaDTO>();
        config.NewConfig<ConviteNaPortaria, ConviteNaPortariaDTO>();
        config.NewConfig<ConsultaNaPortaria, ConsultaNaPortariaDTO>();
        config.NewConfig<ListaDaPortaria, ListaDaPortariaDTO>();
        config.NewConfig<ResultadoDaSincronizacao, ResultadoDaSincronizacaoDTO>();
        config.NewConfig<ResumoDosConvites, ResumoDosConvitesDTO>();
        config.NewConfig<MesaResumo, MesaDTO>();
        config.NewConfig<CompradorDeMesa, CompradorDeMesaDTO>();
        config.NewConfig<MapaDeMesas, MapaDeMesasDTO>();
    }
}
