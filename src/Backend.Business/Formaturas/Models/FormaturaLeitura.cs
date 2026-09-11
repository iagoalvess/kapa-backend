namespace Backend.Business.Formaturas.Models;

/// <summary>Formatura da qual o usuário participa, com o papel dele.</summary>
/// <param name="Id">Identificador da formatura.</param>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Papel">Papel do usuário nesta formatura.</param>
public sealed record FormaturaDoUsuario(Guid Id, string Nome, string Papel);

/// <summary>Vínculo ativo do usuário, do jeito que a emissão de sessão precisa dele.</summary>
/// <param name="FormaturaId">Formatura à qual o vínculo pertence.</param>
/// <param name="Papel">Papel do usuário nela.</param>
public sealed record VinculoAtivo(Guid FormaturaId, string Papel);
