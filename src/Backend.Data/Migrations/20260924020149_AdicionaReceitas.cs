using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaReceitas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "receitas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    origem = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    categoria = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receitas", x => x.id);
                    table.ForeignKey(
                        name: "fk_receitas_documentos_documento_id",
                        column: x => x.documento_id,
                        principalTable: "documentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_receitas_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_receitas_documento_id",
                table: "receitas",
                column: "documento_id");

            migrationBuilder.CreateIndex(
                name: "ix_receitas_formatura_id",
                table: "receitas",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_receitas_formatura_id_status_data",
                table: "receitas",
                columns: new[] { "formatura_id", "status", "data" });

            migrationBuilder.CreateIndex(
                name: "ix_receitas_lancamento_unico",
                table: "receitas",
                columns: new[] { "formatura_id", "descricao", "origem", "data" },
                unique: true,
                filter: "status <> 'Cancelada'")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receitas");

        }
    }
}
