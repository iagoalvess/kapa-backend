using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// A agenda da turma nasce, e as duas datas da formatura se mudam para dentro dela.
    /// </summary>
    /// <remarks>
    /// <b>A ordem é obrigatória</b>: criar a tabela, copiar as colunas para dentro dela e só então
    /// derrubá-las. Invertida — que é como o scaffold a gerou —, a cópia lê coluna que já não existe
    /// e toda turma perde a colação e a festa sem erro nenhum.
    /// <para>
    /// A cópia pula o nulo: turma que nunca informou a data continua sem evento, e não ganha um
    /// cartão vazio na agenda. A situação é <c>AConfirmar</c> porque era isso que os campos diziam —
    /// "previsão" —, e o título é o que a tela mostra.
    /// </para>
    /// </remarks>
    public partial class CriaAgendaDaTurma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "eventos_da_turma",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    situacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    hora = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    local = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    descricao = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_da_turma", x => x.id);
                    table.ForeignKey(
                        name: "fk_eventos_da_turma_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_da_turma_formatura_id",
                table: "eventos_da_turma",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_da_turma_formatura_id_data",
                table: "eventos_da_turma",
                columns: new[] { "formatura_id", "data" });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_da_turma_formatura_id_tipo",
                table: "eventos_da_turma",
                columns: new[] { "formatura_id", "tipo" },
                unique: true,
                filter: "tipo IN ('Colacao', 'Festa')");

            migrationBuilder.Sql(
                """
                INSERT INTO eventos_da_turma (id, titulo, tipo, situacao, data, criado_em, atualizado_em, formatura_id)
                SELECT gen_random_uuid(), 'Colação de grau', 'Colacao', 'AConfirmar', f.previsao_de_colacao, now(), now(), f.id
                FROM formaturas f
                WHERE f.previsao_de_colacao IS NOT NULL;
                """
            );

            migrationBuilder.Sql(
                """
                INSERT INTO eventos_da_turma (id, titulo, tipo, situacao, data, criado_em, atualizado_em, formatura_id)
                SELECT gen_random_uuid(), 'Festa de formatura', 'Festa', 'AConfirmar', f.previsao_da_festa, now(), now(), f.id
                FROM formaturas f
                WHERE f.previsao_da_festa IS NOT NULL;
                """
            );

            migrationBuilder.DropColumn(
                name: "previsao_da_festa",
                table: "formaturas");

            migrationBuilder.DropColumn(
                name: "previsao_de_colacao",
                table: "formaturas");
        }

        /// <inheritdoc />
        /// <remarks>
        /// A volta desfaz na ordem inversa, e também copia: recriar as colunas vazias devolveria um
        /// banco sem as duas datas, que é perda de dado disfarçada de rollback. O que se perde na
        /// volta é o resto da agenda — reunião, prazo e o que a turma tiver marcado —, e isso não
        /// tem para onde ir.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "previsao_da_festa",
                table: "formaturas",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "previsao_de_colacao",
                table: "formaturas",
                type: "date",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE formaturas f
                SET previsao_de_colacao = e.data
                FROM eventos_da_turma e
                WHERE e.formatura_id = f.id AND e.tipo = 'Colacao';
                """
            );

            migrationBuilder.Sql(
                """
                UPDATE formaturas f
                SET previsao_da_festa = e.data
                FROM eventos_da_turma e
                WHERE e.formatura_id = f.id AND e.tipo = 'Festa';
                """
            );

            migrationBuilder.DropTable(
                name: "eventos_da_turma");
        }
    }
}
