namespace Backend.Api.DTOs.Usuarios;

/// <summary>Usuário como aparece em listagem.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Ativo">Indica se pode autenticar.</param>
/// <param name="CriadoEm">Criação, em UTC (ISO 8601).</param>
public sealed record UsuarioResumoDTO(Guid Id, string Nome, string Email, bool Ativo, DateTime CriadoEm);

/// <summary>Usuário com todos os campos expostos pela API.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail.</param>
/// <param name="EmailConfirmado">Indica se o e-mail foi confirmado.</param>
/// <param name="Ativo">Indica se pode autenticar.</param>
/// <param name="Perfis">Perfis de acesso.</param>
/// <param name="CriadoEm">Criação, em UTC (ISO 8601).</param>
/// <param name="AtualizadoEm">Última alteração, em UTC (ISO 8601).</param>
public sealed record UsuarioDetalheDTO(
    Guid Id,
    string Nome,
    string Email,
    bool EmailConfirmado,
    bool Ativo,
    IReadOnlyList<string> Perfis,
    DateTime CriadoEm,
    DateTime AtualizadoEm
);

/// <summary>Corpo do pedido de alteração de dados do usuário.</summary>
/// <param name="Nome">Novo nome de exibição.</param>
public sealed record AtualizarUsuarioRequestDTO(string Nome);

/// <summary>Corpo do pedido de ativação ou desativação.</summary>
/// <param name="Ativo">Novo estado de ativação.</param>
public sealed record AlterarAtivacaoRequestDTO(bool Ativo);

/// <summary>
/// Corpo do pedido de troca de perfis.
/// </summary>
/// <param name="Perfis">Conjunto completo de perfis que o usuário passa a ter — substitui os atuais.</param>
public sealed record AlterarPerfisRequestDTO(IReadOnlyList<string> Perfis);
