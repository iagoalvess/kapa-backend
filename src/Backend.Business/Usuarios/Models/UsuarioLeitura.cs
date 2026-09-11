namespace Backend.Business.Usuarios.Models;

/// <summary>
/// Usuário como aparece em listagem.
/// </summary>
/// <remarks>
/// Modelo de leitura: projetado direto no <c>SELECT</c>, sem materializar a entidade. Listagem
/// não carrega hash de senha, carimbo de segurança nem coleção de claims — dado que a tela não
/// usa é dado que não deve trafegar.
/// </remarks>
/// <param name="Id">Identificador do usuário.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">Endereço de e-mail.</param>
/// <param name="Ativo">Indica se o usuário pode autenticar.</param>
/// <param name="CriadoEm">Momento de criação, em UTC.</param>
public sealed record UsuarioResumo(Guid Id, string Nome, string Email, bool Ativo, DateTime CriadoEm);

/// <summary>
/// Usuário como aparece na tela de detalhe.
/// </summary>
/// <param name="Id">Identificador do usuário.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">Endereço de e-mail.</param>
/// <param name="EmailConfirmado">Indica se o e-mail foi confirmado.</param>
/// <param name="Ativo">Indica se o usuário pode autenticar.</param>
/// <param name="Perfis">Perfis de acesso atribuídos.</param>
/// <param name="CriadoEm">Momento de criação, em UTC.</param>
/// <param name="AtualizadoEm">Momento da última alteração, em UTC.</param>
public sealed record UsuarioDetalhe(
    Guid Id,
    string Nome,
    string Email,
    bool EmailConfirmado,
    bool Ativo,
    IReadOnlyList<string> Perfis,
    DateTime CriadoEm,
    DateTime AtualizadoEm
);

/// <summary>
/// Dados aceitos na alteração de um usuário.
/// </summary>
/// <param name="Nome">Novo nome de exibição.</param>
public sealed record AtualizarUsuario(string Nome);
