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
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<PaginaDe<MembroDaFormatura>>> Listar(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        FiltroDeMembros filtro,
        CancellationToken ct = default
    );

    /// <summary>Quantos membros a formatura tem em cada papel e situação.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<IReadOnlyList<ContagemDeMembros>>> Contar(Guid formaturaId, CancellationToken ct = default);

    /// <summary>Troca o papel de um membro ativo.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Membro a alterar.</param>
    /// <param name="dados">Papel novo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> AlterarPapel(Guid formaturaId, Guid usuarioId, AlterarPapel dados, CancellationToken ct = default);

    /// <summary>Desativa o vínculo de um membro, preservando o histórico.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Membro a remover.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> Remover(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);
}
