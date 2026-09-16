using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// Dá a cada despesa o lançamento que a criou, para as parcelas de uma parcelada se acharem.
    /// </summary>
    /// <remarks>
    /// As linhas que já existiam são reagrupadas por <c>(formatura, fornecedor, descrição, competência,
    /// total de parcelas)</c> — o que o lançamento gravava igual em todas as suas linhas antes de haver
    /// a coluna. Duas parceladas idênticas do mesmo fornecedor, na mesma competência e com o mesmo
    /// número de parcelas cairiam no mesmo lançamento; é o limite do que o dado antigo permite
    /// reconstruir, e a partir daqui o id nasce no serviço.
    /// <para>
    /// O <c>DEFAULT</c> zerado só existe para a coluna nascer preenchida na tabela que já tem linhas;
    /// ele cai logo depois, para que uma inserção sem lançamento falhe em vez de gravar um id vazio.
    /// </para>
    /// </remarks>
    public partial class AdicionaLancamentoNaDespesa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "lancamento_id",
                table: "despesas",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql(
                """
                UPDATE despesas AS d
                SET lancamento_id = g.lancamento_id
                FROM (
                    SELECT
                        formatura_id,
                        fornecedor_id,
                        descricao,
                        competencia,
                        total_de_parcelas,
                        gen_random_uuid() AS lancamento_id
                    FROM despesas
                    GROUP BY formatura_id, fornecedor_id, descricao, competencia, total_de_parcelas
                ) AS g
                WHERE d.formatura_id = g.formatura_id
                  AND d.fornecedor_id IS NOT DISTINCT FROM g.fornecedor_id
                  AND d.descricao = g.descricao
                  AND d.competencia = g.competencia
                  AND d.total_de_parcelas = g.total_de_parcelas;
                """);

            migrationBuilder.Sql("ALTER TABLE despesas ALTER COLUMN lancamento_id DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "ix_despesas_formatura_id_lancamento_id",
                table: "despesas",
                columns: new[] { "formatura_id", "lancamento_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_despesas_formatura_id_lancamento_id",
                table: "despesas");

            migrationBuilder.DropColumn(
                name: "lancamento_id",
                table: "despesas");
        }
    }
}
