using Backend.Business.Formaturas.Models;
using Backend.Business.Recebimentos.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento da conta de recebimento.
/// </summary>
/// <remarks>
/// O índice único em <c>formatura_id</c> é a regra "uma por turma" no banco: duas primeiras gravações
/// simultâneas não viram duas contas. Índice <b>nomeado</b>, como em <c>assinaturas</c>: o sem nome na
/// mesma coluna é o da convenção de <c>EntidadeDaFormatura</c>, e declarar outro o substituiria.
/// <para>Tamanhos: e-mail de chave vai até 77; nome e cidade seguem os do cadastro do formando.</para>
/// </remarks>
public sealed class ContaDeRecebimentoMapping : IEntityTypeConfiguration<ContaDeRecebimento>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ContaDeRecebimento> builder)
    {
        builder.ToTable("contas_de_recebimento");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.TipoDeChave).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Chave).IsRequired().HasMaxLength(ChavePix.TamanhoMaximoDoEmail);
        builder.Property(c => c.NomeDoTitular).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Cidade).IsRequired().HasMaxLength(100);

        builder.Ignore(c => c.Conferida);

        builder
            .HasIndex(c => c.FormaturaId, "ix_contas_de_recebimento_uma_por_formatura")
            .IsUnique()
            .HasDatabaseName("ix_contas_de_recebimento_uma_por_formatura");

        builder.HasOne<Formatura>().WithMany().HasForeignKey(c => c.FormaturaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(c => c.ConferidaPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}
