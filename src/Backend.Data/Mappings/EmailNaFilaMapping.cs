using Backend.Business.Emails.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento da fila de e-mails.
/// </summary>
public sealed class EmailNaFilaMapping : IEntityTypeConfiguration<EmailNaFila>
{
    /// <inheritdoc />
    /// <remarks>
    /// O índice cobre exatamente a consulta de reserva do worker (status + próxima tentativa,
    /// ordenado por prioridade e antiguidade). É filtrado apenas aos pendentes: e-mails já
    /// enviados são a maioria absoluta da tabela e nunca aparecem nessa busca.
    /// </remarks>
    public void Configure(EntityTypeBuilder<EmailNaFila> builder)
    {
        builder.ToTable("emails_fila");

        builder.Property(e => e.Para).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Assunto).IsRequired().HasMaxLength(300);
        builder.Property(e => e.CorpoHtml).IsRequired();
        builder.Property(e => e.UltimoErro).HasMaxLength(1000);

        builder.Property(e => e.Status).HasConversion<int>();
        builder.Property(e => e.Prioridade).HasConversion<int>();

        builder
            .HasIndex(e => new
            {
                e.Status,
                e.ProximaTentativaEm,
                e.Prioridade,
            })
            .HasFilter("status = 0");
    }
}
