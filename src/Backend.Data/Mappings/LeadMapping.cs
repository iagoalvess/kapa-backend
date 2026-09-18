using Backend.Business.Leads.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento dos contatos deixados na página institucional.
/// </summary>
/// <remarks>
/// O índice em <c>(email, criado_em)</c> serve à janela de repetição, que é consultada em <b>todo</b>
/// envio do formulário público — é a consulta mais fácil de virar alvo de carga.
/// </remarks>
public sealed class LeadMapping : IEntityTypeConfiguration<Lead>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        builder.ToTable("leads");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Nome).IsRequired().HasMaxLength(120);
        builder.Property(l => l.Email).IsRequired().HasMaxLength(256);
        builder.Property(l => l.Telefone).HasMaxLength(32);
        builder.Property(l => l.Instituicao).IsRequired().HasMaxLength(160);
        builder.Property(l => l.Curso).IsRequired().HasMaxLength(160);
        builder.Property(l => l.Mensagem).HasMaxLength(1000);
        builder.Property(l => l.Origem).HasMaxLength(120);
        builder.Property(l => l.Meio).HasMaxLength(120);
        builder.Property(l => l.Campanha).HasMaxLength(120);
        builder.Property(l => l.PrivacidadeVersao).IsRequired().HasMaxLength(40);
        builder.Property(l => l.EnderecoIp).HasMaxLength(45);
        builder.Property(l => l.UserAgent).HasMaxLength(512);

        builder.HasIndex(l => new { l.Email, l.CriadoEm });
        builder.HasIndex(l => l.CriadoEm);
    }
}
