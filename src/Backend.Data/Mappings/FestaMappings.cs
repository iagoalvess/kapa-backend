using Backend.Business.Comunicacao.Models;
using Backend.Business.Festa.Models;
using Backend.Business.Formaturas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento dos itens da festa.
/// </summary>
/// <remarks>
/// O índice <c>(formatura_id, ordem)</c> é o da própria tela: a lista sai ordenada por ele, e é a
/// única consulta que esta tabela serve.
/// <para>
/// A tabela é magra de propósito. Não há coluna de situação: "a contratar", "contratado" e "pago"
/// são lidos das despesas (decisão 2). Não há coluna de fornecedor: ele também sai das despesas
/// (decisão 15). O que se grava é o cancelamento — <c>cancelado_em</c> —, que é fato, e não
/// consequência de outra tabela.
/// </para>
/// <para>
/// A chave do contrato é <c>SetNull</c>, e não <c>Restrict</c> como as demais: excluir um documento
/// do acervo é operação legítima da comissão (Sprint 11), e ela não pode ser barrada porque alguém
/// ligou aquele PDF a um cartão. O item continua de pé, sem contrato.
/// </para>
/// </remarks>
public sealed class ItemDaFestaMapping : IEntityTypeConfiguration<ItemDaFesta>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ItemDaFesta> builder)
    {
        builder.ToTable("itens_da_festa");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Titulo).IsRequired().HasMaxLength(120);
        builder.Property(i => i.Categoria).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Rateio).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.OQueInclui).HasMaxLength(2000);

        builder.Ignore(i => i.Cancelado);

        builder.HasIndex(i => new { i.FormaturaId, i.Ordem });

        builder.HasOne<Documento>().WithMany().HasForeignKey(i => i.DocumentoId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(i => i.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeamento das propostas de um item.
/// </summary>
/// <remarks>
/// A chave para o item é <c>Cascade</c>: excluir o item leva as candidatas dele junto. É o oposto da
/// despesa, e de propósito — proposta não é dinheiro, é anotação da escolha, e só o item que
/// <b>nunca</b> teve despesa pode ser excluído (decisão 13).
/// </remarks>
public sealed class PropostaDoItemMapping : IEntityTypeConfiguration<PropostaDoItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PropostaDoItem> builder)
    {
        builder.ToTable("propostas_do_item");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Titulo).IsRequired().HasMaxLength(120);
        builder.Property(p => p.OQueInclui).HasMaxLength(1000);

        builder.HasIndex(p => p.ItemDaFestaId);

        builder.HasOne<ItemDaFesta>().WithMany().HasForeignKey(p => p.ItemDaFestaId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(p => p.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeamento dos votos nas propostas.
/// </summary>
/// <remarks>
/// <b>O índice único em <c>(vinculo_id, item_da_festa_id)</c> é a decisão 17</b>, e não um detalhe de
/// performance: é ele que faz "um voto por formando por item" ser verdade mesmo com dois cliques
/// simultâneos em propostas diferentes. O service lê e atualiza o voto que existe; a constraint é
/// quem impede o segundo de nascer.
/// <para>
/// As duas chaves são <c>Cascade</c>: apagar a proposta apaga os votos nela, e apagar o item apaga
/// os dois. Voto é preferência — sem a proposta, ele não quer dizer nada.
/// </para>
/// </remarks>
public sealed class VotoNaPropostaMapping : IEntityTypeConfiguration<VotoNaProposta>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VotoNaProposta> builder)
    {
        builder.ToTable("votos_nas_propostas");

        builder.HasKey(v => v.Id);

        builder.HasIndex(v => new { v.VinculoId, v.ItemDaFestaId }).IsUnique();
        builder.HasIndex(v => v.PropostaId);

        builder.HasOne<PropostaDoItem>().WithMany().HasForeignKey(v => v.PropostaId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ItemDaFesta>().WithMany().HasForeignKey(v => v.ItemDaFestaId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<VinculoDeFormatura>().WithMany().HasForeignKey(v => v.VinculoId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(v => v.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}
