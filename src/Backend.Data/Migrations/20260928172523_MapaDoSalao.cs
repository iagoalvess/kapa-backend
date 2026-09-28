using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class MapaDoSalao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "formato",
                table: "mesas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Redonda");

            migrationBuilder.AddColumn<bool>(
                name: "girada",
                table: "mesas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "x",
                table: "mesas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "y",
                table: "mesas",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "saloes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    largura = table.Column<int>(type: "integer", nullable: false),
                    altura = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    elementos = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saloes", x => x.id);
                    table.CheckConstraint("ck_saloes_altura", "altura > 0");
                    table.CheckConstraint("ck_saloes_largura", "largura > 0");
                    table.ForeignKey(
                        name: "fk_saloes_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_mesas_posicao",
                table: "mesas",
                sql: "(x IS NULL) = (y IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_saloes_formatura_id",
                table: "saloes",
                column: "formatura_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "saloes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_mesas_posicao",
                table: "mesas");

            migrationBuilder.DropColumn(
                name: "formato",
                table: "mesas");

            migrationBuilder.DropColumn(
                name: "girada",
                table: "mesas");

            migrationBuilder.DropColumn(
                name: "x",
                table: "mesas");

            migrationBuilder.DropColumn(
                name: "y",
                table: "mesas");
        }
    }
}
