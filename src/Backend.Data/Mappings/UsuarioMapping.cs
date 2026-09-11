using Backend.Business.Usuarios.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento da entidade de usuário.
/// </summary>
/// <remarks>
/// Só configura o que o projeto acrescenta ao <c>IdentityUser</c>. As colunas do Identity já
/// vêm mapeadas pela base — reconfigurá-las aqui só criaria divergência.
/// </remarks>
public sealed class UsuarioMapping : IEntityTypeConfiguration<Usuario>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("usuarios");

        builder.Property(u => u.Nome).IsRequired().HasMaxLength(120);

        builder.Property(u => u.Ativo).IsRequired().HasDefaultValue(true);

        builder.Property(u => u.CriadoEm).IsRequired();
        builder.Property(u => u.AtualizadoEm).IsRequired();

        builder.HasIndex(u => u.Nome);
    }
}

/// <summary>
/// Mapeamento da entidade de perfil.
/// </summary>
public sealed class PerfilMapping : IEntityTypeConfiguration<Perfil>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Perfil> builder) => builder.ToTable("perfis");
}
