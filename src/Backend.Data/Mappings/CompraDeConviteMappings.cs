using Backend.Business.Cobrancas.Models;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Loja.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento das compras da loja pública (Sprint 26).
/// </summary>
/// <remarks>
/// O índice único em <c>chave_de_idempotencia</c> é a garantia contra a compra em dobro (decisão 7): a
/// trava consultiva na chave serializa o clique duplo, e o índice segura o que escapar dela. O CPF é
/// cifrado pelo <c>AppDbContext</c>; <see cref="PropriedadeDoHmac"/> é a coluna de sombra que o limite por
/// pessoa (P3) procura, como na adesão — nula depois do descarte. O índice parcial
/// <c>(status, expira_em)</c> nas pendentes é o caminho do job de expiração.
/// </remarks>
public sealed class CompraDeConviteMapping : IEntityTypeConfiguration<CompraDeConvite>
{
    /// <summary>Nome da propriedade de sombra com o HMAC do CPF (coluna <c>cpf_hmac</c>).</summary>
    public const string PropriedadeDoHmac = "CpfHmac";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CompraDeConvite> builder)
    {
        builder.ToTable(
            "compras_de_convite",
            tabela =>
            {
                tabela.HasCheckConstraint("ck_compras_de_convite_quantidade", "quantidade > 0");
                tabela.HasCheckConstraint("ck_compras_de_convite_valor", "valor_em_centavos >= 0");
            }
        );

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Meio).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.NomeDoComprador).HasMaxLength(120);
        builder.Property(c => c.Email).HasMaxLength(200);
        builder.Property(c => c.Cpf).HasMaxLength(256);
        builder.Property(c => c.CpfDoPagador).HasMaxLength(256);
        builder.Property<string?>(PropriedadeDoHmac).HasMaxLength(64);

        builder.HasIndex(c => c.ChaveDeIdempotencia).IsUnique();
        builder.HasIndex(PropriedadeDoHmac, nameof(CompraDeConvite.ItemDeCobrancaId));
        builder.HasIndex(c => new { c.FormaturaId, c.Email });
        builder.HasIndex(c => new { c.Status, c.ExpiraEm }).HasFilter("status = 'Pendente'");

        builder.HasOne<Formatura>().WithMany().HasForeignKey(c => c.FormaturaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ItemDeCobranca>().WithMany().HasForeignKey(c => c.ItemDeCobrancaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OutraReceita>().WithMany().HasForeignKey(c => c.OutraReceitaId).OnDelete(DeleteBehavior.Restrict);
    }
}
