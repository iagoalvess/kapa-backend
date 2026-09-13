using Backend.Business.Formaturas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento das formaturas.
/// </summary>
/// <remarks>
/// O índice único parcial em <c>criado_por_usuario_id</c> onde <c>status = 'Rascunho'</c> é a
/// regra "um rascunho por usuário" no banco. O service confere antes e devolve o 409 com código,
/// mas um clique duplo manda as duas requisições juntas e as duas passam pela checagem — aí quem
/// barra a segunda é o índice.
/// </remarks>
public sealed class FormaturaMapping : IEntityTypeConfiguration<Formatura>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Formatura> builder)
    {
        builder.ToTable("formaturas");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Nome).IsRequired().HasMaxLength(200);
        builder.Property(f => f.Instituicao).IsRequired().HasMaxLength(120);
        builder.Property(f => f.Curso).IsRequired().HasMaxLength(120);
        builder.Property(f => f.Status).HasConversion<string>().HasMaxLength(30);

        builder
            .HasIndex(f => f.CriadoPorUsuarioId)
            .IsUnique()
            .HasFilter($"status = '{nameof(StatusDaFormatura.Rascunho)}'")
            .HasDatabaseName("ix_formaturas_rascunho_por_criador");
    }
}
