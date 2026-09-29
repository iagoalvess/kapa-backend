using Backend.Business.Formaturas.Models;
using Backend.Business.Loja.Models;
using Backend.Business.Recebimentos.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento da autorização do Mercado Pago da turma.
/// </summary>
/// <remarks>
/// Uma por turma, garantido pelo índice único nomeado — o sem nome na mesma coluna é o da convenção de
/// <c>EntidadeDaFormatura</c>, e declarar outro o substituiria. Os tokens são cifrados pelo conversor da
/// <c>CifraDeCampo</c>, aplicado no <c>AppDbContext</c>; o tamanho é o da cifra em Base64, não o do token.
/// </remarks>
public sealed class CredencialDeProvedorMapping : IEntityTypeConfiguration<CredencialDeProvedor>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CredencialDeProvedor> builder)
    {
        builder.ToTable("credenciais_de_provedor");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.AccessToken).HasMaxLength(1000).IsRequired();
        builder.Property(c => c.RefreshToken).HasMaxLength(1000).IsRequired();
        builder.Property(c => c.ContaNoProvedor).HasMaxLength(200).IsRequired();
        builder.Property(c => c.ChavePublica).HasMaxLength(200);

        builder
            .HasIndex(c => c.FormaturaId, "ix_credenciais_de_provedor_uma_por_formatura")
            .IsUnique()
            .HasDatabaseName("ix_credenciais_de_provedor_uma_por_formatura");

        builder.HasIndex(c => c.ExpiraEm);

        builder.HasOne<Formatura>().WithMany().HasForeignKey(c => c.FormaturaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(c => c.CadastradaPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeamento das cobranças feitas pelo Mercado Pago da turma.
/// </summary>
/// <remarks>
/// O índice único parcial em <c>chave</c> é "uma cobrança viva por pagamento" no banco (Sprint 25,
/// decisão 4): enquanto <c>Emitindo</c> ou <c>Emitida</c>, a segunda emissão com a mesma chave — no PIX, as
/// parcelas, o valor e o dia; na compra, a compra — não entra — é contra ele que a reserva faz <c>ON CONFLICT DO NOTHING</c>. O índice em
/// <c>id_externo</c> é o caminho do aviso do Mercado Pago, que chega só com o id do pedido.
/// <para>
/// As parcelas numa coluna <c>uuid[]</c>, e não em tabela de ligação: a cobrança não muda de parcelas
/// depois de emitida, e ninguém consulta "as cobranças desta parcela" — quem lê é sempre a cobrança.
/// </para>
/// </remarks>
public sealed class CobrancaBancariaMapping : IEntityTypeConfiguration<CobrancaBancaria>
{
    /// <summary>Os status em que a cobrança ainda pode ser paga.</summary>
    public static readonly string FiltroDasVivas =
        $"status IN ('{nameof(StatusDaCobrancaBancaria.Emitindo)}', '{nameof(StatusDaCobrancaBancaria.Emitida)}')";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CobrancaBancaria> builder)
    {
        builder.ToTable("cobrancas_bancarias");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.ParcelaIds).IsRequired();
        builder.Property(c => c.Chave).HasMaxLength(1000).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.IdExterno).HasMaxLength(100);
        builder.Property(c => c.Meio).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.CopiaECola).HasMaxLength(1000);

        builder.HasIndex(c => new { c.FormaturaId, c.Chave }).IsUnique().HasFilter(FiltroDasVivas);
        builder.HasIndex(c => c.IdExterno);
        builder.HasIndex(c => c.CompraId);
        builder.HasIndex(c => new { c.Status, c.CriadoEm });

        builder.HasOne<Formatura>().WithMany().HasForeignKey(c => c.FormaturaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CompraDeConvite>().WithMany().HasForeignKey(c => c.CompraId).OnDelete(DeleteBehavior.Restrict);
    }
}
