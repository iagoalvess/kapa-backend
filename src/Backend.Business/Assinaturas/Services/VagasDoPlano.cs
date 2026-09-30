using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;

namespace Backend.Business.Assinaturas.Services;

/// <summary>
/// As vagas que o plano vigente dá à turma: quantas estão ocupadas, se ainda cabe mais uma e se ela pode ser
/// formando.
/// </summary>
/// <remarks>
/// Um lugar só para a conta, porque são duas as portas que a turma usa para crescer: o convite
/// (criar e aceitar) e a troca de plano — o religar de quem foi desligado era a terceira, e saiu em
/// 23/09/2026. Até 22/09/2026 só o convite conferia, e só para o papel Formando — a turma entrava
/// inteira como "Comissão", ou voltava pelo religar, e o gratuito virava o produto inteiro.
/// <para>
/// <b>Todo papel ocupa vaga</b>, sem folga para a comissão (decisão de 22/09/2026): o plano é
/// vendido por tamanho de turma, e qualquer membro adere ao termo e paga parcela. Quem foi
/// desligado ou removido não ocupa — saiu.
/// </para>
/// <para>
/// Limite negativo é plano mal cadastrado: não se tranca a turma por isso.
/// </para>
/// <para>
/// <b>Formando só com plano pago em vigor</b> (Sprint 45, P3). Até 29/09/2026 a pergunta era "a turma já
/// contratou alguma vez?" (<c>JaContratou</c>), e a turma que pagou um mês e parou seguia convidando formando
/// no teto do grátis. Agora vale o plano de hoje, o mesmo que o gate de módulo lê.
/// </para>
/// </remarks>
/// <param name="assinaturaRepository">O plano vigente, de onde vem o limite.</param>
/// <param name="vinculoRepository">Os vínculos ativos, que são as vagas ocupadas.</param>
public sealed class VagasDoPlano(IAssinaturaRepository assinaturaRepository, IVinculoRepository vinculoRepository)
{
    /// <summary>O mesmo código de antes da Sprint 45: para o front, "contrate para ter formandos" continua um caso só.</summary>
    private static readonly Erro SemPlanoPago = Erro.Proibido(
        "convite.formatura_nao_contratada",
        "Formandos entram só com um plano contratado em dia. Antes disso, dá para montar a comissão."
    );

    /// <summary>Quantas pessoas ocupam vaga: vínculo ativo e não desligado, de qualquer papel.</summary>
    /// <param name="formaturaId">Turma.</param>
    public async Task<int> Ocupadas(Guid formaturaId, CancellationToken ct = default) =>
        (await vinculoRepository.ContarMembros(formaturaId, ct)).Where(c => c is { Ativo: true, Desligado: false }).Sum(c => c.Quantidade);

    /// <summary>Recusa quem entraria como Formando numa turma sem plano pago em vigor.</summary>
    /// <remarks>
    /// Um lugar só para as duas portas por onde alguém vira formando: o convite (e o link da turma, que é convite
    /// de Formando) e a troca de papel de um membro. Antes da Sprint 45 a troca de papel não conferia nada.
    /// </remarks>
    /// <param name="formaturaId">Turma.</param>
    /// <param name="papel">Papel com que a pessoa ficaria.</param>
    /// <returns>O erro, ou <c>null</c> se pode.</returns>
    public async Task<Erro?> ConferirPapel(Guid formaturaId, string papel, CancellationToken ct = default)
    {
        if (
            papel != PapelNaFormatura.Formando
            || await assinaturaRepository.ObterPlanoVigenteDeTodasAsFormaturas(formaturaId, ct) is not { Pago: false }
        )
            return null;

        return SemPlanoPago;
    }

    /// <summary>Recusa a entrada de mais uma pessoa quando a turma já ocupou todas as vagas do plano.</summary>
    /// <remarks>
    /// Chame dentro da transação que grava o vínculo: a conferência enfileira as entradas da turma até
    /// o commit (<see cref="IVinculoRepository.TravarEntradas"/>), e a próxima conta já com quem entrou.
    /// </remarks>
    /// <param name="formaturaId">Turma que recebe.</param>
    /// <returns>O erro, ou <c>null</c> se ainda há vaga.</returns>
    public async Task<Erro?> ConferirEntrada(Guid formaturaId, CancellationToken ct = default)
    {
        await vinculoRepository.TravarEntradas(formaturaId, ct);

        if (await assinaturaRepository.ObterPlanoVigenteDeTodasAsFormaturas(formaturaId, ct) is not { LimiteDeFormandos: >= 0 } plano)
            return null;

        var ocupadas = await Ocupadas(formaturaId, ct);

        return ocupadas < plano.LimiteDeFormandos
            ? null
            : Erro.Conflito(
                "plano.limite_de_formandos",
                $"O plano {plano.Nome} comporta {plano.LimiteDeFormandos} pessoas e a turma já tem {ocupadas}. Troque de plano para incluir mais gente."
            );
    }
}
