using Backend.Business.Agenda.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Festa.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Loja.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento dos convites da festa (e da colação).
/// </summary>
/// <remarks>
/// <b>Os dois índices únicos são o que impede duplicação</b> (decisões 12 e 14), e não detalhe de
/// performance:
/// <list type="bullet">
/// <item><c>codigo</c>, global e não por turma: o QR de uma turma nunca abre o convite de outra;</item>
/// <item><c>(evento_id, vinculo_id, pedido_id, sequencial)</c>, <b>parcial</b> —
/// <c>WHERE vinculo_id IS NOT NULL AND revogado_em IS NULL</c>. É o alvo do <c>ON CONFLICT</c> da
/// emissão: emitir de novo os convites 1 a N de um pedido não cria nada. Parcial nos válidos porque
/// a transferência e o estorno revogam a linha e uma nova nasce na mesma posição; parcial em
/// <c>vinculo_id</c> porque a cortesia não tem dono (decisão 14). <c>pedido_id</c> entra na chave
/// para dois itens de convite (adulto e infantil) não disputarem a mesma posição — e, como o convite
/// de cota tem pedido nulo, <c>NULLS NOT DISTINCT</c>.</item>
/// </list>
/// <para>
/// O convite da loja (Sprint 26) não tem vínculo e fica fora do índice acima: a posição dele é
/// <c>(compra_id, sequencial)</c> nos válidos, no índice <c>ux_convites_do_evento_posicao_da_compra</c>.
/// </para>
/// <para>
/// O índice <c>(formatura_id, evento_id)</c> é o da lista da portaria, a única consulta larga daqui.
/// </para>
/// </remarks>
public sealed class ConviteDoEventoMapping : IEntityTypeConfiguration<ConviteDoEvento>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ConviteDoEvento> builder)
    {
        builder.ToTable("convites_do_evento", tabela => tabela.HasCheckConstraint("ck_convites_do_evento_sequencial", "sequencial >= 0"));

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Codigo).IsRequired().HasMaxLength(20);
        builder.Property(c => c.NomeDoConvidado).HasMaxLength(120);
        builder.Property(c => c.TipoDoDocumento).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.NumeroDoDocumento).HasMaxLength(200);
        builder.Property(c => c.EmailDoConvidado).HasMaxLength(600);
        builder.Property(c => c.Observacoes).HasMaxLength(2000);
        builder.Property(c => c.MotivoDaRevogacao).HasMaxLength(200);

        builder.Ignore(c => c.Valido);
        builder.Ignore(c => c.Nomeado);
        builder.Ignore(c => c.Origem);

        builder.HasIndex(c => c.Codigo).IsUnique();
        builder
            .HasIndex(c => new
            {
                c.EventoId,
                c.VinculoId,
                c.PedidoId,
                c.Sequencial,
            })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter("vinculo_id IS NOT NULL AND revogado_em IS NULL")
            .HasDatabaseName("ux_convites_do_evento_posicao_valida");
        builder
            .HasIndex(c => new { c.CompraId, c.Sequencial })
            .IsUnique()
            .HasFilter("compra_id IS NOT NULL AND revogado_em IS NULL")
            .HasDatabaseName("ux_convites_do_evento_posicao_da_compra");
        builder.HasIndex(c => new { c.FormaturaId, c.EventoId });
        builder.HasIndex(c => c.PedidoId);

        builder.HasOne<EventoDaTurma>().WithMany().HasForeignKey(c => c.EventoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VinculoDeFormatura>().WithMany().HasForeignKey(c => c.VinculoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Pedido>().WithMany().HasForeignKey(c => c.PedidoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CompraDeConvite>().WithMany().HasForeignKey(c => c.CompraId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(c => c.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeamento das entradas da portaria.
/// </summary>
/// <remarks>
/// O índice único <b>parcial</b> <c>(convite_id) WHERE desfeito_em IS NULL</c> é a decisão 12: uma
/// entrada ativa por convite, e o histórico inteiro preservado — o desfeito, o refeito e a tentativa
/// repetida sem rede, que nasce desfeita. É o alvo do <c>ON CONFLICT</c> do check-in. Parcial, então
/// a armadilha de <c>NULL</c> distinto de <c>NULL</c> não se aplica.
/// </remarks>
public sealed class CheckInMapping : IEntityTypeConfiguration<CheckIn>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CheckIn> builder)
    {
        builder.ToTable("check_ins");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Aparelho).HasMaxLength(60);
        builder.Property(c => c.Motivo).HasMaxLength(40);

        builder.HasIndex(c => c.ConviteId).IsUnique().HasFilter("desfeito_em IS NULL").HasDatabaseName("ux_check_ins_ativo");
        builder.HasIndex(c => new { c.ConviteId, c.ValidadoEm });

        builder.HasOne<ConviteDoEvento>().WithMany().HasForeignKey(c => c.ConviteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(c => c.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}
