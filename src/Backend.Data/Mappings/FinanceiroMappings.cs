using Backend.Business.Arquivos.Models;
using Backend.Business.Comunicacao.Models;
using Backend.Business.Festa.Models;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formaturas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento dos fornecedores.
/// </summary>
/// <remarks>
/// O índice único <c>(formatura_id, nome)</c> é o "um fornecedor por nome na turma": o service
/// confere antes, e dois cliques que passem juntos pela conferência esbarram aqui. Chave em
/// <c>Restrict</c>: excluir fornecedor com despesa é recusado no service (409), e o banco é a
/// segunda barreira.
/// </remarks>
public sealed class FornecedorMapping : IEntityTypeConfiguration<Fornecedor>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Fornecedor> builder)
    {
        builder.ToTable("fornecedores");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Nome).IsRequired().HasMaxLength(200);
        builder.Property(f => f.Documento).HasMaxLength(14);
        builder.Property(f => f.Categoria).HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.Telefone).HasMaxLength(20);
        builder.Property(f => f.Email).HasMaxLength(256);
        builder.Property(f => f.Observacoes).HasMaxLength(1000);

        builder.HasIndex(f => new { f.FormaturaId, f.Nome }).IsUnique();

        builder.HasOne<Formatura>().WithMany().HasForeignKey(f => f.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeamento das despesas.
/// </summary>
/// <remarks>
/// O índice único <c>(formatura_id, fornecedor_id, descricao, vencimento)</c> onde
/// <c>status &lt;&gt; 'Cancelada'</c> é o "lançar duas vezes cria uma só": o service confere antes, e o
/// clique duplo que passe junto pela conferência falha no insert. <c>NULLS NOT DISTINCT</c> porque
/// despesa sem fornecedor também precisa ser barrada — no padrão do Postgres, dois nulos são
/// diferentes, e a taxa bancária entraria duas vezes. Cancelada fica de fora: relançar o que foi
/// cancelado é legítimo.
/// <para>
/// O índice <c>(formatura_id, lancamento_id)</c> atende as irmãs de uma parcelada: a tela de detalhe
/// pede as N linhas do lançamento de uma vez. O <c>(formatura_id, status, competencia)</c> é o do
/// quadro por categoria e do fechamento por mês (risco da sprint). O
/// <c>(formatura_id, item_da_festa_id)</c> é o das somas por item da Sprint 17, que a tela da festa
/// pede uma vez por item numa consulta só. Chaves em <c>Restrict</c>: dinheiro que saiu não some em
/// cascata.
/// </para>
/// </remarks>
public sealed class DespesaMapping : IEntityTypeConfiguration<Despesa>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Despesa> builder)
    {
        builder.ToTable("despesas");

        builder.HasKey(d => d.Id);

        builder.ComTokenDeConcorrencia();

        builder.Property(d => d.Descricao).IsRequired().HasMaxLength(200);
        builder.Property(d => d.Categoria).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);

        builder.Ignore(d => d.EmAberto);

        builder
            .HasIndex(
                d => new
                {
                    d.FormaturaId,
                    d.FornecedorId,
                    d.Descricao,
                    d.Vencimento,
                },
                "ix_despesas_lancamento_unico"
            )
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter($"status <> '{nameof(StatusDaDespesa.Cancelada)}'")
            .HasDatabaseName("ix_despesas_lancamento_unico");

        builder.HasIndex(d => new { d.FormaturaId, d.LancamentoId });

        builder.HasIndex(d => new
        {
            d.FormaturaId,
            d.Status,
            d.Competencia,
        });
        builder.HasIndex(d => new { d.FormaturaId, d.Vencimento });

        builder.HasIndex(d => new { d.FormaturaId, d.ItemDaFestaId });

        builder.HasOne<Fornecedor>().WithMany().HasForeignKey(d => d.FornecedorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ItemDaFesta>().WithMany().HasForeignKey(d => d.ItemDaFestaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Arquivo>().WithMany().HasForeignKey(d => d.ComprovanteArquivoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(d => d.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeamento de <see cref="OutraReceita"/> (Sprint 28) — o espelho da despesa.</summary>
public sealed class OutraReceitaMapping : IEntityTypeConfiguration<OutraReceita>
{
    /// <inheritdoc />
    /// <remarks>
    /// O índice único é a segunda barreira contra o clique repetido (a primeira é o
    /// <c>ExisteIgual</c> do service), com <c>NULLS NOT DISTINCT</c> pela mesma armadilha da despesa:
    /// sem ele, duas receitas iguais <b>sem origem</b> passariam, porque no Postgres dois nulos são
    /// diferentes.
    /// <para>
    /// O documento é <c>SetNull</c>, como o contrato do item da festa: apagar o arquivo do acervo tira
    /// o comprovante, não a receita — o dinheiro entrou de qualquer jeito.
    /// </para>
    /// </remarks>
    public void Configure(EntityTypeBuilder<OutraReceita> builder)
    {
        builder.ToTable("outras_receitas");

        builder.HasKey(r => r.Id);

        builder.ComTokenDeConcorrencia();

        builder.Property(r => r.Descricao).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Origem).HasMaxLength(200);
        builder.Property(r => r.Categoria).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        builder
            .HasIndex(r => new
            {
                r.FormaturaId,
                r.Descricao,
                r.Origem,
                r.Data,
            })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter($"status <> '{nameof(StatusDaOutraReceita.Cancelada)}'")
            .HasDatabaseName("ix_outras_receitas_lancamento_unico");

        builder.HasIndex(r => new
        {
            r.FormaturaId,
            r.Status,
            r.Data,
        });

        builder.HasOne<Documento>().WithMany().HasForeignKey(r => r.DocumentoId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(r => r.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}
