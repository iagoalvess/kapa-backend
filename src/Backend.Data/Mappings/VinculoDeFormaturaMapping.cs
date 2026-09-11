using Backend.Business.Formaturas.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento do vínculo entre usuário e formatura.
/// </summary>
/// <remarks>
/// O índice único em <c>(UsuarioId, FormaturaId)</c> é a regra "uma pessoa tem um papel por
/// turma" no banco, e não só no service: duas requisições simultâneas de convite passariam
/// pela checagem do service e criariam dois vínculos, cada um com um papel diferente.
/// <para>
/// Chaves estrangeiras em <c>Restrict</c>: vínculo é histórico da turma e se desativa, nunca se
/// apaga. Um <c>DELETE</c> avulso em formatura ou usuário não pode levar tudo junto em cascata.
/// </para>
/// </remarks>
public sealed class VinculoDeFormaturaMapping : IEntityTypeConfiguration<VinculoDeFormatura>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VinculoDeFormatura> builder)
    {
        builder.ToTable("vinculos_de_formatura");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Papel).IsRequired().HasMaxLength(20);

        builder.HasIndex(v => new { v.UsuarioId, v.FormaturaId }).IsUnique();

        builder.HasOne<Usuario>().WithMany().HasForeignKey(v => v.UsuarioId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Formatura>().WithMany().HasForeignKey(v => v.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}
