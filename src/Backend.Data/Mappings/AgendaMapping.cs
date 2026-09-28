using Backend.Business.Agenda.Models;
using Backend.Business.Formaturas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento dos eventos da agenda da turma.
/// </summary>
/// <remarks>
/// <b>O índice único parcial em <c>(formatura_id, tipo)</c> é a decisão 2</b>, e não um detalhe de
/// performance: é ele que faz "uma colação e uma festa por turma" ser verdade mesmo com dois
/// cliques simultâneos — o service dá o código do erro, o banco dá a garantia. O filtro limita a
/// unicidade aos dois tipos únicos; reunião e prazo se repetem à vontade.
/// <para>
/// O índice <c>(formatura_id, data)</c> é o da própria tela: a lista sai ordenada por ele, e é a
/// única consulta que esta tabela serve.
/// </para>
/// <para>
/// <c>Data</c> é <c>date</c> e <c>Hora</c> é <c>time</c>, sem fuso (decisão 3): um evento é "19h no
/// ateliê", não um instante em UTC. É a exceção justificada à regra de datas do projeto, que vale
/// para instante — o que aconteceu — e não para o dia do calendário de quem vai.
/// </para>
/// </remarks>
public sealed class EventoDaTurmaMapping : IEntityTypeConfiguration<EventoDaTurma>
{
    /// <summary>Os tipos que existem no máximo uma vez por turma, como o Postgres os lê na coluna de texto.</summary>
    private static readonly string TiposUnicos = string.Join(", ", TiposDeEvento.Unicos.Select(tipo => $"'{tipo}'"));

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EventoDaTurma> builder)
    {
        builder.ToTable(
            "eventos_da_turma",
            tabela =>
            {
                tabela.HasCheckConstraint("ck_eventos_da_turma_cota", "cota_por_formando IS NULL OR cota_por_formando > 0");
                tabela.HasCheckConstraint("ck_eventos_da_turma_capacidade", "capacidade IS NULL OR capacidade > 0");
            }
        );

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Titulo).IsRequired().HasMaxLength(120);
        builder.Property(e => e.Tipo).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Situacao).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Local).HasMaxLength(200);
        builder.Property(e => e.Descricao).HasMaxLength(1000);

        builder.Ignore(e => e.Cancelado);

        builder.HasIndex(e => new { e.FormaturaId, e.Data });
        builder.HasIndex(e => new { e.FormaturaId, e.Tipo }).IsUnique().HasFilter($"tipo IN ({TiposUnicos})");

        builder.HasOne<Formatura>().WithMany().HasForeignKey(e => e.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}
