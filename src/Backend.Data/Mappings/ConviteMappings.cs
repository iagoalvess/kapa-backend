using Backend.Business.Convites.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento dos convites.
/// </summary>
/// <remarks>
/// O índice único em <c>token_hash</c> é a busca do aceite e garante que dois convites nunca
/// respondam pelo mesmo link. O de <c>formatura_id</c> vem da convenção de <c>EntidadeDaFormatura</c>.
/// </remarks>
public sealed class ConviteMapping : IEntityTypeConfiguration<Convite>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Convite> builder)
    {
        builder.ToTable("convites");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.Papel).IsRequired().HasMaxLength(20);

        builder.HasIndex(c => c.TokenHash).IsUnique();

        builder.HasOne<Formatura>().WithMany().HasForeignKey(c => c.FormaturaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(c => c.CriadoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeamento dos aceites de convite.
/// </summary>
/// <remarks>Chaves em <c>Restrict</c>: é o registro de quem entrou por qual link, e registro não some em cascata.</remarks>
public sealed class AceiteDeConviteMapping : IEntityTypeConfiguration<AceiteDeConvite>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AceiteDeConvite> builder)
    {
        builder.ToTable("aceites_de_convite");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.EnderecoIp).HasMaxLength(45);
        builder.Property(a => a.UserAgent).HasMaxLength(512);

        builder.HasIndex(a => a.ConviteId);

        builder.HasOne<Convite>().WithMany().HasForeignKey(a => a.ConviteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(a => a.UsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}
