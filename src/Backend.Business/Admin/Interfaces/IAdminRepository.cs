using Backend.Business.Admin.Models;

namespace Backend.Business.Admin.Interfaces;

/// <summary>
/// Consultas agregadas do painel administrativo.
/// </summary>
public interface IAdminRepository
{
    /// <summary>Apura os números do painel em uma única ida ao banco.</summary>
    Task<ResumoAdmin> ObterResumo(CancellationToken ct = default);

    /// <summary>
    /// Conta os administradores **ativos**.
    /// </summary>
    /// <remarks>
    /// Existe para uma proteção específica: impedir que a última conta de administrador seja
    /// desativada ou rebaixada. Sem ela, um clique deixa o sistema sem ninguém capaz de
    /// gerenciar usuários — e a recuperação exige acesso direto ao banco.
    /// </remarks>
    Task<int> ContarAdministradoresAtivos(CancellationToken ct = default);

    /// <summary>
    /// Turmas cujo nome, instituição ou curso batem com o termo.
    /// </summary>
    /// <remarks>
    /// Atravessa todas as formaturas: quem atende é perfil de plataforma e não tem turma na sessão.
    /// O filtro global do <c>AppDbContext</c> não alcança <c>Formatura</c>, que é a raiz — mas
    /// alcança tudo o que pende dela, e por isso as consultas daqui que passam por vínculo,
    /// parcela ou adesão precisam ignorá-lo explicitamente.
    /// </remarks>
    /// <param name="termo">Trecho digitado.</param>
    /// <param name="limite">Máximo de linhas.</param>
    Task<IReadOnlyList<TurmaEncontrada>> BuscarTurmas(string termo, int limite, CancellationToken ct = default);

    /// <summary>Contas cujo nome ou e-mail batem com o termo, com em quantas turmas cada uma está.</summary>
    /// <param name="termo">Trecho digitado.</param>
    /// <param name="limite">Máximo de linhas.</param>
    Task<IReadOnlyList<UsuarioEncontrado>> BuscarUsuarios(string termo, int limite, CancellationToken ct = default);

    /// <summary>A turma inteira como o suporte a vê, ou nulo se não existir.</summary>
    /// <param name="formaturaId">Formatura.</param>
    Task<TurmaNoSuporte?> ObterTurma(Guid formaturaId, CancellationToken ct = default);

    /// <summary>A conta inteira como o suporte a vê, ou nulo se não existir.</summary>
    /// <param name="usuarioId">Conta.</param>
    Task<UsuarioNoSuporte?> ObterUsuario(Guid usuarioId, CancellationToken ct = default);
}
