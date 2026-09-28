using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class MesasDoJantar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mesas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identificacao = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    lugares = table.Column<int>(type: "integer", nullable: false),
                    observacao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reservada = table.Column<bool>(type: "boolean", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mesas", x => x.id);
                    table.CheckConstraint("ck_mesas_lugares", "lugares > 0");
                    table.CheckConstraint("ck_mesas_reservada_sem_dono", "NOT (reservada AND vinculo_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_mesas_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mesas_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mesas_formatura_id",
                table: "mesas",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_mesas_vinculo_id",
                table: "mesas",
                column: "vinculo_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mesas");
        }
    }
}
