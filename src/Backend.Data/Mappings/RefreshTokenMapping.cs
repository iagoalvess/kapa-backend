using Backend.Business.Auth.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento dos refresh tokens.
/// </summary>
public sealed class RefreshTokenMapping : IEntityTypeConfiguration<RefreshToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);

        builder.Property(t => t.CriadoPorIp).HasMaxLength(45);

        builder.Property(t => t.SubstituidoPorHash).HasMaxLength(64);

        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.HasIndex(t => new { t.UsuarioId, t.RevogadoEm });

        builder.HasOne<Usuario>().WithMany().HasForeignKey(t => t.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}
