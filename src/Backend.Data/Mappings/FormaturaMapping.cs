using Backend.Business.Formaturas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento das formaturas.
/// </summary>
/// <remarks>
/// Sem índice para "uma turma não paga por conta": essa regra passou a depender de <c>assinaturas</c>
/// em 18/09/2026, quando <c>Rascunho</c> deixou de existir, e índice parcial não enxerga outra
/// tabela. Quem confere é <c>FormaturaService.Criar</c>. Ver o <c>ponytail:</c> em
/// <c>IFormaturaRepository.ExisteGratuitaCriadaPorDeTodasAsFormaturas</c>.
/// </remarks>
public sealed class FormaturaMapping : IEntityTypeConfiguration<Formatura>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Formatura> builder)
    {
        builder.ToTable("formaturas");

        builder.ComTokenDeConcorrencia();

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Nome).IsRequired().HasMaxLength(200);
        builder.Property(f => f.Instituicao).IsRequired().HasMaxLength(120);
        builder.Property(f => f.Curso).IsRequired().HasMaxLength(120);
        builder.Property(f => f.Status).HasConversion<string>().HasMaxLength(30);
    }
}
