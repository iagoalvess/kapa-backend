using Microsoft.AspNetCore.Identity;

namespace Backend.Business.Usuarios.Models;

/// <summary>
/// Perfil de acesso (role do Identity).
/// </summary>
public class Perfil : IdentityRole<Guid>
{
    /// <summary>Inicializa um perfil sem nome, exigido pelo Identity na materialização.</summary>
    public Perfil() => Id = Guid.CreateVersion7();

    /// <summary>Inicializa um perfil com o nome informado.</summary>
    /// <param name="nome">Nome do perfil.</param>
    public Perfil(string nome)
        : base(nome) => Id = Guid.CreateVersion7();
}

/// <summary>
/// Perfis criados pelo seed inicial.
/// </summary>
/// <remarks>
/// São constantes porque aparecem em <c>[Authorize(Roles = ...)]</c>, onde erro de digitação
/// não é erro de compilação — vira 403 em produção.
/// </remarks>
public static class PerfisPadrao
{
    /// <summary>Acesso total, incluindo gestão de usuários.</summary>
    public const string Administrador = "Administrador";

    /// <summary>Acesso comum da aplicação.</summary>
    public const string Usuario = "Usuario";

    /// <summary>Todos os perfis criados no seed.</summary>
    public static readonly IReadOnlyList<string> Todos = [Administrador, Usuario];
}
