using Backend.Business.Legal.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento das versões de documento legal.
/// </summary>
/// <remarks>
/// O índice único em <c>(Tipo, Versao)</c> é o que torna "publicar" sinônimo de "inserir": não
/// há como existir duas versões 1 dos termos com textos diferentes.
/// </remarks>
public sealed class DocumentoLegalMapping : IEntityTypeConfiguration<DocumentoLegal>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DocumentoLegal> builder)
    {
        builder.ToTable("documentos_legais");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Tipo).IsRequired().HasMaxLength(40);
        builder.Property(d => d.Versao).IsRequired().HasMaxLength(40);
        builder.Property(d => d.Conteudo).IsRequired();

        builder.HasIndex(d => new { d.Tipo, d.Versao }).IsUnique();
        builder.HasIndex(d => new { d.Tipo, d.VigenteDesde });
    }
}

/// <summary>
/// Mapeamento dos registros de consentimento.
/// </summary>
/// <remarks>
/// Chaves estrangeiras em <c>Restrict</c>: consentimento é prova, e prova não some em cascata
/// porque alguém apagou um usuário ou um documento.
/// </remarks>
public sealed class ConsentimentoRegistradoMapping : IEntityTypeConfiguration<ConsentimentoRegistrado>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ConsentimentoRegistrado> builder)
    {
        builder.ToTable("consentimentos");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Versao).IsRequired().HasMaxLength(40);
        builder.Property(c => c.EnderecoIp).IsRequired().HasMaxLength(45);
        builder.Property(c => c.UserAgent).IsRequired().HasMaxLength(512);

        builder.HasIndex(c => new { c.UsuarioId, c.AceitoEm });

        builder.HasOne<Usuario>().WithMany().HasForeignKey(c => c.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentoLegal>().WithMany().HasForeignKey(c => c.DocumentoLegalId).OnDelete(DeleteBehavior.Restrict);
    }
}
