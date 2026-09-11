namespace Backend.Api.DTOs.Formaturas;

/// <summary>Formatura da qual o usuário participa.</summary>
/// <param name="Id">Identificador da formatura, usado em <c>POST /formaturas/{id}/selecionar</c>.</param>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Papel">Papel do usuário nesta formatura.</param>
public sealed record FormaturaDoUsuarioDTO(Guid Id, string Nome, string Papel);
