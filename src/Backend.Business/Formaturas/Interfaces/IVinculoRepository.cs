using Backend.Business.Abstractions;
using Backend.Business.Formaturas.Models;

namespace Backend.Business.Formaturas.Interfaces;

/// <summary>
/// Consultas sobre o vínculo entre usuário e formatura.
/// </summary>
public interface IVinculoRepository
{
    /// <summary>Formaturas em que o usuário tem vínculo ativo, com o papel dele em cada uma.</summary>
    /// <param name="usuarioId">Usuário autenticado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<IReadOnlyList<FormaturaDoUsuario>> ListarDoUsuario(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// O vínculo ativo do usuário, <b>se</b> ele tiver exatamente um.
    /// </summary>
    /// <remarks>
    /// Nulo tanto para nenhum quanto para dois ou mais: os dois casos precisam de uma decisão
    /// que não é do sistema — criar a primeira formatura, ou escolher entre as que existem.
    /// </remarks>
    /// <param name="usuarioId">Usuário autenticado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<VinculoAtivo?> ObterUnicoAtivo(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Papel do usuário na formatura, ou nulo se não houver vínculo ativo.</summary>
    /// <param name="usuarioId">Usuário autenticado.</param>
    /// <param name="formaturaId">Formatura pretendida.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<string?> ObterPapelAtivo(Guid usuarioId, Guid formaturaId, CancellationToken ct = default);

    /// <summary>Uma página dos vínculos da formatura, ativos primeiro, com nome e e-mail do usuário.</summary>
    /// <param name="formaturaId">Formatura consultada.</param>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="filtro">Busca e situação.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<PaginaDe<MembroDaFormatura>> ListarMembros(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        FiltroDeMembros filtro,
        CancellationToken ct = default
    );

    /// <summary>Quantos vínculos a formatura tem em cada papel e situação. Combinação vazia não vem.</summary>
    /// <param name="formaturaId">Formatura consultada.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<IReadOnlyList<ContagemDeMembros>> ContarMembros(Guid formaturaId, CancellationToken ct = default);

    /// <summary>O vínculo ativo do usuário na formatura, rastreado para alteração.</summary>
    /// <param name="usuarioId">Membro.</param>
    /// <param name="formaturaId">Formatura.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<VinculoDeFormatura?> ObterAtivoParaEdicao(Guid usuarioId, Guid formaturaId, CancellationToken ct = default);

    /// <summary>Todos os vínculos ativos da formatura, rastreados para alteração.</summary>
    /// <remarks>Só para o descarte de rascunho, que tem a comissão e mais ninguém: poucas linhas.</remarks>
    /// <param name="formaturaId">Formatura.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<IReadOnlyList<VinculoDeFormatura>> ListarAtivosParaEdicao(Guid formaturaId, CancellationToken ct = default);

    /// <summary>O vínculo do usuário na formatura, ativo ou não, rastreado para alteração.</summary>
    /// <remarks>É o que o aceite de convite reativa, em vez de criar uma segunda linha para quem foi removido e voltou.</remarks>
    /// <param name="usuarioId">Usuário.</param>
    /// <param name="formaturaId">Formatura.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<VinculoDeFormatura?> ObterParaEdicao(Guid usuarioId, Guid formaturaId, CancellationToken ct = default);

    /// <summary>
    /// Trava os vínculos de presidente ativos da formatura até o fim da transação e devolve quantos são.
    /// </summary>
    /// <remarks>
    /// <c>SELECT … FOR UPDATE</c>: precisa rodar dentro de <c>IUnitOfWork.EmTransacaoAsync</c>, senão
    /// a trava cai no mesmo instante. É o que impede dois presidentes de rebaixarem um ao outro ao
    /// mesmo tempo — o segundo espera o primeiro terminar e reconta já sem ele.
    /// </remarks>
    /// <param name="formaturaId">Formatura.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<int> TravarPresidentesAtivos(Guid formaturaId, CancellationToken ct = default);

    /// <summary>E-mails dos presidentes ativos da formatura — os destinatários dos avisos de assinatura.</summary>
    /// <param name="formaturaId">Formatura.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<IReadOnlyList<string>> ListarEmailsDosPresidentes(Guid formaturaId, CancellationToken ct = default);

    /// <summary>Registra um vínculo novo.</summary>
    /// <param name="vinculo">Vínculo a persistir.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task Adicionar(VinculoDeFormatura vinculo, CancellationToken ct = default);
}
