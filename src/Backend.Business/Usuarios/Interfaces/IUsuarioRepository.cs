using Backend.Business.Abstractions;
using Backend.Business.Usuarios.Models;

namespace Backend.Business.Usuarios.Interfaces;

/// <summary>
/// Acesso a dados de usuário.
/// </summary>
/// <remarks>
/// Repositório específico do agregado, com métodos de intenção — não existe
/// <c>Repository&lt;TEntity&gt;</c> genérico neste projeto (ver <c>docs/padroes.md</c>).
/// <para>
/// Nenhum método aqui persiste. Quem faz commit é o service, via <see cref="IUnitOfWork"/>.
/// </para>
/// </remarks>
public interface IUsuarioRepository
{
    /// <summary>Lista usuários paginados, filtrando por nome ou e-mail.</summary>
    /// <param name="paginacao">Página e tamanho já normalizados.</param>
    /// <param name="busca">Termo livre aplicado a nome e e-mail. Nulo lista todos.</param>
    Task<PaginaDe<UsuarioResumo>> Listar(PaginacaoRequest paginacao, string? busca, CancellationToken ct = default);

    /// <summary>Obtém o detalhe de um usuário, incluindo seus perfis.</summary>
    /// <param name="id">Identificador do usuário.</param>
    /// <returns>O detalhe, ou nulo se não existir.</returns>
    Task<UsuarioDetalhe?> ObterDetalhe(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Obtém a entidade rastreada pelo contexto, para alteração.
    /// </summary>
    /// <remarks>
    /// Separado das consultas de leitura de propósito: as outras usam <c>AsNoTracking</c> e
    /// projetam direto no <c>SELECT</c>. Só quem vai escrever paga o custo do rastreamento.
    /// </remarks>
    /// <param name="id">Identificador do usuário.</param>
    Task<Usuario?> ObterParaEdicao(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Conta os administradores **ativos**.
    /// </summary>
    /// <remarks>
    /// Existe para uma proteção específica: impedir que a última conta de administrador seja
    /// desativada ou rebaixada. Sem ela, um clique deixa o sistema sem ninguém capaz de
    /// gerenciar usuários — e a recuperação exige acesso direto ao banco.
    /// </remarks>
    Task<int> ContarAdministradoresAtivos(CancellationToken ct = default);
}
