using Backend.Business.Formaturas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento dos avisos do mural.
/// </summary>
/// <remarks>
/// Sem uma linha sequer sobre <c>FormaturaId</c>: o índice e o filtro global vêm da convenção
/// de <c>EntidadeDaFormatura</c>, aplicada pelo <c>AppDbContext</c>.
/// </remarks>
public sealed class AvisoMapping : IEntityTypeConfiguration<Aviso>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Aviso> builder)
    {
        builder.ToTable("avisos");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Texto).IsRequired().HasMaxLength(2000);

        builder.HasOne<Formatura>().WithMany().HasForeignKey(a => a.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}
