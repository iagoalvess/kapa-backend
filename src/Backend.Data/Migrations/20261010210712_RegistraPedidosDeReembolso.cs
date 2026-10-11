using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RegistraPedidosDeReembolso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pedidos_de_reembolso",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assinatura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cobranca_id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email_do_solicitante = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    motivo = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    id_do_pagamento = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    meio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    paga_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedidos_de_reembolso", x => x.id);
                    table.ForeignKey(
                        name: "fk_pedidos_de_reembolso_assinaturas_assinatura_id",
                        column: x => x.assinatura_id,
                        principalTable: "assinaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pedidos_de_reembolso_cobrancas_da_assinatura_cobranca_id",
                        column: x => x.cobranca_id,
                        principalTable: "cobrancas_da_assinatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pedidos_de_reembolso_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_de_reembolso_assinatura_id",
                table: "pedidos_de_reembolso",
                column: "assinatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_de_reembolso_cobranca_id",
                table: "pedidos_de_reembolso",
                column: "cobranca_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_de_reembolso_formatura_id",
                table: "pedidos_de_reembolso",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_de_reembolso_status_criado_em",
                table: "pedidos_de_reembolso",
                columns: new[] { "status", "criado_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pedidos_de_reembolso");
        }
    }
}
