using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RevisaoDePagamentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cobranca_id",
                table: "recebimentos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "valores_a_devolver",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    item_de_cobranca_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    parcela_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cobranca_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolvido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolvido_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comprovante_arquivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    despesa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_valores_a_devolver", x => x.id);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_arquivos_comprovante_arquivo_id",
                        column: x => x.comprovante_arquivo_id,
                        principalTable: "arquivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_cobrancas_bancarias_cobranca_id",
                        column: x => x.cobranca_id,
                        principalTable: "cobrancas_bancarias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_despesas_despesa_id",
                        column: x => x.despesa_id,
                        principalTable: "despesas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_itens_de_cobranca_item_de_cobranca_id",
                        column: x => x.item_de_cobranca_id,
                        principalTable: "itens_de_cobranca",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_parcelas_parcela_id",
                        column: x => x.parcela_id,
                        principalTable: "parcelas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_pedidos_pedido_id",
                        column: x => x.pedido_id,
                        principalTable: "pedidos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_usuarios_resolvido_por_usuario_id",
                        column: x => x.resolvido_por_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recebimentos_cobranca_id",
                table: "recebimentos",
                column: "cobranca_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_cobranca_id",
                table: "valores_a_devolver",
                column: "cobranca_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_comprovante_arquivo_id",
                table: "valores_a_devolver",
                column: "comprovante_arquivo_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_criado_em",
                table: "valores_a_devolver",
                column: "criado_em",
                filter: "status = 'ADevolver'");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_despesa_id",
                table: "valores_a_devolver",
                column: "despesa_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_formatura_id",
                table: "valores_a_devolver",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_item_de_cobranca_id",
                table: "valores_a_devolver",
                column: "item_de_cobranca_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_parcela_id",
                table: "valores_a_devolver",
                column: "parcela_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_pedido_id",
                table: "valores_a_devolver",
                column: "pedido_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_resolvido_por_usuario_id",
                table: "valores_a_devolver",
                column: "resolvido_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_vinculo_id",
                table: "valores_a_devolver",
                column: "vinculo_id");

            migrationBuilder.AddForeignKey(
                name: "fk_recebimentos_cobrancas_bancarias_cobranca_id",
                table: "recebimentos",
                column: "cobranca_id",
                principalTable: "cobrancas_bancarias",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_recebimentos_cobrancas_bancarias_cobranca_id",
                table: "recebimentos");

            migrationBuilder.DropTable(
                name: "valores_a_devolver");

            migrationBuilder.DropIndex(
                name: "ix_recebimentos_cobranca_id",
                table: "recebimentos");

            migrationBuilder.DropColumn(
                name: "cobranca_id",
                table: "recebimentos");
        }
    }
}
