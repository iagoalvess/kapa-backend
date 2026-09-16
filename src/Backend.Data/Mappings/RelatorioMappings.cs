using Backend.Business.Arquivos.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Relatorios.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento das solicitações de relatório.
/// </summary>
/// <remarks>
/// O índice <c>(status, criado_em)</c> é o da fila do worker: ele pergunta "o que está na fila, da
/// mais antiga" de todas as turmas de uma vez, sem a coluna da formatura na frente. É a única
/// consulta do projeto que atravessa formaturas por desenho, e o índice acompanha isso.
/// <para>
/// O <c>(expira_em)</c> parcial atende a limpeza: só a linha que ainda tem arquivo interessa a ela,
/// e as demais — a esmagadora maioria com o tempo — ficam fora do índice.
/// </para>
/// <para>
/// A chave do arquivo é <c>SetNull</c>, e não <c>Restrict</c>: o arquivo é derivado e apagável, e o
/// que sobra aqui é o registro de que o relatório foi pedido. O oposto das despesas, onde o
/// comprovante é prova e não pode sumir.
/// </para>
/// </remarks>
public sealed class SolicitacaoDeRelatorioMapping : IEntityTypeConfiguration<SolicitacaoDeRelatorio>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SolicitacaoDeRelatorio> builder)
    {
        builder.ToTable("solicitacoes_de_relatorio");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Tipo).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Motivo).HasMaxLength(500);
        builder.Property(s => s.Categoria).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.SituacaoDaDespesa).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.SituacaoDaParcela).HasConversion<string>().HasMaxLength(20);

        builder.Ignore(s => s.Periodo);
        builder.Ignore(s => s.Filtro);

        builder.HasIndex(s => new { s.Status, s.CriadoEm });
        builder.HasIndex(s => s.ExpiraEm).HasFilter("arquivo_id is not null");

        builder.HasOne<Usuario>().WithMany().HasForeignKey(s => s.SolicitadaPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Arquivo>().WithMany().HasForeignKey(s => s.ArquivoId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(s => s.FormaturaId).OnDelete(DeleteBehavior.Restrict);

        // Sem chave estrangeira nos ids do recorte, e de propósito: eles são um pedaço do **pedido**,
        // não um vínculo. Apagar um fornecedor não pode derrubar o registro de que alguém pediu o
        // relatório dele; o que acontece é o subtítulo ficar sem o nome, que `NomesDoFiltro` já trata.
        builder.HasIndex(s => new
        {
            s.Tipo,
            s.Status,
            s.De,
            s.Ate,
        });
    }
}
