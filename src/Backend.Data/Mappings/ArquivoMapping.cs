using Backend.Business.Arquivos.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento dos metadados de arquivo.
/// </summary>
public sealed class ArquivoMapping : IEntityTypeConfiguration<Arquivo>
{
    /// <inheritdoc />
    /// <remarks>
    /// A chave é única: duas linhas apontando para o mesmo objeto fariam a remoção de uma apagar
    /// o conteúdo da outra.
    /// <para>
    /// A exclusão do usuário é <c>Restrict</c>, e não cascata. Apagar um usuário não deve levar
    /// junto os arquivos que ele enviou — nem, pior, deixar objetos órfãos no provedor, já que o
    /// cascata acontece no banco e o provedor não fica sabendo.
    /// </para>
    /// </remarks>
    public void Configure(EntityTypeBuilder<Arquivo> builder)
    {
        builder.ToTable("arquivos");

        builder.Property(a => a.Nome).IsRequired().HasMaxLength(255);
        builder.Property(a => a.Chave).IsRequired().HasMaxLength(500);
        builder.Property(a => a.ContentType).IsRequired().HasMaxLength(150);
        builder.Property(a => a.Categoria).IsRequired().HasMaxLength(60);

        builder.HasIndex(a => a.Chave).IsUnique();
        builder.HasIndex(a => new
        {
            a.EnviadoPorId,
            a.Categoria,
            a.CriadoEm,
        });

        builder.HasOne<Usuario>().WithMany().HasForeignKey(a => a.EnviadoPorId).OnDelete(DeleteBehavior.Restrict);
    }
}
