using Backend.Business.Formaturas.Models;

namespace Backend.Business.Formaturas.Interfaces;

/// <summary>
/// Consultas e escrita da formatura em si.
/// </summary>
/// <remarks>
/// Recebe o id explícito, e não lê da sessão: <see cref="Formatura"/> é a raiz do isolamento e
/// fica fora do filtro global. Quem garante que o id é da sessão é o controller, que o tira da claim.
/// </remarks>
public interface IFormaturaRepository
{
    /// <summary>Detalhe da formatura, sem rastreamento.</summary>
    /// <param name="formaturaId">Formatura consultada.</param>
    Task<FormaturaDetalhe?> ObterDetalheDeTodasAsFormaturas(Guid formaturaId, CancellationToken ct = default);

    /// <summary>Só o nome da formatura, ou nulo se ela não existir.</summary>
    /// <remarks>
    /// Para o assunto e o corpo de e-mail, que só precisam do nome: o detalhe traz as datas da agenda
    /// e a contratação por subconsulta, três idas a mais por mensagem.
    /// </remarks>
    /// <param name="formaturaId">Formatura consultada.</param>
    Task<string?> ObterNome(Guid formaturaId, CancellationToken ct = default);

    /// <summary>Status atual, ou nulo se a formatura não existir. É a consulta da política de escrita.</summary>
    /// <param name="formaturaId">Formatura consultada.</param>
    Task<StatusDaFormatura?> ObterStatus(Guid formaturaId, CancellationToken ct = default);

    /// <summary>A formatura, rastreada para alteração.</summary>
    /// <param name="formaturaId">Formatura a alterar.</param>
    Task<Formatura?> ObterParaEdicao(Guid formaturaId, CancellationToken ct = default);

    /// <summary>
    /// Se o usuário já criou uma turma que nunca contratou — a dele no plano gratuito.
    /// </summary>
    /// <remarks>
    /// Substituiu "um rascunho por usuário" em 18/09/2026, quando a turma passou a nascer ativa no
    /// gratuito: não existindo mais rascunho, a regra tinha de mudar de eixo para continuar dizendo
    /// a mesma coisa — <b>uma turma não paga por conta</b>.
    /// <para>
    /// <c>ponytail:</c> a checagem é só do service, então dois cliques simultâneos ainda criam duas.
    /// A versão antiga tinha índice único parcial cobrindo a corrida, e este eixo não cabe num
    /// índice — ele depende de <c>assinaturas</c>. Se virar problema real, o caminho é uma coluna
    /// em <c>formaturas</c> dizendo que a turma nunca contratou, aí o índice parcial volta.
    /// </para>
    /// </remarks>
    /// <param name="usuarioId">Criador.</param>
    Task<bool> ExisteGratuitaCriadaPorDeTodasAsFormaturas(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Registra uma formatura nova.</summary>
    /// <param name="formatura">Formatura a persistir.</param>
    Task Adicionar(Formatura formatura, CancellationToken ct = default);
}
