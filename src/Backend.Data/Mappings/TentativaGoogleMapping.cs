using Backend.Business.Auth.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>Contextos curtos do SSO: credenciais persistidas apenas como hash.</summary>
public sealed class TentativaGoogleMapping : IEntityTypeConfiguration<TentativaGoogle>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TentativaGoogle> builder)
    {
        builder.ToTable("tentativas_google");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.NavegadorHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Nonce).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Etapa).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Sub).HasMaxLength(255);
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.Nome).HasMaxLength(120);
        builder.Property(x => x.SecurityStamp).HasMaxLength(256);
        builder.Property(x => x.ComprovanteHash).HasMaxLength(64);
        builder.Property(x => x.Convite).HasMaxLength(2048);
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.ExpiraEm);
    }
}
