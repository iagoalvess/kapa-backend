using Backend.Business.Abstractions;
using Backend.Business.Auth.Models;
using Backend.Business.Formaturas.Models;

namespace Backend.Business.Formaturas.Interfaces;

/// <summary>
/// Criação, seleção e ciclo de vida da formatura.
/// </summary>
/// <remarks>
/// Listar, selecionar e criar funcionam <b>sem</b> formatura selecionada: são justamente o caminho
/// para ter uma. As demais operações recebem o id da formatura da sessão.
/// </remarks>
public interface IFormaturaService
{
    /// <summary>Formaturas em que o usuário tem vínculo ativo.</summary>
    /// <param name="usuarioId">Usuário autenticado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<IReadOnlyList<FormaturaDoUsuario>>> ListarMinhas(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Troca a formatura da sessão, devolvendo um par de tokens novo.
    /// </summary>
    /// <remarks>
    /// A formatura entra no token assinado pela API, que só põe ali uma turma cujo vínculo
    /// acabou de ser verificado.
    /// </remarks>
    /// <param name="usuarioId">Usuário autenticado.</param>
    /// <param name="formaturaId">Formatura pretendida.</param>
    /// <param name="refreshTokenAtual">Refresh token da sessão atual, rotacionado na troca.</param>
    /// <param name="ipDeOrigem">IP do solicitante, registrado para auditoria.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<ParDeTokens>> Selecionar(
        Guid usuarioId,
        Guid formaturaId,
        string refreshTokenAtual,
        string? ipDeOrigem,
        CancellationToken ct = default
    );

    /// <summary>
    /// Cria a formatura em rascunho, com o criador como Presidente, e devolve a sessão já dentro dela.
    /// </summary>
    /// <param name="usuarioId">Criador, que vira Presidente.</param>
    /// <param name="dados">Dados cadastrais.</param>
    /// <param name="refreshTokenAtual">Refresh token da sessão atual, rotacionado na criação.</param>
    /// <param name="ipDeOrigem">IP do solicitante, registrado para auditoria.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<ParDeTokens>> Criar(
        Guid usuarioId,
        DadosDaFormatura dados,
        string refreshTokenAtual,
        string? ipDeOrigem,
        CancellationToken ct = default
    );

    /// <summary>Detalhe da formatura da sessão.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<FormaturaDetalhe>> ObterAtual(Guid formaturaId, CancellationToken ct = default);

    /// <summary>Edita os dados cadastrais da formatura da sessão.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="dados">Dados novos.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<FormaturaDetalhe>> Atualizar(Guid formaturaId, DadosDaFormatura dados, CancellationToken ct = default);

    /// <summary>Encerra a formatura da sessão.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> Encerrar(Guid formaturaId, CancellationToken ct = default);

    /// <summary>
    /// Desiste de um rascunho que nunca foi pago: a turma vai para <c>Descartada</c> e some da
    /// lista de todos os membros.
    /// </summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> Descartar(Guid formaturaId, CancellationToken ct = default);
}
