namespace Backend.Api.DTOs.Admin;

/// <summary>Números do painel administrativo.</summary>
/// <param name="UsuariosTotal">Contas cadastradas.</param>
/// <param name="UsuariosAtivos">Contas que podem autenticar.</param>
/// <param name="UsuariosInativos">Contas desativadas.</param>
/// <param name="Administradores">Contas ativas com perfil de administrador.</param>
/// <param name="SessoesAtivas">Sessões abertas neste momento.</param>
/// <param name="CadastrosUltimos30Dias">Contas criadas nos últimos 30 dias.</param>
/// <param name="GeradoEm">Momento da apuração, em UTC (ISO 8601).</param>
public sealed record ResumoAdminDTO(
    long UsuariosTotal,
    long UsuariosAtivos,
    long UsuariosInativos,
    long Administradores,
    long SessoesAtivas,
    long CadastrosUltimos30Dias,
    DateTime GeradoEm
);
