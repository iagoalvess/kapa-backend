using Backend.Business.Admin.Models;

namespace Backend.Business.Admin.Interfaces;

/// <summary>
/// Consultas agregadas do painel administrativo.
/// </summary>
public interface IAdminRepository
{
    /// <summary>Apura os números do painel em uma única ida ao banco.</summary>
    /// <param name="agoraUtc">Momento da apuração — o corte das sessões ativas.</param>
    /// <param name="cadastradosDesde">Início da janela dos cadastros recentes.</param>
    Task<ResumoAdmin> ObterResumo(DateTime agoraUtc, DateTime cadastradosDesde, CancellationToken ct = default);

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
    Task<IReadOnlyList<TurmaEncontrada>> BuscarTurmasDeTodasAsFormaturas(string termo, int limite, CancellationToken ct = default);

    /// <summary>Contas cujo nome ou e-mail batem com o termo, com em quantas turmas cada uma está.</summary>
    /// <param name="termo">Trecho digitado.</param>
    /// <param name="limite">Máximo de linhas.</param>
    Task<IReadOnlyList<UsuarioEncontrado>> BuscarUsuariosDeTodasAsFormaturas(string termo, int limite, CancellationToken ct = default);

    /// <summary>A turma inteira como o suporte a vê, ou nulo se não existir.</summary>
    /// <param name="formaturaId">Formatura.</param>
    Task<TurmaNoSuporte?> ObterTurmaDeTodasAsFormaturas(Guid formaturaId, CancellationToken ct = default);

    /// <summary>A conta inteira como o suporte a vê, ou nulo se não existir.</summary>
    /// <param name="usuarioId">Conta.</param>
    Task<UsuarioNoSuporte?> ObterUsuarioDeTodasAsFormaturas(Guid usuarioId, CancellationToken ct = default);
}
