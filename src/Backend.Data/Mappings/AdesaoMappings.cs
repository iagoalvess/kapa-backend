using Backend.Business.Adesoes.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento das versões do termo de adesão.
/// </summary>
/// <remarks>
/// O índice único <c>(formatura_id, versao)</c> é o que torna "publicar" sinônimo de "inserir", como
/// em <c>documentos_legais</c>: duas publicações simultâneas não viram duas versões 2. Append-only por
/// gatilho, criado na migration.
/// </remarks>
public sealed class TermoDaFormaturaMapping : IEntityTypeConfiguration<TermoDaFormatura>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TermoDaFormatura> builder)
    {
        builder.ToTable("termos_de_adesao");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Conteudo).IsRequired();

        builder.HasIndex(t => new { t.FormaturaId, t.Versao }).IsUnique();

        builder.HasOne<Formatura>().WithMany().HasForeignKey(t => t.FormaturaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(t => t.PublicadoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeamento dos resumos do termo (Sprint 24).
/// </summary>
/// <remarks>
/// Chave no <c>termo_id</c>: um resumo por versão, e a segunda gravação é conflito de chave. Sem gatilho
/// append-only — o imutável é o termo, não o resumo; apagar a linha é o conserto de emergência de um
/// resumo ruim (decisão 9). Cascata do termo não existe porque o termo nunca é apagado.
/// </remarks>
public sealed class ResumoDoTermoMapping : IEntityTypeConfiguration<ResumoDoTermo>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ResumoDoTermo> builder)
    {
        builder.ToTable("resumos_de_termo");

        builder.HasKey(r => r.TermoId);

        builder.Property(r => r.Texto).IsRequired();
        builder.Property(r => r.Modelo).IsRequired().HasMaxLength(200);

        builder.HasOne<TermoDaFormatura>().WithOne().HasForeignKey<ResumoDoTermo>(r => r.TermoId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeamento das adesões.
/// </summary>
/// <remarks>
/// A cifra do CPF é aplicada pelo <c>AppDbContext</c>, como no perfil. <see cref="PropriedadeDoHmac"/>
/// é propriedade de sombra: o HMAC é detalhe de busca da persistência, e a entidade não precisa
/// saber que ele existe — quem o preenche é o repositório.
/// <para>
/// Índice único <c>(vinculo_id, termo_id)</c>: o clique duplo no "Aceito" não grava duas adesões à
/// mesma versão. Chaves em <c>Restrict</c>: adesão é prova, e prova não some em cascata. Append-only
/// por gatilho, criado na migration.
/// </para>
/// </remarks>
public sealed class AdesaoDoFormandoMapping : IEntityTypeConfiguration<AdesaoDoFormando>
{
    /// <summary>Nome da propriedade de sombra com o HMAC do CPF (coluna <c>cpf_hmac</c>).</summary>
    public const string PropriedadeDoHmac = "CpfHmac";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AdesaoDoFormando> builder)
    {
        builder.ToTable("adesoes");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.HashDoConteudo).IsRequired().HasMaxLength(64);
        builder.Property(a => a.EnderecoIp).IsRequired().HasMaxLength(45);
        builder.Property(a => a.UserAgent).IsRequired().HasMaxLength(512);
        builder.Property(a => a.EmailDoAceite).IsRequired().HasMaxLength(256);
        builder.Property(a => a.NomeCompleto).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Cpf).IsRequired().HasMaxLength(256);
        builder.Property(a => a.PlanoAceito).IsRequired();
        builder.Property<string>(PropriedadeDoHmac).IsRequired().HasMaxLength(64);

        builder.HasIndex(a => new { a.VinculoId, a.TermoId }).IsUnique();
        builder.HasIndex(PropriedadeDoHmac);

        builder.HasOne<Formatura>().WithMany().HasForeignKey(a => a.FormaturaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VinculoDeFormatura>().WithMany().HasForeignKey(a => a.VinculoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TermoDaFormatura>().WithMany().HasForeignKey(a => a.TermoId).OnDelete(DeleteBehavior.Restrict);
    }
}
