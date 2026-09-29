using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class CancelamentoDeCompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "estorno_de_id",
                table: "outras_receitas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "comprovante_da_devolucao_id",
                table: "compras_de_convite",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "convites_cancelados",
                table: "compras_de_convite",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "devolvida_em",
                table: "compras_de_convite",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "valor_a_devolver_em_centavos",
                table: "compras_de_convite",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "valor_estornado_em_centavos",
                table: "compras_de_convite",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(
                "UPDATE compras_de_convite SET convites_cancelados = quantidade, "
                    + "valor_a_devolver_em_centavos = COALESCE(valor_pago_em_centavos, valor_em_centavos) WHERE status = 'ADevolver'"
            );

            migrationBuilder.CreateTable(
                name: "pedidos_de_cancelamento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    convite_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    motivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    pedido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    respondido_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    respondido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    motivo_da_resposta = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedidos_de_cancelamento", x => x.id);
                    table.ForeignKey(
                        name: "fk_pedidos_de_cancelamento_compras_de_convite_compra_id",
                        column: x => x.compra_id,
                        principalTable: "compras_de_convite",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pedidos_de_cancelamento_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outras_receitas_estorno_de_id",
                table: "outras_receitas",
                column: "estorno_de_id");

            migrationBuilder.CreateIndex(
                name: "ix_compras_de_convite_comprovante_da_devolucao_id",
                table: "compras_de_convite",
                column: "comprovante_da_devolucao_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_compras_de_convite_cancelados",
                table: "compras_de_convite",
                sql: "convites_cancelados BETWEEN 0 AND quantidade");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_de_cancelamento_formatura_id",
                table: "pedidos_de_cancelamento",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_de_cancelamento_formatura_id_status",
                table: "pedidos_de_cancelamento",
                columns: new[] { "formatura_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_de_cancelamento_um_aberto",
                table: "pedidos_de_cancelamento",
                column: "compra_id",
                unique: true,
                filter: "status = 'Aberto'");

            migrationBuilder.AddForeignKey(
                name: "fk_compras_de_convite_arquivos_comprovante_da_devolucao_id",
                table: "compras_de_convite",
                column: "comprovante_da_devolucao_id",
                principalTable: "arquivos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_outras_receitas_outras_receitas_estorno_de_id",
                table: "outras_receitas",
                column: "estorno_de_id",
                principalTable: "outras_receitas",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_compras_de_convite_arquivos_comprovante_da_devolucao_id",
                table: "compras_de_convite");

            migrationBuilder.DropForeignKey(
                name: "fk_outras_receitas_outras_receitas_estorno_de_id",
                table: "outras_receitas");

            migrationBuilder.DropTable(
                name: "pedidos_de_cancelamento");

            migrationBuilder.DropIndex(
                name: "ix_outras_receitas_estorno_de_id",
                table: "outras_receitas");

            migrationBuilder.DropIndex(
                name: "ix_compras_de_convite_comprovante_da_devolucao_id",
                table: "compras_de_convite");

            migrationBuilder.DropCheckConstraint(
                name: "ck_compras_de_convite_cancelados",
                table: "compras_de_convite");

            migrationBuilder.DropColumn(
                name: "estorno_de_id",
                table: "outras_receitas");

            migrationBuilder.DropColumn(
                name: "comprovante_da_devolucao_id",
                table: "compras_de_convite");

            migrationBuilder.DropColumn(
                name: "convites_cancelados",
                table: "compras_de_convite");

            migrationBuilder.DropColumn(
                name: "devolvida_em",
                table: "compras_de_convite");

            migrationBuilder.DropColumn(
                name: "valor_a_devolver_em_centavos",
                table: "compras_de_convite");

            migrationBuilder.DropColumn(
                name: "valor_estornado_em_centavos",
                table: "compras_de_convite");
        }
    }
}
