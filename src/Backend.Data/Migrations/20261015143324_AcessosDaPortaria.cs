using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AcessosDaPortaria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acessos_da_portaria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ativado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revogado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acessos_da_portaria", x => x.id);
                    table.ForeignKey(
                        name: "fk_acessos_da_portaria_eventos_da_turma_evento_id",
                        column: x => x.evento_id,
                        principalTable: "eventos_da_turma",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_acessos_da_portaria_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acessos_da_portaria_evento_id",
                table: "acessos_da_portaria",
                column: "evento_id");

            migrationBuilder.CreateIndex(
                name: "ix_acessos_da_portaria_formatura_id",
                table: "acessos_da_portaria",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_acessos_da_portaria_token",
                table: "acessos_da_portaria",
                column: "token",
                unique: true,
                filter: "token IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acessos_da_portaria");
        }
    }
}
