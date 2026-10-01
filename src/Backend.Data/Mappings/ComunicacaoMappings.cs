using Backend.Business.Arquivos.Models;
using Backend.Business.Comunicacao.Models;
using Backend.Business.Formaturas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento dos avisos do mural.
/// </summary>
/// <remarks>
/// Sem uma linha sequer sobre o isolamento: o índice e o filtro global de <c>FormaturaId</c> vêm da
/// convenção de <c>EntidadeDaFormatura</c>, aplicada pelo <c>AppDbContext</c>.
/// <para>
/// ponytail: sem índice próprio para o feed — são dezenas de avisos por turma, e o de
/// <c>formatura_id</c> já corta. Com milhares, <c>(formatura_id, fixado, criado_em)</c>.
/// </para>
/// </remarks>
public sealed class AvisoMapping : IEntityTypeConfiguration<Aviso>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Aviso> builder)
    {
        builder.ToTable("avisos");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Titulo).IsRequired().HasMaxLength(150);
        builder.Property(a => a.Conteudo).IsRequired().HasMaxLength(20_000);
        builder.Property(a => a.Visibilidade).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Formatura>().WithMany().HasForeignKey(a => a.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeamento do acervo de documentos.
/// </summary>
/// <remarks>
/// Chave para o arquivo em <c>Restrict</c>: o service apaga o documento antes do arquivo, e o banco é
/// a segunda barreira contra um documento apontando para bytes que sumiram.
/// </remarks>
public sealed class DocumentoMapping : IEntityTypeConfiguration<Documento>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Documento> builder)
    {
        builder.ToTable("documentos");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Titulo).IsRequired().HasMaxLength(150);
        builder.Property(d => d.Categoria).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Visibilidade).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Arquivo>().WithMany().HasForeignKey(d => d.ArquivoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(d => d.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}
