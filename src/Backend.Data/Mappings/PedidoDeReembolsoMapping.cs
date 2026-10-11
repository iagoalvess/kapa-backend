using Backend.Business.Assinaturas.Models;
using Backend.Business.Formaturas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>Pedido isolado por formatura, com uma solicitação por cobrança mesmo em cliques concorrentes.</summary>
public sealed class PedidoDeReembolsoMapping : IEntityTypeConfiguration<PedidoDeReembolso>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PedidoDeReembolso> builder)
    {
        builder.ToTable("pedidos_de_reembolso");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Motivo).IsRequired().HasMaxLength(2000);
        builder.Property(p => p.EmailDoSolicitante).HasMaxLength(256);
        builder.Property(p => p.IdDoPagamento).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Meio).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(p => p.CobrancaId).IsUnique();
        builder.HasIndex(p => new { p.Status, p.CriadoEm });
        builder.HasOne<Assinatura>().WithMany().HasForeignKey(p => p.AssinaturaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CobrancaDaAssinatura>().WithMany().HasForeignKey(p => p.CobrancaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(p => p.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}
