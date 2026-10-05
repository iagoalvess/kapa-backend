using Backend.Business.Abstractions;
using Backend.Business.Formaturas.Models;

namespace Backend.Business.Formaturas.Interfaces;

/// <summary>
/// Consultas sobre o vínculo entre usuário e formatura.
/// </summary>
public interface IVinculoRepository
{
    /// <summary>
    /// Formaturas em que o usuário tem vínculo ativo <b>ou desligado</b>, com o papel dele em cada uma.
    /// </summary>
    /// <remarks>
    /// O desligado vem junto porque a turma continua no seletor dele, em leitura: o extrato é a prova
    /// do que ele pagou (P5 de 17/09/2026). Quem foi <b>removido</b> não vem — nunca aderiu, nunca
    /// deveu, e não tem histórico a consultar.
    /// </remarks>
    /// <param name="usuarioId">Usuário autenticado.</param>
    Task<IReadOnlyList<FormaturaDoUsuario>> ListarDoUsuario(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// O vínculo ativo ou desligado do usuário, <b>se</b> ele tiver exatamente um.
    /// </summary>
    /// <remarks>
    /// Nulo tanto para nenhum quanto para dois ou mais: os dois casos precisam de uma decisão
    /// que não é do sistema — criar a primeira formatura, ou escolher entre as que existem.
    /// <para>
    /// O desligado entra na contagem pelo mesmo motivo de <see cref="ListarDoUsuario"/>: sem a claim
    /// <c>formatura_id</c> ele não chegaria nem ao próprio extrato.
    /// </para>
    /// </remarks>
    /// <param name="usuarioId">Usuário autenticado.</param>
    Task<VinculoAtivo?> ObterUnicoAtivo(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Papel do usuário na formatura, ou nulo se não houver vínculo ativo.</summary>
    /// <param name="usuarioId">Usuário autenticado.</param>
    /// <param name="formaturaId">Formatura pretendida.</param>
    Task<string?> ObterPapelAtivo(Guid usuarioId, Guid formaturaId, CancellationToken ct = default);

    /// <summary>
    /// Se o usuário é formando ativo da turma, a turma já publicou o termo e ele ainda não aderiu — quem o gate de
    /// adesão barra (Sprint 47, D18).
    /// </summary>
    /// <param name="usuarioId">Usuário autenticado.</param>
    /// <param name="formaturaId">Formatura da sessão.</param>
    Task<bool> FormandoSemAdesao(Guid usuarioId, Guid formaturaId, CancellationToken ct = default);

    /// <summary>
    /// O vínculo do usuário na formatura aceitando o <b>desligado</b>; nulo para quem foi removido ou
    /// nunca pertenceu a ela.
    /// </summary>
    /// <remarks>
    /// A emissão de sessão e a leitura do próprio histórico (P5) passam por aqui — <b>e mais nada</b>.
    /// Toda política de papel continua em <see cref="ObterPapelAtivo"/>: quem saiu não opera a turma.
    /// </remarks>
    /// <param name="usuarioId">Usuário autenticado.</param>
    /// <param name="formaturaId">Formatura pretendida.</param>
    Task<VinculoAtivo?> ObterDoTitular(Guid usuarioId, Guid formaturaId, CancellationToken ct = default);

    /// <summary>Uma página dos vínculos da formatura, ativos primeiro, com nome e e-mail do usuário.</summary>
    /// <param name="formaturaId">Formatura consultada.</param>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="filtro">Busca e situação.</param>
    Task<PaginaDe<MembroDaFormatura>> ListarMembros(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        FiltroDeMembros filtro,
        CancellationToken ct = default
    );

    /// <summary>Quantos vínculos a formatura tem em cada papel e situação. Combinação vazia não vem.</summary>
    /// <param name="formaturaId">Formatura consultada.</param>
    Task<IReadOnlyList<ContagemDeMembros>> ContarMembros(Guid formaturaId, CancellationToken ct = default);

    /// <summary>
    /// Enfileira quem entra na formatura até o fim da transação corrente.
    /// </summary>
    /// <remarks>
    /// Contar as vagas e gravar o vínculo são dois passos: sem a fila, entradas simultâneas por convites
    /// diferentes contavam a mesma turma e passavam juntas do limite do plano. Fora de transação não
    /// segura nada — quem chama já está dentro de uma.
    /// </remarks>
    /// <param name="formaturaId">Formatura que vai receber gente.</param>
    Task TravarEntradas(Guid formaturaId, CancellationToken ct = default);

    /// <summary>O vínculo ativo do usuário na formatura, rastreado para alteração.</summary>
    /// <param name="usuarioId">Membro.</param>
    /// <param name="formaturaId">Formatura.</param>
    Task<VinculoDeFormatura?> ObterAtivoParaEdicao(Guid usuarioId, Guid formaturaId, CancellationToken ct = default);

    /// <summary>Todos os vínculos ativos da formatura, rastreados para alteração.</summary>
    /// <remarks>Só para o descarte de rascunho, que tem a comissão e mais ninguém: poucas linhas.</remarks>
    /// <param name="formaturaId">Formatura.</param>
    Task<IReadOnlyList<VinculoDeFormatura>> ListarAtivosParaEdicao(Guid formaturaId, CancellationToken ct = default);

    /// <summary>O vínculo do usuário na formatura, ativo ou não, rastreado para alteração.</summary>
    /// <remarks>É o que o aceite de convite reativa, em vez de criar uma segunda linha para quem foi removido e voltou.</remarks>
    /// <param name="usuarioId">Usuário.</param>
    /// <param name="formaturaId">Formatura.</param>
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
    Task<int> TravarPresidentesAtivos(Guid formaturaId, CancellationToken ct = default);

    /// <summary>E-mails dos presidentes ativos da formatura — os destinatários dos avisos de assinatura.</summary>
    /// <param name="formaturaId">Formatura.</param>
    Task<IReadOnlyList<string>> ListarEmailsDosPresidentes(Guid formaturaId, CancellationToken ct = default);

    /// <summary>E-mails dos membros ativos da comissão — Presidente, Tesoureiro e Comissão; formando não.</summary>
    /// <param name="formaturaId">Formatura.</param>
    Task<IReadOnlyList<string>> ListarEmailsDaComissao(Guid formaturaId, CancellationToken ct = default);

    /// <summary>E-mails dos formandos ativos — a turma, sem a comissão, que recebe o aviso dela.</summary>
    /// <param name="formaturaId">Formatura.</param>
    Task<IReadOnlyList<string>> ListarEmailsDosFormandos(Guid formaturaId, CancellationToken ct = default);

    /// <summary>E-mails de quem responde pelo caixa — Presidente e Tesoureiro; a Comissão não confere pagamento.</summary>
    /// <remarks>É o destinatário dos resumos da régua: informe parado e parcela vencida há trinta dias.</remarks>
    /// <param name="formaturaId">Formatura.</param>
    Task<IReadOnlyList<string>> ListarEmailsDaTesouraria(Guid formaturaId, CancellationToken ct = default);

    /// <summary>O e-mail da conta de cada vínculo — o destino dos avisos de pagamento.</summary>
    /// <remarks>Os ids vêm de linhas já isoladas pela formatura (parcela, informe), então não levam a turma.</remarks>
    /// <param name="vinculoIds">Vínculos.</param>
    Task<IReadOnlyDictionary<Guid, string>> ListarEmailsDosVinculos(IReadOnlyCollection<Guid> vinculoIds, CancellationToken ct = default);

    /// <summary>Registra um vínculo novo.</summary>
    /// <param name="vinculo">Vínculo a persistir.</param>
    Task Adicionar(VinculoDeFormatura vinculo, CancellationToken ct = default);
}
