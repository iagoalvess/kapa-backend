using Backend.Business.Enquetes.Models;
using Backend.Business.Formaturas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>Consulta sem status nem contador persistidos.</summary>
public sealed class EnqueteMapping : IEntityTypeConfiguration<Enquete>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Enquete> builder)
    {
        builder.ToTable(
            "enquetes",
            t =>
            {
                t.HasCheckConstraint("ck_enquetes_quorum", "quorum_minimo_percentual BETWEEN 1 AND 100");
                t.HasCheckConstraint(
                    "ck_enquetes_escolhas",
                    "(tipo = 'UnicaEscolha' AND maximo_de_escolhas IS NULL) OR (tipo = 'MultiplaEscolha' AND maximo_de_escolhas BETWEEN 1 AND 10)"
                );
            }
        );
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Pergunta).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Descricao).HasMaxLength(2000);
        builder.Property(e => e.Tipo).HasConversion<string>().HasMaxLength(30);
        builder.HasIndex(e => new { e.FormaturaId, e.EncerraEm });
        builder.HasOne<Formatura>().WithMany().HasForeignKey(e => e.FormaturaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Opcoes).WithOne().HasForeignKey(o => o.EnqueteId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Alternativas ordenadas por consulta.</summary>
public sealed class OpcaoDaEnqueteMapping : IEntityTypeConfiguration<OpcaoDaEnquete>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OpcaoDaEnquete> builder)
    {
        builder.ToTable("opcoes_de_enquetes");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Texto).IsRequired().HasMaxLength(120);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(o => o.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Unicidade por vínculo preserva respostas anônimas distintas.</summary>
public sealed class RespostaDaEnqueteMapping : IEntityTypeConfiguration<RespostaDaEnquete>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RespostaDaEnquete> builder)
    {
        builder.ToTable("respostas_de_enquetes");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.EnqueteId, r.VinculoId }).IsUnique().HasFilter("vinculo_id IS NOT NULL");
        builder.HasOne<Enquete>().WithMany().HasForeignKey(r => r.EnqueteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VinculoDeFormatura>().WithMany().HasForeignKey(r => r.VinculoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(r => r.FormaturaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Escolhas).WithOne().HasForeignKey(e => e.RespostaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Escolhas normalizadas para a contagem por opção.</summary>
public sealed class EscolhaDaRespostaMapping : IEntityTypeConfiguration<EscolhaDaResposta>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EscolhaDaResposta> builder)
    {
        builder.ToTable("escolhas_de_enquetes");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.RespostaId, e.OpcaoId }).IsUnique();
        builder.HasOne<OpcaoDaEnquete>().WithMany().HasForeignKey(e => e.OpcaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(e => e.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}
