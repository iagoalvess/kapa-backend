using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Formaturas anteriores ao ciclo de vida já operavam como ativas e entram como <c>Ativa</c>. O
    /// default é removido logo depois: um insert sem status tem de falhar, e não virar turma ativa
    /// sem pagamento.
    /// </remarks>
    public partial class AdicionaCicloDeVidaDaFormatura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ano",
                table: "formaturas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ativada_em",
                table: "formaturas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "criado_por_usuario_id",
                table: "formaturas",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "curso",
                table: "formaturas",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "encerrada_em",
                table: "formaturas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "instituicao",
                table: "formaturas",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "previsao_de_colacao",
                table: "formaturas",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "quantidade_estimada_de_formandos",
                table: "formaturas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "semestre",
                table: "formaturas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "formaturas",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Ativa");

            migrationBuilder.Sql("ALTER TABLE formaturas ALTER COLUMN status DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "ix_formaturas_rascunho_por_criador",
                table: "formaturas",
                column: "criado_por_usuario_id",
                unique: true,
                filter: "status = 'Rascunho'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_formaturas_rascunho_por_criador",
                table: "formaturas");

            migrationBuilder.DropColumn(
                name: "ano",
                table: "formaturas");

            migrationBuilder.DropColumn(
                name: "ativada_em",
                table: "formaturas");

            migrationBuilder.DropColumn(
                name: "criado_por_usuario_id",
                table: "formaturas");

            migrationBuilder.DropColumn(
                name: "curso",
                table: "formaturas");

            migrationBuilder.DropColumn(
                name: "encerrada_em",
                table: "formaturas");

            migrationBuilder.DropColumn(
                name: "instituicao",
                table: "formaturas");

            migrationBuilder.DropColumn(
                name: "previsao_de_colacao",
                table: "formaturas");

            migrationBuilder.DropColumn(
                name: "quantidade_estimada_de_formandos",
                table: "formaturas");

            migrationBuilder.DropColumn(
                name: "semestre",
                table: "formaturas");

            migrationBuilder.DropColumn(
                name: "status",
                table: "formaturas");
        }
    }
}
