using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// Leva as turmas que ainda não contrataram para <c>Ativa</c> e derruba o índice de rascunho.
    /// </summary>
    /// <remarks>
    /// <c>Rascunho</c> e <c>AguardandoPagamento</c> deixaram de existir em 18/09/2026: a turma nasce
    /// ativa no plano gratuito, e quem limita o que ela faz é o plano. Linha gravada com um desses
    /// dois textos não casaria com nenhum valor do enum — quem lesse a turma estouraria.
    /// <para>
    /// O índice único parcial filtrava <c>status = 'Rascunho'</c>: sem esse status ele nunca mais
    /// dispararia. A regra que ele guardava virou <c>ExisteGratuitaCriadaPor</c>, no service.
    /// </para>
    /// <para>
    /// <b>O <c>Down</c> é aproximado</b>, e não tem como não ser: os dois status viram um só na ida,
    /// e a volta os separa pelo que dá para observar — turma com assinatura pendente estava em
    /// checkout (<c>AguardandoPagamento</c>), turma sem assinatura nenhuma era rascunho. Quem pagou
    /// continua <c>Ativa</c>.
    /// </para>
    /// </remarks>
    public partial class TurmaNasceAtivaNoGratuito : Migration
    {
        /// <summary>
        /// A data de ativação de quem nunca ativou vira a de criação: é quando a turma começou.
        /// </summary>
        private const string ParaAtiva = """
            UPDATE formaturas
               SET status = 'Ativa',
                   ativada_em = COALESCE(ativada_em, criado_em)
             WHERE status IN ('Rascunho', 'AguardandoPagamento');
            """;

        private const string DeVoltaAosDoisStatus = """
            UPDATE formaturas f
               SET status = CASE
                       WHEN EXISTS (
                           SELECT 1 FROM assinaturas a
                            WHERE a.formatura_id = f.id AND a.status = 'Pendente'
                       ) THEN 'AguardandoPagamento'
                       ELSE 'Rascunho'
                   END
             WHERE f.status = 'Ativa'
               AND NOT EXISTS (
                       SELECT 1 FROM assinaturas a
                        WHERE a.formatura_id = f.id AND a.status IN ('Ativa', 'Cancelada', 'Vencida')
                   );
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "ix_formaturas_rascunho_por_criador", table: "formaturas");
            migrationBuilder.Sql(ParaAtiva);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DeVoltaAosDoisStatus);
            migrationBuilder.CreateIndex(
                name: "ix_formaturas_rascunho_por_criador",
                table: "formaturas",
                column: "criado_por_usuario_id",
                unique: true,
                filter: "status = 'Rascunho'"
            );
        }
    }
}
