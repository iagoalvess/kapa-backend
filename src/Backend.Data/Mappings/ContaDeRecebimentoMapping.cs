using Backend.Business.Formaturas.Models;
using Backend.Business.Recebimentos.Models;
using Backend.Business.Recebimentos.Validators;
using Backend.Business.Usuarios.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento da conta de recebimento.
/// </summary>
/// <remarks>
/// O índice único em <c>formatura_id</c> é a regra "uma por turma" no banco: duas primeiras gravações
/// simultâneas não viram duas contas. Índice <b>nomeado</b>, como em <c>assinaturas</c>: o sem nome na
/// mesma coluna é o da convenção de <c>EntidadeDaFormatura</c>, e declarar outro o substituiria.
/// <para>
/// Tamanhos: e-mail de chave vai até 77; nome e cidade seguem os do cadastro do formando; os campos
/// dos demais meios seguem o validator, que é onde o limite é escrito uma vez.
/// </para>
/// <para>
/// Tudo anulável menos a formatura: cada meio é um grupo de colunas, e grupo vazio é meio desligado
/// (P4 de 21/09/2026 — a chave PIX deixou de ser obrigatória). Quem exige ao menos um meio é o
/// validator, porque a regra é "pelo menos um destes três" e nenhum <c>NOT NULL</c> diz isso.
/// </para>
/// </remarks>
public sealed class ContaDeRecebimentoMapping : IEntityTypeConfiguration<ContaDeRecebimento>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ContaDeRecebimento> builder)
    {
        builder.ToTable("contas_de_recebimento");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.TipoDeChave).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Chave).HasMaxLength(ChavePix.TamanhoMaximoDoEmail);
        builder.Property(c => c.NomeDoTitular).HasMaxLength(200);
        builder.Property(c => c.Cidade).HasMaxLength(100);
        builder.Property(c => c.BancoDaChave).HasMaxLength(ContaDeRecebimentoValidator.TamanhoDoCampoBancario);

        builder.Property(c => c.Banco).HasMaxLength(ContaDeRecebimentoValidator.TamanhoDoCampoBancario);
        builder.Property(c => c.Agencia).HasMaxLength(ContaDeRecebimentoValidator.TamanhoDoCampoBancario);
        builder.Property(c => c.Conta).HasMaxLength(ContaDeRecebimentoValidator.TamanhoDoCampoBancario);
        builder.Property(c => c.TipoDeConta).HasMaxLength(ContaDeRecebimentoValidator.TamanhoDoCampoBancario);
        builder.Property(c => c.TitularDaConta).HasMaxLength(ContaDeRecebimentoValidator.TamanhoDoCampoBancario);
        builder.Property(c => c.DinheiroCom).HasMaxLength(ContaDeRecebimentoValidator.TamanhoDoCampoBancario);
        builder.Property(c => c.DinheiroOnde).HasMaxLength(ContaDeRecebimentoValidator.TamanhoDoCampoBancario);

        builder.Ignore(c => c.Conferida);

        builder
            .HasIndex(c => c.FormaturaId, "ix_contas_de_recebimento_uma_por_formatura")
            .IsUnique()
            .HasDatabaseName("ix_contas_de_recebimento_uma_por_formatura");

        builder.HasOne<Formatura>().WithMany().HasForeignKey(c => c.FormaturaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(c => c.ConferidaPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}
