using Backend.Business.Cobrancas.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Notificacoes.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Mapeamento dos degraus da régua.
/// </summary>
/// <remarks>
/// O índice único <c>(formatura_id, gatilho, dias_de_deslocamento)</c> é a identidade do degrau: é
/// por ele que o degrau acha o texto em <c>ReguaDoKapa</c>, e é ele que impede dois D+3 na mesma turma.
/// </remarks>
public sealed class RegraDeNotificacaoMapping : IEntityTypeConfiguration<RegraDeNotificacao>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RegraDeNotificacao> builder)
    {
        builder.ToTable("regras_de_notificacao");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Gatilho).HasConversion<string>().HasMaxLength(20);

        builder
            .HasIndex(r => new
            {
                r.FormaturaId,
                r.Gatilho,
                r.DiasDeDeslocamento,
            })
            .IsUnique();

        builder.HasOne<Formatura>().WithMany().HasForeignKey(r => r.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeamento do histórico de envios.
/// </summary>
/// <remarks>
/// O índice único é <b>o</b> requisito da sprint (decisão 2): o job pode rodar duas vezes, a
/// instância pode duplicar e o worker pode reiniciar no meio — a pessoa recebe uma mensagem.
/// <para>
/// <c>NULLS NOT DISTINCT</c> porque o resumo à tesouraria não tem parcela: no padrão do Postgres,
/// dois nulos são diferentes, e o mesmo resumo entraria de novo a cada rodada do dia.
/// </para>
/// <para>
/// O destinatário entra na chave por um motivo concreto: um resumo de régua vai para <b>cada</b>
/// membro da tesouraria, e com a chave de três colunas o segundo tesoureiro nunca receberia. A
/// garantia que a decisão pede — "a pessoa recebe uma mensagem" — é exatamente esta.
/// </para>
/// </remarks>
public sealed class NotificacaoEnviadaMapping : IEntityTypeConfiguration<NotificacaoEnviada>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NotificacaoEnviada> builder)
    {
        builder.ToTable("notificacoes_enviadas");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.Destinatario).IsRequired().HasMaxLength(256);
        builder.Property(n => n.Assunto).IsRequired().HasMaxLength(300);
        builder.Property(n => n.Erro).HasMaxLength(500);

        builder
            .HasIndex(
                n => new
                {
                    n.FormaturaId,
                    n.ParcelaId,
                    n.RegraId,
                    n.DataDeReferencia,
                    n.Destinatario,
                },
                "ix_notificacoes_enviadas_idempotencia"
            )
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ix_notificacoes_enviadas_idempotencia");

        builder.HasIndex(n => new { n.FormaturaId, n.DataDeReferencia });
        builder.HasIndex(n => new { n.FormaturaId, n.Status });

        builder.HasOne<RegraDeNotificacao>().WithMany().HasForeignKey(n => n.RegraId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Parcela>().WithMany().HasForeignKey(n => n.ParcelaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VinculoDeFormatura>().WithMany().HasForeignKey(n => n.VinculoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(n => n.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeamento das preferências do titular.
/// </summary>
/// <remarks>
/// Uma linha por <c>(vínculo, tipo)</c>, com índice único: a ausência de linha é "recebe", e o
/// índice é o que impede duas escolhas contraditórias para o mesmo assunto.
/// <para>
/// Sem chave para o e-mail na fila em <c>NotificacaoEnviada</c>: a fila é apagada por retenção e o
/// histórico precisa sobreviver a ela. O id fica como referência solta, e a conferência simplesmente
/// não acha o que já sumiu.
/// </para>
/// </remarks>
public sealed class PreferenciaDeNotificacaoMapping : IEntityTypeConfiguration<PreferenciaDeNotificacao>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PreferenciaDeNotificacao> builder)
    {
        builder.ToTable("preferencias_de_notificacao");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Tipo).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(p => new { p.VinculoId, p.Tipo }).IsUnique();

        builder.HasOne<VinculoDeFormatura>().WithMany().HasForeignKey(p => p.VinculoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Formatura>().WithMany().HasForeignKey(p => p.FormaturaId).OnDelete(DeleteBehavior.Restrict);
    }
}
