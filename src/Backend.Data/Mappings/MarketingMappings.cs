using Backend.Business.Formaturas.Models;
using Backend.Business.Marketing.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento do histórico da preferência de marketing.
/// </summary>
/// <remarks>
/// Append-only por gatilho, como <c>consentimentos</c> — a migration liga o <c>recusar_alteracao()</c>. FK em
/// <c>Restrict</c>: é prova, e prova não some em cascata.
/// </remarks>
public sealed class ConsentimentoDeMarketingMapping : IEntityTypeConfiguration<ConsentimentoDeMarketing>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ConsentimentoDeMarketing> builder)
    {
        builder.ToTable("consentimentos_de_marketing");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Origem).IsRequired().HasMaxLength(40);
        builder.Property(c => c.VersaoDoTexto).IsRequired().HasMaxLength(20);
        builder.Property(c => c.EnderecoIp).IsRequired().HasMaxLength(45);
        builder.Property(c => c.UserAgent).IsRequired().HasMaxLength(512);

        builder.HasIndex(c => new { c.UsuarioId, c.RegistradoEm });

        builder.HasOne<Usuario>().WithMany().HasForeignKey(c => c.UsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeamento do registro dos envios de marketing.
/// </summary>
/// <remarks>
/// O índice único em <c>(usuario, formatura, jornada)</c> é a garantia de que a mesma jornada não sai duas vezes
/// para a mesma pessoa sobre a mesma turma — a consulta das jornadas já não a escolhe, e o índice segura a corrida.
/// O de <c>(usuario, enviado_em)</c> atende a trava dos 14 dias.
/// </remarks>
public sealed class EnvioDeMarketingMapping : IEntityTypeConfiguration<EnvioDeMarketing>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EnvioDeMarketing> builder)
    {
        builder.ToTable("envios_de_marketing");

        builder.Property(e => e.Jornada).IsRequired().HasMaxLength(40);

        builder
            .HasIndex(e => new
            {
                e.UsuarioId,
                e.FormaturaId,
                e.Jornada,
            })
            .IsUnique();
        builder.HasIndex(e => new { e.UsuarioId, e.EnviadoEm });

        builder.HasOne<Usuario>().WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(e => e.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}
