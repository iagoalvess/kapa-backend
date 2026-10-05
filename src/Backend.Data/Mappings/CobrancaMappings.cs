using Backend.Business.Cobrancas.Models;
using Backend.Business.Cobrancas.Validators;
using Backend.Business.Festa.Models;
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

        builder.Ignore(p => p.ItensOpcionais);
        builder.HasMany(p => p.Itens).WithOne().HasForeignKey(i => i.PlanoId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Formatura>().WithMany().HasForeignKey(p => p.FormaturaId).OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(p => p.FormaturaId, "ix_planos_de_cobranca_vigente_por_formatura")
            .IsUnique()
            .HasFilter($"status = '{nameof(StatusDoPlano.Vigente)}'")
            .HasDatabaseName("ix_planos_de_cobranca_vigente_por_formatura");
    }
}

/// <summary>
/// Mapeamento dos itens do plano — e, desde a Sprint 20, também dos opcionais.
/// </summary>
/// <remarks>
/// As duas <c>CHECK</c> de estoque são a garantia de não vender a mais (decisão 10), e não o cinto
/// de segurança: job com bug, cancelamento que devolve duas vezes, migration futura e
/// <c>UPDATE</c> na mão em produção abortam a transação em vez de vender o convite 81. O
/// <c>reservados &gt;= 0</c> é, literalmente, a liberação indevida.
/// <para>
/// O índice do vínculo com a festa é único e <b>parcial</b>: um item da festa tem no máximo um item
/// de opcionais, e sem o filtro os nulos — que são a maioria — colidiriam entre si.
/// </para>
/// </remarks>
public sealed class ItemDeCobrancaMapping : IEntityTypeConfiguration<ItemDeCobranca>
{
    /// <inheritdoc />
    /// <remarks>
    /// O item da festa é <c>SetNull</c>, pela mesma razão do <c>DocumentoId</c> do item da festa:
    /// excluir um item da festa é operação legítima, e não pode ser barrada porque alguém abriu venda
    /// dele.
    /// </remarks>
    public void Configure(EntityTypeBuilder<ItemDeCobranca> builder)
    {
        builder.ToTable(
            "itens_de_cobranca",
            tabela =>
            {
                tabela.HasCheckConstraint("ck_itens_de_cobranca_reservados", "reservados >= 0 AND (estoque IS NULL OR reservados <= estoque)");
                tabela.HasCheckConstraint("ck_itens_de_cobranca_estoque", "estoque IS NULL OR estoque >= 0");
                tabela.HasCheckConstraint("ck_itens_de_cobranca_beneficios", "convites_da_festa >= 0 AND convites_da_colacao >= 0");
            }
        );

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Tipo).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Descricao).HasMaxLength(120);
        builder.Property(i => i.OrigemDaDecisao).HasMaxLength(RateioExtraordinarioValidator.TamanhoDaOrigem);

        builder.Property(i => i.ModoDeVenda).HasConversion<string>().HasMaxLength(20);

        builder.Ignore(i => i.Disponivel);
        builder.Ignore(i => i.PrecoNaLoja);
        builder.Ignore(i => i.NaLoja);
        builder.Ignore(i => i.Pacote);
        builder.Property(i => i.Grupo).HasMaxLength(60);
        builder.Property(i => i.AlvoDoRateio).HasDefaultValueSql("'{}'::uuid[]");

        builder.HasOne<VinculoDeFormatura>().WithMany().HasForeignKey(i => i.VinculoDoLancamento).OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(i => i.ItemDaFestaId, "ix_itens_de_cobranca_item_da_festa")
            .IsUnique()
            .HasFilter("item_da_festa_id IS NOT NULL")
            .HasDatabaseName("ix_itens_de_cobranca_item_da_festa");

        builder.HasOne<Formatura>().WithMany().HasForeignKey(i => i.FormaturaId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ItemDaFesta>().WithMany().HasForeignKey(i => i.ItemDaFestaId).OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>Mapeamento da cesta do formando (Sprint 47).</summary>
/// <remarks>Um pacote entra uma vez na cesta de cada vínculo: o índice único é a garantia contra a adesão em dobro.</remarks>
public sealed class EscolhaDaCestaMapping : IEntityTypeConfiguration<EscolhaDaCesta>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EscolhaDaCesta> builder)
    {
        builder.ToTable("escolhas_da_cesta");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Observacao).HasMaxLength(PedidoMapping.TamanhoDaObservacao);

        builder.HasIndex(e => new { e.VinculoId, e.ItemDeCobrancaId }).IsUnique();
        builder.HasIndex(e => e.ItemDeCobrancaId);

        builder.HasOne<VinculoDeFormatura>().WithMany().HasForeignKey(e => e.VinculoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ItemDeCobranca>().WithMany().HasForeignKey(e => e.ItemDeCobrancaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(e => e.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeamento dos pedidos dos opcionais.
/// </summary>
/// <remarks>
/// O índice único <c>(vinculo_id, item_de_cobranca_id)</c> é a decisão 3 no banco: um pedido por
/// item por formando. É ele que faz do clique duplo no <c>POST</c> um pedido só, sem precisar de
/// chave de idempotência — e é ele que mantém a chave natural da parcela intacta, já que dois
/// pedidos do mesmo item colidiriam no número dela.
/// <para>Chaves em <c>Restrict</c>: pedido vira dívida, e dívida não some em cascata.</para>
/// </remarks>
public sealed class PedidoMapping : IEntityTypeConfiguration<Pedido>
{
    /// <summary>Teto do detalhe livre do pedido e da cesta (Sprint 48, D26) — o mesmo dos validators.</summary>
    public const int TamanhoDaObservacao = 300;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Pedido> builder)
    {
        builder.ToTable(
            "pedidos",
            tabela =>
            {
                tabela.HasCheckConstraint("ck_pedidos_quantidade", "quantidade >= 1");
                tabela.HasCheckConstraint("ck_pedidos_parcelas", "parcelas >= 1");
            }
        );

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);

        builder.Ignore(p => p.Confirmado);
        builder.Property(p => p.Observacao).HasMaxLength(TamanhoDaObservacao);

        builder.HasIndex(p => new { p.VinculoId, p.ItemDeCobrancaId }).IsUnique();
        builder.HasIndex(p => new { p.ItemDeCobrancaId, p.Status });

        builder.HasOne<VinculoDeFormatura>().WithMany().HasForeignKey(p => p.VinculoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ItemDeCobranca>().WithMany().HasForeignKey(p => p.ItemDeCobrancaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(p => p.FormaturaId).OnDelete(DeleteBehavior.Restrict);
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

        builder.ComTokenDeConcorrencia();

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

/// <summary>Mapeamento das solicitações de cancelamento do formando (Sprint 48, D8).</summary>
/// <remarks>
/// Uma aberta por par (vínculo, item): o índice único parcial é o que faz do clique duplo uma solicitação só, como no
/// pedido de cancelamento da loja. Chaves em <c>Restrict</c>, como tudo o que toca dívida.
/// </remarks>
public sealed class SolicitacaoDeCancelamentoMapping : IEntityTypeConfiguration<SolicitacaoDeCancelamento>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SolicitacaoDeCancelamento> builder)
    {
        builder.ToTable("solicitacoes_de_cancelamento");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Motivo).HasMaxLength(PedidoMapping.TamanhoDaObservacao);
        builder.Property(s => s.MotivoDaResposta).HasMaxLength(PedidoMapping.TamanhoDaObservacao);

        builder
            .HasIndex(s => new { s.VinculoId, s.ItemDeCobrancaId })
            .IsUnique()
            .HasFilter("status = 'Aberto'")
            .HasDatabaseName("ix_solicitacoes_de_cancelamento_uma_aberta");
        builder.HasIndex(s => new { s.FormaturaId, s.Status });

        builder.HasOne<VinculoDeFormatura>().WithMany().HasForeignKey(s => s.VinculoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ItemDeCobranca>().WithMany().HasForeignKey(s => s.ItemDeCobrancaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Pedido>().WithMany().HasForeignKey(s => s.PedidoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(s => s.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}
