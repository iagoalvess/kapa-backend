using Backend.Business.Abstractions;
using Backend.Business.Marketing.Interfaces;
using Backend.Business.Marketing.Models;
using Backend.Business.Marketing.Settings;
using Backend.Business.Notificacoes.Services;
using Microsoft.Extensions.Options;

namespace Backend.Business.Marketing.Services;

/// <summary>
/// Uma rodada das jornadas: acha quem venceu, enfileira o e-mail e registra o envio, tudo numa transação.
/// </summary>
/// <remarks>
/// A regra de quem pode receber está na consulta (<see cref="IComunicacaoDoKapaRepository.ListarCandidatosDeTodasAsFormaturas"/>)
/// e a de qual jornada venceu, em <see cref="CandidatoDeMarketing.JornadaVencida"/>. Aqui ficam o interruptor, a
/// janela e a trava de um e-mail por pessoa por rodada — quem é da comissão de duas turmas que vencem juntas
/// recebe um só, e a outra espera os 14 dias (P3).
/// <para>
/// E-mail e registro entram juntos: registro sem e-mail seria uma jornada perdida, e e-mail sem registro seria
/// a mesma mensagem de novo na próxima hora.
/// </para>
/// <para>
/// <c>ponytail:</c> a rodada carrega todos os candidatos numa consulta. Cabe no volume de turmas do gratuito criadas
/// nos últimos 30 dias; se passar de milhares, paginar por turma.
/// </para>
/// </remarks>
/// <param name="repositorio">Candidatos e registro dos envios.</param>
/// <param name="emails">Montagem dos e-mails.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="options">O interruptor do envio.</param>
public sealed class JornadasDeMarketingService(
    IComunicacaoDoKapaRepository repositorio,
    EmailsDeMarketing emails,
    IUnitOfWork unitOfWork,
    IOptions<ComunicacaoDoKapaSettings> options
) : IJornadasDeMarketingService
{
    /// <inheritdoc />
    public async Task<int> Executar(DateTime agoraUtc, CancellationToken ct = default)
    {
        if (!options.Value.EnvioLigado || !JanelaDeEnvio.Aberta(agoraUtc))
            return 0;

        var vencidos = (await repositorio.ListarCandidatosDeTodasAsFormaturas(agoraUtc, ct))
            .Select(candidato => (Candidato: candidato, Jornada: candidato.JornadaVencida(agoraUtc)))
            .Where(par => par.Jornada is not null)
            .GroupBy(par => par.Candidato.UsuarioId)
            .Select(daPessoa => daPessoa.OrderBy(par => par.Candidato.TurmaCriadaEm).First())
            .ToList();

        var enfileirados = 0;

        foreach (var (candidato, jornada) in vencidos)
        {
            if ((await emails.Enfileirar(candidato, jornada!, agoraUtc, ct)).Falhou)
                continue;

            enfileirados++;

            await repositorio.Adicionar(
                new EnvioDeMarketing
                {
                    UsuarioId = candidato.UsuarioId,
                    FormaturaId = candidato.FormaturaId,
                    Jornada = jornada!,
                    EnviadoEm = agoraUtc,
                },
                ct
            );
        }

        if (enfileirados > 0)
            await unitOfWork.SalvarAsync(ct);

        return enfileirados;
    }
}
