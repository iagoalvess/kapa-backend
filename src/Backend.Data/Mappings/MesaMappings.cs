using Backend.Business.Festa.Models;
using Backend.Business.Formaturas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento das mesas do jantar.
/// </summary>
/// <remarks>
/// Os dois <c>CHECK</c> são a regra, não enfeite: mesa tem ao menos um lugar, e mesa reservada nunca
/// tem dono (P3) — o service dá a mensagem, o banco garante. O índice em <c>vinculo_id</c> serve à
/// contagem das mesas de um formando, que acontece a cada atribuição.
/// </remarks>
public sealed class MesaMapping : IEntityTypeConfiguration<Mesa>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Mesa> builder)
    {
        builder.ToTable(
            "mesas",
            tabela =>
            {
                tabela.HasCheckConstraint("ck_mesas_lugares", "lugares > 0");
                tabela.HasCheckConstraint("ck_mesas_reservada_sem_dono", "NOT (reservada AND vinculo_id IS NOT NULL)");
            }
        );

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Identificacao).IsRequired().HasMaxLength(60);
        builder.Property(m => m.Observacao).HasMaxLength(200);

        builder.HasIndex(m => m.VinculoId);

        builder.HasOne<VinculoDeFormatura>().WithMany().HasForeignKey(m => m.VinculoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(m => m.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}
