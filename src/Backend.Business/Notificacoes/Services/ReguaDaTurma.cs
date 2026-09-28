using Backend.Business.Abstractions;
using Backend.Business.Notificacoes.Interfaces;
using Backend.Business.Notificacoes.Models;

namespace Backend.Business.Notificacoes.Services;

/// <summary>
/// A régua vigente da turma: os degraus de <see cref="ReguaDoKapa"/>, cada um ligado ou não.
/// </summary>
/// <remarks>
/// Decisão 5: exigir configuração antes de funcionar significa que metade das turmas nunca terá
/// lembrete. O degrau que falta é criado ligado na primeira leitura (a tela da régua ou a rodada do
/// job), e não na criação da formatura, porque as turmas que já existem também precisam dele — e
/// porque um degrau novo no catálogo chega a todas sem script.
/// <para>
/// Fora dos dois services porque os dois precisam do mesmo comportamento: a tela mostra o que o job
/// vai usar, e não uma cópia parecida.
/// </para>
/// </remarks>
internal static class ReguaDaTurma
{
    /// <summary>Os degraus da turma, criando os que faltam e deixando de fora os que saíram do catálogo.</summary>
    /// <param name="repositorio">Régua da formatura selecionada.</param>
    /// <param name="unitOfWork">Fronteira transacional.</param>
    public static async Task<IReadOnlyList<RegraResumo>> Garantir(
        INotificacaoRepository repositorio,
        IUnitOfWork unitOfWork,
        CancellationToken ct = default
    )
    {
        var regras = await repositorio.ListarRegras(ct);

        var faltam = ReguaDoKapa
            .Degraus.Where(degrau => !regras.Any(r => r.Gatilho == degrau.Gatilho && r.DiasDeDeslocamento == degrau.DiasDeDeslocamento))
            .Select(RegraDeNotificacao.Nova)
            .ToList();

        if (faltam.Count > 0)
        {
            await repositorio.AdicionarRegras(faltam, ct);
            await unitOfWork.SalvarAsync(ct);
            regras = await repositorio.ListarRegras(ct);
        }

        return [.. regras.Where(r => ReguaDoKapa.De(r.Gatilho, r.DiasDeDeslocamento) is not null)];
    }
}
