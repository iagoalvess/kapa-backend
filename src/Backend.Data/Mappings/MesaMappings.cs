using Backend.Business.Festa.Models;
using Backend.Business.Formaturas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento das mesas do jantar.
/// </summary>
/// <remarks>
/// Os dois <c>CHECK</c> são a regra, não enfeite: mesa tem ao menos um lugar, e mesa reservada nunca
/// tem dono (P3) — o service dá a mensagem, o banco garante. O índice em <c>vinculo_id</c> serve à
/// contagem das mesas de um formando, que acontece a cada atribuição. As coordenadas andam juntas: a mesa
/// está no mapa com as duas, ou fora dele sem nenhuma.
/// </remarks>
public sealed class MesaMapping : IEntityTypeConfiguration<Mesa>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Mesa> builder)
    {
        builder.ToTable(
            "mesas",
            tabela =>
            {
                tabela.HasCheckConstraint("ck_mesas_lugares", "lugares > 0");
                tabela.HasCheckConstraint("ck_mesas_reservada_sem_dono", "NOT (reservada AND vinculo_id IS NOT NULL)");
                tabela.HasCheckConstraint("ck_mesas_posicao", "(x IS NULL) = (y IS NULL)");
            }
        );

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Identificacao).IsRequired().HasMaxLength(60);
        builder.Property(m => m.Observacao).HasMaxLength(200);
        builder.Property(m => m.Formato).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(m => m.VinculoId);

        builder.HasOne<VinculoDeFormatura>().WithMany().HasForeignKey(m => m.VinculoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(m => m.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeamento do salão do jantar.
/// </summary>
/// <remarks>
/// Um por turma, e o índice único é quem garante. Os elementos são uma coleção complexa gravada em
/// <c>jsonb</c>: não têm identidade, ninguém os consulta por dentro, e o mapa se lê e se grava inteiro.
/// Tipo e cor vão como texto dentro do JSON, pelo mesmo motivo de toda coluna de enum do projeto.
/// </remarks>
public sealed class SalaoMapping : IEntityTypeConfiguration<Salao>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Salao> builder)
    {
        builder.ToTable(
            "saloes",
            tabela =>
            {
                tabela.HasCheckConstraint("ck_saloes_largura", "largura > 0");
                tabela.HasCheckConstraint("ck_saloes_altura", "altura > 0");
            }
        );

        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.FormaturaId).IsUnique();

        builder.ComplexCollection(
            s => s.Elementos,
            elemento =>
            {
                elemento.ToJson("elementos");
                elemento.Property(e => e.Tipo).HasConversion<string>();
                elemento.Property(e => e.Cor).HasConversion<string>();
            }
        );

        builder.HasOne<Formatura>().WithMany().HasForeignKey(s => s.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}
