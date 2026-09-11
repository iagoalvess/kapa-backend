using Backend.Business.Formaturas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento das formaturas.
/// </summary>
public sealed class FormaturaMapping : IEntityTypeConfiguration<Formatura>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Formatura> builder)
    {
        builder.ToTable("formaturas");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Nome).IsRequired().HasMaxLength(200);
    }
}
