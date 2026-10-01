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
    /// <para>
    /// O primeiro <b>cobre</b> o ranking de uso do painel do Kapa (Sprint 44, critério 6): com nome, turma e
    /// usuário no próprio índice, o recorte do período é lido sem tocar a tabela — a maior do banco. Medido num
    /// clone com 3 milhões de eventos, o ranking de 30 dias caiu de 417 para 288 ms. Substituiu o
    /// <c>(ocorrido_em, nome)</c>, que ele engloba.
    /// </para>
    /// <para>
    /// O terceiro é da trilha de auditoria (Sprint 14) e é <b>parcial</b>: só a minoria dos eventos
    /// pertence a uma turma, e um índice sobre a coluna inteira guardaria uma entrada por clique de
    /// analytics para responder a uma consulta que nunca os alcança.
    /// </para>
    /// </remarks>
    public void Configure(EntityTypeBuilder<Evento> builder)
    {
        builder.ToTable("eventos");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Nome).IsRequired().HasMaxLength(120);

        builder.Property(e => e.Dados).HasColumnType("jsonb");

        builder
            .HasIndex(e => e.OcorridoEm)
            .IncludeProperties(e => new
            {
                e.Nome,
                e.FormaturaId,
                e.UsuarioId,
            });
        builder.HasIndex(e => new { e.UsuarioId, e.OcorridoEm });

        builder
            .HasIndex(e => new { e.FormaturaId, e.OcorridoEm })
            .HasDatabaseName("ix_eventos_trilha_da_formatura")
            .HasFilter("formatura_id is not null");
    }
}
