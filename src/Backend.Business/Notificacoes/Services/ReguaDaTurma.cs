using Backend.Business.Abstractions;
using Backend.Business.Notificacoes.Interfaces;
using Backend.Business.Notificacoes.Models;

namespace Backend.Business.Notificacoes.Services;

/// <summary>
/// A régua vigente da turma — a gravada, ou a padrão materializada na primeira vez.
/// </summary>
/// <remarks>
/// Decisão 5: exigir configuração antes de funcionar significa que metade das turmas nunca terá
/// lembrete. A materialização acontece na primeira leitura (a tela da régua ou a primeira rodada do
/// job), e não na criação da formatura, porque as turmas que já existem também precisam dela.
/// <para>
/// Fora dos dois services porque os dois precisam do mesmo comportamento: a tela mostra o que o job
/// vai usar, e não uma cópia parecida.
/// </para>
/// </remarks>
internal static class ReguaDaTurma
{
    /// <summary>Os degraus da turma, criando a régua padrão se ela ainda não tiver nenhum.</summary>
    /// <param name="repositorio">Régua da formatura selecionada.</param>
    /// <param name="unitOfWork">Fronteira transacional.</param>
    public static async Task<IReadOnlyList<RegraResumo>> Garantir(
        INotificacaoRepository repositorio,
        IUnitOfWork unitOfWork,
        CancellationToken ct = default
    )
    {
        var regras = await repositorio.ListarRegras(ct);

        if (regras.Count > 0)
            return regras;

        await repositorio.AdicionarRegras(RegraDeNotificacao.Padrao(), ct);
        await unitOfWork.SalvarAsync(ct);

        return await repositorio.ListarRegras(ct);
    }
}
