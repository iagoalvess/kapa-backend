using Backend.Business.Eventos.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento da tabela de eventos.
/// </summary>
public sealed class EventoMapping : IEntityTypeConfiguration<Evento>
{
    /// <inheritdoc />
    /// <remarks>
    /// Dois índices, para as duas perguntas que sempre aparecem: "quanto deste evento aconteceu
    /// no período" e "o que este usuário fez". O de retenção aproveita o primeiro, porque
    /// <c>OcorridoEm</c> é a coluna principal dele.
    /// </remarks>
    public void Configure(EntityTypeBuilder<Evento> builder)
    {
        builder.ToTable("eventos");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Nome).IsRequired().HasMaxLength(120);
        builder.Property(e => e.Rota).HasMaxLength(300);

        builder.Property(e => e.Dados).HasColumnType("jsonb");

        builder.HasIndex(e => new { e.OcorridoEm, e.Nome });
        builder.HasIndex(e => new { e.UsuarioId, e.OcorridoEm });
    }
}
