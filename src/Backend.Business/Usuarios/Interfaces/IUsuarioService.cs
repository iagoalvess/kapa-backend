using Backend.Business.Abstractions;
using Backend.Business.Usuarios.Models;

namespace Backend.Business.Usuarios.Interfaces;

/// <summary>
/// Regras de gestão de usuários.
/// </summary>
public interface IUsuarioService
{
    /// <summary>Lista usuários paginados.</summary>
    /// <param name="paginacao">Página e tamanho solicitados; são normalizados internamente.</param>
    /// <param name="busca">Termo livre aplicado a nome e e-mail.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<PaginaDe<UsuarioResumo>>> Listar(PaginacaoRequest paginacao, string? busca, CancellationToken ct = default);

    /// <summary>Obtém um usuário pelo identificador.</summary>
    /// <param name="id">Identificador do usuário.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<UsuarioDetalhe>> ObterPorId(Guid id, CancellationToken ct = default);

    /// <summary>Altera os dados editáveis de um usuário.</summary>
    /// <param name="id">Identificador do usuário.</param>
    /// <param name="dados">Novos valores.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<UsuarioDetalhe>> Atualizar(Guid id, AtualizarUsuario dados, CancellationToken ct = default);

    /// <summary>Ativa ou desativa o acesso de um usuário.</summary>
    /// <param name="id">Identificador do usuário.</param>
    /// <param name="ativo">Novo estado de ativação.</param>
    /// <param name="idDoSolicitante">Usuário autenticado que pediu a mudança.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> AlterarAtivacao(Guid id, bool ativo, Guid idDoSolicitante, CancellationToken ct = default);

    /// <summary>Substitui os perfis de acesso de um usuário.</summary>
    /// <param name="id">Identificador do usuário.</param>
    /// <param name="dados">Novo conjunto de perfis.</param>
    /// <param name="idDoSolicitante">Usuário autenticado que pediu a mudança.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<UsuarioDetalhe>> AlterarPerfis(Guid id, AlterarPerfis dados, Guid idDoSolicitante, CancellationToken ct = default);
}
