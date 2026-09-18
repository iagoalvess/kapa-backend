using Backend.Business.Privacidade.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento das solicitações do portal do titular.
/// </summary>
/// <remarks>
/// <b>Sem coluna de formatura, e é de propósito</b> (decisão 2 da Sprint 14): a entidade não herda de
/// <c>EntidadeDaFormatura</c>, então o <c>AppDbContext</c> não aplica filtro global nem índice de
/// turma aqui. O titular é a pessoa, e o pedido dele atravessa as turmas em que ela está.
/// <para>
/// O índice parcial de pendentes é o que o worker varre de trinta em trinta segundos: sem ele, cada
/// passada varreria a tabela inteira para achar as zero linhas do dia.
/// </para>
/// </remarks>
public sealed class SolicitacaoDePrivacidadeMapping : IEntityTypeConfiguration<SolicitacaoDePrivacidade>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SolicitacaoDePrivacidade> builder)
    {
        builder.ToTable("solicitacoes_de_privacidade");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Tipo).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Motivo).HasMaxLength(300);

        builder.HasIndex(s => new { s.TitularUsuarioId, s.CriadoEm });

        builder
            .HasIndex(s => s.PrazoEm)
            .HasDatabaseName("ix_solicitacoes_de_privacidade_pendentes")
            .HasFilter($"status = '{nameof(StatusDaSolicitacaoDePrivacidade.Pendente)}'");

        builder.HasOne<Usuario>().WithMany().HasForeignKey(s => s.TitularUsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}
