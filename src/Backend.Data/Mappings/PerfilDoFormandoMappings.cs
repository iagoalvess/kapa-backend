using Backend.Business.Arquivos.Models;
using Backend.Business.Formandos.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento do cadastro do formando.
/// </summary>
/// <remarks>
/// A cifra do CPF é aplicada pelo <c>AppDbContext</c>, que tem a chave — é uma linha na coluna, e
/// nenhum service ou repositório sabe que ela existe.
/// <para>
/// O contato de emergência é tipo próprio nas colunas do perfil
/// (<c>contato_de_emergencia_nome</c>): é lido sempre junto, e tabela à
/// parte seria um <c>JOIN</c> a mais para nada. Navegação obrigatória: a instância existe mesmo
/// com as colunas todas nulas, e ninguém precisa testar nulo antes de ler um campo.
/// </para>
/// <para>
/// A foto em <c>SetNull</c>: arquivo removido pelo módulo de arquivos só tira a foto do cadastro.
/// A completude gravada fica defasada até a próxima edição — o módulo de arquivos não sabe de
/// perfil, e o caso é raro demais para ensinar a ele.
/// </para>
/// </remarks>
public sealed class PerfilDoFormandoMapping : IEntityTypeConfiguration<PerfilDoFormando>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PerfilDoFormando> builder)
    {
        builder.ToTable("perfis_de_formandos");

        builder.ComTokenDeConcorrencia();

        builder.HasKey(p => p.Id);

        builder.Property(p => p.NomeCompleto).HasMaxLength(200);
        builder.Property(p => p.Cpf).HasMaxLength(256);
        builder.Property(p => p.Telefone).HasMaxLength(16);

        builder.OwnsOne(
            p => p.ContatoDeEmergencia,
            contato =>
            {
                contato.Property(c => c.Nome).HasMaxLength(200);
                contato.Property(c => c.Telefone).HasMaxLength(16);
                contato.Property(c => c.Parentesco).HasMaxLength(50);
            }
        );

        builder.Navigation(p => p.ContatoDeEmergencia).IsRequired();

        builder.HasIndex(p => p.VinculoId).IsUnique();

        builder.HasOne<Formatura>().WithMany().HasForeignKey(p => p.FormaturaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VinculoDeFormatura>().WithMany().HasForeignKey(p => p.VinculoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Arquivo>().WithMany().HasForeignKey(p => p.FotoArquivoId).OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Mapeamento do registro de correções da comissão.
/// </summary>
/// <remarks>Chaves em <c>Restrict</c>: é registro de auditoria, e registro não some em cascata.</remarks>
public sealed class CorrecaoDePerfilMapping : IEntityTypeConfiguration<CorrecaoDePerfil>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CorrecaoDePerfil> builder)
    {
        builder.ToTable("correcoes_de_perfil");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Secoes).IsRequired().HasMaxLength(100);

        builder.HasIndex(c => c.PerfilId);

        builder.HasOne<PerfilDoFormando>().WithMany().HasForeignKey(c => c.PerfilId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(c => c.AutorUsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}
