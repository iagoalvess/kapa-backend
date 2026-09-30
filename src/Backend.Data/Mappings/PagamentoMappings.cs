using Backend.Business.Arquivos.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Models;
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
        builder.Property(i => i.MeioEscolhido).HasConversion<string>().HasMaxLength(20);
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
/// Mapeamento da lista "a devolver" (Sprint 42).
/// </summary>
/// <remarks>
/// O índice parcial nos que esperam é o da contagem de pendências do encerramento e da fila da tesouraria. Chaves em
/// <c>Restrict</c>, como no recebimento: o registro de dinheiro não some em cascata.
/// </remarks>
public sealed class ValorADevolverMapping : IEntityTypeConfiguration<ValorADevolver>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ValorADevolver> builder)
    {
        builder.ToTable("valores_a_devolver");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Origem).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Observacao).HasMaxLength(500);

        builder.HasIndex(v => v.CriadoEm).HasFilter($"status = '{nameof(StatusDoValorADevolver.ADevolver)}'");
        builder.HasIndex(v => v.ParcelaId);
        builder.HasIndex(v => v.CobrancaId);

        builder.HasOne<VinculoDeFormatura>().WithMany().HasForeignKey(v => v.VinculoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ItemDeCobranca>().WithMany().HasForeignKey(v => v.ItemDeCobrancaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Parcela>().WithMany().HasForeignKey(v => v.ParcelaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Pedido>().WithMany().HasForeignKey(v => v.PedidoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CobrancaBancaria>().WithMany().HasForeignKey(v => v.CobrancaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Despesa>().WithMany().HasForeignKey(v => v.DespesaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Arquivo>().WithMany().HasForeignKey(v => v.ComprovanteArquivoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(v => v.ResolvidoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(v => v.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeamento dos recebimentos — as entradas no caixa.
/// </summary>
/// <remarks>
/// O índice de <c>parcela_id</c> deixou de ser único em 17/09/2026: o pagamento parcial não fecha a
/// parcela, então ela passa a ter uma entrada por PIX recebido, e o único recusaria a segunda. Quem
/// barra a baixa dupla é a trava da parcela (<c>IParcelaRepository.TravarParaBaixa</c>) somada à
/// conferência de status — e, no informe, o <c>pagamento.informe_pendente</c>.
/// <c>ponytail:</c> a barreira do banco contra um caminho futuro que esqueça a trava caiu junto;
/// se ela fizer falta, o caminho é um único sobre <c>(informe_id)</c>, que continua sendo um para um.
/// <para>Chaves em <c>Restrict</c>: dinheiro que entrou não some em cascata.</para>
/// <para>
/// O índice de <c>cobranca_id</c> é o caminho da devolução do Mercado Pago até a baixa que aquela cobrança fez
/// (Sprint 42, F3).
/// </para>
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

        builder.HasIndex(r => r.ParcelaId);
        builder.HasIndex(r => r.CobrancaId);

        builder.HasOne<Parcela>().WithMany().HasForeignKey(r => r.ParcelaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InformeDePagamento>().WithMany().HasForeignKey(r => r.InformeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CobrancaBancaria>().WithMany().HasForeignKey(r => r.CobrancaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Arquivo>().WithMany().HasForeignKey(r => r.ComprovanteArquivoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(r => r.BaixadoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(r => r.EstornadoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(r => r.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}
