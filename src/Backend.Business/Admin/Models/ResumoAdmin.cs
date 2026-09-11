namespace Backend.Business.Admin.Models;

/// <summary>
/// Números que alimentam o painel administrativo.
/// </summary>
/// <remarks>
/// Deliberadamente pequeno e genérico: é o que existe em **qualquer** aplicação. Métrica de
/// domínio (pedidos, vendas, locações) entra na feature dela, não aqui — este resumo é o que o
/// template entrega pronto no dia zero para a tela do admin não nascer vazia.
/// </remarks>
/// <param name="UsuariosTotal">Contas cadastradas, ativas ou não.</param>
/// <param name="UsuariosAtivos">Contas que podem autenticar.</param>
/// <param name="UsuariosInativos">Contas desativadas.</param>
/// <param name="Administradores">Contas ativas com o perfil de administrador.</param>
/// <param name="SessoesAtivas">Refresh tokens válidos neste momento — sessões abertas.</param>
/// <param name="CadastrosUltimos30Dias">Contas criadas nos últimos 30 dias.</param>
/// <param name="GeradoEm">Momento da apuração, em UTC.</param>
public sealed record ResumoAdmin(
    long UsuariosTotal,
    long UsuariosAtivos,
    long UsuariosInativos,
    long Administradores,
    long SessoesAtivas,
    long CadastrosUltimos30Dias,
    DateTime GeradoEm
);
