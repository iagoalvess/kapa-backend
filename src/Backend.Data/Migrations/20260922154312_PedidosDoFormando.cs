using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class PedidosDoFormando : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "abertura_de_vendas",
                table: "itens_de_cobranca",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "estoque",
                table: "itens_de_cobranca",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "item_da_festa_id",
                table: "itens_de_cobranca",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "limite_por_formando",
                table: "itens_de_cobranca",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "pedidos_ate_dia",
                table: "itens_de_cobranca",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reservados",
                table: "itens_de_cobranca",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "sob_demanda",
                table: "itens_de_cobranca",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "pedidos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_de_cobranca_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantidade = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pedido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    cancelado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedidos", x => x.id);
                    table.CheckConstraint("ck_pedidos_quantidade", "quantidade >= 1");
                    table.ForeignKey(
                        name: "fk_pedidos_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pedidos_itens_de_cobranca_item_de_cobranca_id",
                        column: x => x.item_de_cobranca_id,
                        principalTable: "itens_de_cobranca",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pedidos_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_itens_de_cobranca_item_da_festa",
                table: "itens_de_cobranca",
                column: "item_da_festa_id",
                unique: true,
                filter: "item_da_festa_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_itens_de_cobranca_estoque",
                table: "itens_de_cobranca",
                sql: "estoque IS NULL OR estoque >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_itens_de_cobranca_reservados",
                table: "itens_de_cobranca",
                sql: "reservados >= 0 AND (estoque IS NULL OR reservados <= estoque)");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_formatura_id",
                table: "pedidos",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_item_de_cobranca_id_status",
                table: "pedidos",
                columns: new[] { "item_de_cobranca_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_vinculo_id_item_de_cobranca_id",
                table: "pedidos",
                columns: new[] { "vinculo_id", "item_de_cobranca_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_itens_de_cobranca_itens_da_festa_item_da_festa_id",
                table: "itens_de_cobranca",
                column: "item_da_festa_id",
                principalTable: "itens_da_festa",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_itens_de_cobranca_itens_da_festa_item_da_festa_id",
                table: "itens_de_cobranca");

            migrationBuilder.DropTable(
                name: "pedidos");

            migrationBuilder.DropIndex(
                name: "ix_itens_de_cobranca_item_da_festa",
                table: "itens_de_cobranca");

            migrationBuilder.DropCheckConstraint(
                name: "ck_itens_de_cobranca_estoque",
                table: "itens_de_cobranca");

            migrationBuilder.DropCheckConstraint(
                name: "ck_itens_de_cobranca_reservados",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "abertura_de_vendas",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "estoque",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "item_da_festa_id",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "limite_por_formando",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "pedidos_ate_dia",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "reservados",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "sob_demanda",
                table: "itens_de_cobranca");
        }
    }
}
