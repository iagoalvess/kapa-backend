using Backend.Business.Assinaturas.Models;
using Backend.Business.Formaturas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>Mapeamento do catálogo de planos.</summary>
public sealed class PlanoMapping : IEntityTypeConfiguration<Plano>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Plano> builder)
    {
        builder.ToTable("planos");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Codigo).IsRequired().HasMaxLength(40);
        builder.Property(p => p.Nome).IsRequired().HasMaxLength(80);
        builder.Property(p => p.Ciclo).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(p => p.Codigo).IsUnique();
    }
}

/// <summary>
/// Mapeamento das assinaturas.
/// </summary>
/// <remarks>
/// O índice único parcial em <c>formatura_id</c> onde <c>status = 'Pendente'</c> é a regra "uma
/// pendente por formatura" no banco: o service retoma a pendente que existe, mas um clique duplo
/// manda dois checkouts juntos, e aí quem barra o segundo é o índice.
/// <para>
/// Índice <b>nomeado</b>: o sem nome em <c>formatura_id</c> é o da convenção de
/// <c>EntidadeDaFormatura</c>, e declarar outro sem nome na mesma coluna o substituiria.
/// </para>
/// </remarks>
public sealed class AssinaturaMapping : IEntityTypeConfiguration<Assinatura>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Assinatura> builder)
    {
        builder.ToTable("assinaturas");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.IdExterno).HasMaxLength(200);

        builder.HasOne<Plano>().WithMany().HasForeignKey(a => a.PlanoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(a => a.FormaturaId).OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(a => a.FormaturaId, "ix_assinaturas_pendente_por_formatura")
            .IsUnique()
            .HasFilter($"status = '{nameof(StatusDaAssinatura.Pendente)}'")
            .HasDatabaseName("ix_assinaturas_pendente_por_formatura");

        builder.HasIndex(a => new { a.Status, a.VigenteAte });
    }
}

/// <summary>
/// Mapeamento dos eventos de cobrança.
/// </summary>
/// <remarks>
/// O índice único em <c>id_externo</c> é a idempotência do webhook: evento reentregue esbarra nele
/// e não reprocessa. <c>payload</c> é texto, e não <c>jsonb</c>: há PSP que manda formulário, e o
/// corpo precisa ser guardado exatamente como chegou.
/// </remarks>
public sealed class EventoDeCobrancaMapping : IEntityTypeConfiguration<EventoDeCobranca>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EventoDeCobranca> builder)
    {
        builder.ToTable("eventos_de_cobranca");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.IdExterno).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Tipo).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Payload).IsRequired();

        builder.HasIndex(e => e.IdExterno).IsUnique();
        builder.HasIndex(e => e.AssinaturaId);
    }
}
