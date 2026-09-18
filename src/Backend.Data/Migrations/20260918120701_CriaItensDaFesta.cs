using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class CriaItensDaFesta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "item_da_festa_id",
                table: "despesas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "itens_da_festa",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    categoria = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    o_que_inclui = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    rateio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valor_previsto_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    quantidade_estimada = table.Column<int>(type: "integer", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    cancelado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_itens_da_festa", x => x.id);
                    table.ForeignKey(
                        name: "fk_itens_da_festa_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_despesas_formatura_id_item_da_festa_id",
                table: "despesas",
                columns: new[] { "formatura_id", "item_da_festa_id" });

            migrationBuilder.CreateIndex(
                name: "ix_despesas_item_da_festa_id",
                table: "despesas",
                column: "item_da_festa_id");

            migrationBuilder.CreateIndex(
                name: "ix_itens_da_festa_formatura_id",
                table: "itens_da_festa",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_itens_da_festa_formatura_id_ordem",
                table: "itens_da_festa",
                columns: new[] { "formatura_id", "ordem" });

            migrationBuilder.AddForeignKey(
                name: "fk_despesas_itens_da_festa_item_da_festa_id",
                table: "despesas",
                column: "item_da_festa_id",
                principalTable: "itens_da_festa",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_despesas_itens_da_festa_item_da_festa_id",
                table: "despesas");

            migrationBuilder.DropTable(
                name: "itens_da_festa");

            migrationBuilder.DropIndex(
                name: "ix_despesas_formatura_id_item_da_festa_id",
                table: "despesas");

            migrationBuilder.DropIndex(
                name: "ix_despesas_item_da_festa_id",
                table: "despesas");

            migrationBuilder.DropColumn(
                name: "item_da_festa_id",
                table: "despesas");
        }
    }
}
