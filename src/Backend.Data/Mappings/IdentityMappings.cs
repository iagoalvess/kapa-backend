using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Nomes das tabelas auxiliares do Identity.
/// </summary>
/// <remarks>
/// Sem isto elas nascem como <c>AspNetUserClaims</c>, <c>AspNetUserTokens</c> e companhia,
/// destoando do resto do banco em <c>snake_case</c>. Quem for depurar em <c>psql</c> teria que
/// lembrar de aspear metade das tabelas e a outra metade não.
/// </remarks>
public sealed class UsuarioClaimMapping : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder) => builder.ToTable("usuarios_claims");
}

/// <summary>Nome da tabela de logins externos.</summary>
public sealed class UsuarioLoginMapping : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder) => builder.ToTable("usuarios_logins");
}

/// <summary>Nome da tabela de tokens do Identity (confirmação de e-mail, 2FA, reset de senha).</summary>
public sealed class UsuarioTokenMapping : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder) => builder.ToTable("usuarios_tokens");
}

/// <summary>Nome da tabela de vínculo entre usuário e perfil.</summary>
public sealed class UsuarioPerfilMapping : IEntityTypeConfiguration<IdentityUserRole<Guid>>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> builder) => builder.ToTable("usuarios_perfis");
}

/// <summary>Nome da tabela de claims de perfil.</summary>
public sealed class PerfilClaimMapping : IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> builder) => builder.ToTable("perfis_claims");
}
