using Backend.Business.Arquivos.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento dos informes de pagamento.
/// </summary>
/// <remarks>
/// O índice único parcial em <c>parcela_id</c> onde <c>status = 'Pendente'</c> é o "um aviso pendente
/// por parcela" no banco: o service confere antes, mas dois toques no "Já paguei" passam juntos pela
/// conferência — e aí quem barra o segundo é o índice. Recusado e confirmado não contam: o formando pode
/// avisar de novo depois de uma recusa.
/// </remarks>
public sealed class InformeDePagamentoMapping : IEntityTypeConfiguration<InformeDePagamento>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InformeDePagamento> builder)
    {
        builder.ToTable("informes_de_pagamento");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.MotivoDaRecusa).HasMaxLength(500);

        builder.HasIndex(i => i.ParcelaId).IsUnique().HasFilter($"status = '{nameof(StatusDoInforme.Pendente)}'");
        builder.HasIndex(i => new { i.Status, i.CriadoEm });

        builder.HasOne<Parcela>().WithMany().HasForeignKey(i => i.ParcelaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VinculoDeFormatura>().WithMany().HasForeignKey(i => i.VinculoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Arquivo>().WithMany().HasForeignKey(i => i.ComprovanteArquivoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(i => i.ConferidoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(i => i.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeamento dos recebimentos — as entradas no caixa.
/// </summary>
/// <remarks>
/// O índice único parcial em <c>parcela_id</c> onde <c>estornado_em is null</c> é a última barreira
/// contra a baixa dupla: a parcela é travada antes da baixa, e se algum caminho futuro esquecer a trava,
/// o segundo insert falha. Estornar libera a parcela para uma baixa nova, e as duas linhas ficam.
/// Chaves em <c>Restrict</c>: dinheiro que entrou não some em cascata.
/// </remarks>
public sealed class RecebimentoMapping : IEntityTypeConfiguration<Recebimento>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Recebimento> builder)
    {
        builder.ToTable("recebimentos");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Forma).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.EnderecoIp).IsRequired().HasMaxLength(45);
        builder.Property(r => r.JustificativaDoEstorno).HasMaxLength(500);

        builder.Ignore(r => r.Divergente);

        builder.HasIndex(r => r.ParcelaId).IsUnique().HasFilter("estornado_em IS NULL");

        builder.HasOne<Parcela>().WithMany().HasForeignKey(r => r.ParcelaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InformeDePagamento>().WithMany().HasForeignKey(r => r.InformeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Arquivo>().WithMany().HasForeignKey(r => r.ComprovanteArquivoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(r => r.BaixadoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(r => r.EstornadoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(r => r.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}
