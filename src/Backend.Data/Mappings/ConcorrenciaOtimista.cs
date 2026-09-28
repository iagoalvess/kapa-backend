using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Data.Mappings;

/// <summary>
/// Token de concorrência das entidades que duas pessoas mexem ao mesmo tempo.
/// </summary>
/// <remarks>
/// Sem token, duas gravações simultâneas sobre a mesma linha terminam com a última vencendo
/// <b>em silêncio</b>: o Presidente corrige a data da colação enquanto o Tesoureiro renomeia a
/// turma, e uma das duas edições some sem ninguém saber. Com ele, a segunda gravação recebe
/// <c>DbUpdateConcurrencyException</c>, que o <c>ClassificadorDeExcecao</c> já traduz em 409
/// "recarregue e tente novamente".
/// <para>
/// O token é o <c>xmin</c> do próprio Postgres — a coluna de sistema que guarda a transação que
/// escreveu a linha. Não custa coluna, não custa migration de dados e não depende de ninguém
/// lembrar de incrementar uma versão na entidade, que é exatamente o modo como um campo
/// <c>Versao</c> escrito à mão para de funcionar.
/// </para>
/// <para>
/// Vai só onde há disputa real (Sprint 16, Parte B): <c>Assinatura</c>, <c>Formatura</c>,
/// <c>PerfilDoFormando</c> e <c>RefreshToken</c> — e, desde a auditoria de 22/09/2026, <c>Parcela</c>
/// e <c>Despesa</c>: desligar, encerrar item, repactuar e cancelar pedido mexiam na parcela sem a trava
/// da baixa, e uma baixa no meio terminava com parcela cancelada e recebimento ativo. Consulta crua
/// sobre elas precisa trazer a coluna (<c>SELECT *, xmin</c>): o <c>*</c> não inclui coluna de sistema. Tabela append-only não precisa — ela nunca é
/// alterada —, e pôr o token em tudo transformaria toda concorrência benigna em 409 na cara do
/// usuário.
/// </para>
/// </remarks>
public static class ConcorrenciaOtimista
{
    /// <summary>Nome da propriedade sombra que espelha a coluna de sistema <c>xmin</c>.</summary>
    /// <remarks>
    /// Propriedade <b>sombra</b>: a entidade do <c>Business</c> não ganha um campo que só existe
    /// por causa do Postgres, e o domínio continua testável sem banco.
    /// </remarks>
    public const string Versao = "xmin";

    /// <summary>Usa o <c>xmin</c> do Postgres como token de concorrência desta entidade.</summary>
    /// <remarks>
    /// O atalho <c>UseXminAsConcurrencyToken</c> saiu do Npgsql 10; o que restou é declarar a
    /// propriedade sombra à mão, que é exatamente o que ele fazia.
    /// <para>
    /// <c>HasColumnType("xid")</c> não é detalhe: é ele que diz ao EF que a coluna <b>já existe</b>,
    /// por ser do sistema. Sem o tipo, a migration criaria uma coluna <c>xmin</c> de verdade ao lado
    /// da do Postgres — e o token passaria a apontar para um número que ninguém escreve.
    /// </para>
    /// </remarks>
    /// <typeparam name="TEntidade">Entidade disputada.</typeparam>
    /// <param name="builder">Construtor da entidade.</param>
    public static EntityTypeBuilder<TEntidade> ComTokenDeConcorrencia<TEntidade>(this EntityTypeBuilder<TEntidade> builder)
        where TEntidade : class
    {
        builder.Property<uint>(Versao).HasColumnName(Versao).HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        return builder;
    }
}
