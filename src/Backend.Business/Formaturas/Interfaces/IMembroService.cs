using Backend.Business.Abstractions;
using Backend.Business.Formaturas.Models;

namespace Backend.Business.Formaturas.Interfaces;

/// <summary>
/// Gestão dos membros da formatura selecionada.
/// </summary>
/// <remarks>
/// Quem pode chamar cada operação é decidido pela política do endpoint; aqui ficam só as regras
/// que dependem do estado da turma.
/// </remarks>
public interface IMembroService
{
    /// <summary>Uma página dos vínculos da formatura, com papel e situação.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="paginacao">Página pedida; o teto é aplicado aqui.</param>
    /// <param name="filtro">Busca por nome ou e-mail e situação do vínculo.</param>
    Task<Result<PaginaDe<MembroDaFormatura>>> Listar(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        FiltroDeMembros filtro,
        CancellationToken ct = default
    );

    /// <summary>Quantos membros a formatura tem em cada papel e situação.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    Task<Result<IReadOnlyList<ContagemDeMembros>>> Contar(Guid formaturaId, CancellationToken ct = default);

    /// <summary>Troca o papel de um membro ativo. Para Presidente, só pede: o link vai ao e-mail de quem pediu.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Membro a alterar.</param>
    /// <param name="dados">Papel novo.</param>
    /// <param name="autorId">Quem está trocando — vai na trilha de auditoria (Sprint 14).</param>
    Task<Result<AlteracaoDePapel>> AlterarPapel(Guid formaturaId, Guid usuarioId, AlterarPapel dados, Guid autorId, CancellationToken ct = default);

    /// <summary>Aplica a promoção a Presidente pedida em <see cref="AlterarPapel"/>, pelo link do e-mail.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="autorId">Quem confirma — tem de ser quem pediu.</param>
    /// <param name="token">O token do link.</param>
    Task<Result> ConfirmarPresidente(Guid formaturaId, Guid autorId, string? token, CancellationToken ct = default);

    /// <summary>Desativa o vínculo de um membro, preservando o histórico.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Membro a remover.</param>
    /// <param name="autorId">Quem está removendo — vai na trilha de auditoria (Sprint 14).</param>
    Task<Result> Remover(Guid formaturaId, Guid usuarioId, Guid autorId, CancellationToken ct = default);

    /// <summary>
    /// O que o desligamento vai mexer: quanto a pessoa já pagou, quanto deve e quanto está em atraso.
    /// </summary>
    /// <remarks>Alimenta o diálogo de confirmação. Desligar sem ver estes números é assinar em branco.</remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Membro que sairia.</param>
    Task<Result<ResumoDaSaida>> ResumirSaida(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Desliga um formando: inativa o vínculo, cancela o que ele ainda deve e avisa quem precisa saber.
    /// </summary>
    /// <remarks>
    /// Idempotente: desligar de novo não cancela mais nada e não manda o segundo e-mail. Quem nunca
    /// aderiu é recusado com <c>formatura.membro_sem_adesao</c> — para ele a ação é Remover.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Membro a desligar.</param>
    /// <param name="dados">Motivo e o que fazer com o atraso.</param>
    /// <param name="autorId">Quem desligou — vai para a auditoria.</param>
    Task<Result> Desligar(Guid formaturaId, Guid usuarioId, DesligarFormando dados, Guid autorId, CancellationToken ct = default);
}
