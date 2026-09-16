using Backend.Business.Cobrancas.Models;
using Backend.Business.Formaturas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento dos planos de cobrança.
/// </summary>
/// <remarks>
/// O índice único parcial em <c>formatura_id</c> onde <c>status = 'Vigente'</c> é a regra "um
/// plano vigente por turma" no banco: o service confere antes, mas dois cliques em "colocar em
/// vigor" em dois planos passam juntos pela conferência — e aí quem barra o segundo é o índice.
/// Nomeado, pelo mesmo motivo do de assinaturas: o sem nome em <c>formatura_id</c> é o da convenção.
/// </remarks>
public sealed class PlanoDeCobrancaMapping : IEntityTypeConfiguration<PlanoDeCobranca>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlanoDeCobranca> builder)
    {
        builder.ToTable("planos_de_cobranca");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Nome).IsRequired().HasMaxLength(120);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);

        builder.Ignore(p => p.ItensAtivos);
        builder.HasMany(p => p.Itens).WithOne().HasForeignKey(i => i.PlanoId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Formatura>().WithMany().HasForeignKey(p => p.FormaturaId).OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(p => p.FormaturaId, "ix_planos_de_cobranca_vigente_por_formatura")
            .IsUnique()
            .HasFilter($"status = '{nameof(StatusDoPlano.Vigente)}'")
            .HasDatabaseName("ix_planos_de_cobranca_vigente_por_formatura");
    }
}

/// <summary>Mapeamento dos itens do plano.</summary>
public sealed class ItemDeCobrancaMapping : IEntityTypeConfiguration<ItemDeCobranca>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ItemDeCobranca> builder)
    {
        builder.ToTable("itens_de_cobranca");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Tipo).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Descricao).HasMaxLength(120);

        builder.HasOne<Formatura>().WithMany().HasForeignKey(i => i.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeamento das parcelas.
/// </summary>
/// <remarks>
/// O índice único <c>(vinculo_id, item_de_cobranca_id, numero)</c> é a idempotência da geração: a
/// segunda cobrança do mesmo mês para a mesma pessoa não grava, venha de onde vier. Ele também
/// serve a consulta por vínculo — o extrato do formando — pela coluna da frente.
/// <para>
/// O <c>(formatura_id, status, vencimento)</c> é o das agregações do dashboard (Sprint 12):
/// adimplência, inadimplentes e o que a turma deve são sempre "abertas da turma antes de hoje", e é
/// esse índice que as responde sem varrer a tabela.
/// </para>
/// <para>Chaves em <c>Restrict</c>: parcela é dívida, e dívida não some em cascata.</para>
/// </remarks>
public sealed class ParcelaMapping : IEntityTypeConfiguration<Parcela>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Parcela> builder)
    {
        builder.ToTable("parcelas");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);

        builder
            .HasIndex(p => new
            {
                p.VinculoId,
                p.ItemDeCobrancaId,
                p.Numero,
            })
            .IsUnique();
        builder.HasIndex(p => new { p.ItemDeCobrancaId, p.Vencimento });
        builder.HasIndex(p => new
        {
            p.FormaturaId,
            p.Status,
            p.Vencimento,
        });

        builder.HasOne<VinculoDeFormatura>().WithMany().HasForeignKey(p => p.VinculoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ItemDeCobranca>().WithMany().HasForeignKey(p => p.ItemDeCobrancaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(p => p.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}
