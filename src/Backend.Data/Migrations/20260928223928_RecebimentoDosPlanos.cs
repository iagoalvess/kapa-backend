using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecebimentoDosPlanos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "meio",
                table: "assinaturas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Cartao");

            migrationBuilder.AddColumn<Guid>(
                name: "plano_do_proximo_ciclo_id",
                table: "assinaturas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cobrancas_da_assinatura",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assinatura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plano_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    meio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    situacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    id_do_pagamento = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    paga_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    valor_estornado_em_centavos = table.Column<long>(type: "bigint", nullable: true),
                    estornada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cobrancas_da_assinatura", x => x.id);
                    table.ForeignKey(
                        name: "fk_cobrancas_da_assinatura_assinaturas_assinatura_id",
                        column: x => x.assinatura_id,
                        principalTable: "assinaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cobrancas_da_assinatura_planos_plano_id",
                        column: x => x.plano_id,
                        principalTable: "planos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_plano_do_proximo_ciclo_id",
                table: "assinaturas",
                column: "plano_do_proximo_ciclo_id");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_da_assinatura_assinatura_id",
                table: "cobrancas_da_assinatura",
                column: "assinatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_da_assinatura_id_do_pagamento",
                table: "cobrancas_da_assinatura",
                column: "id_do_pagamento",
                unique: true,
                filter: "id_do_pagamento IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_da_assinatura_plano_id",
                table: "cobrancas_da_assinatura",
                column: "plano_id");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_da_assinatura_situacao_criado_em",
                table: "cobrancas_da_assinatura",
                columns: new[] { "situacao", "criado_em" });

            migrationBuilder.AddForeignKey(
                name: "fk_assinaturas_planos_plano_do_proximo_ciclo_id",
                table: "assinaturas",
                column: "plano_do_proximo_ciclo_id",
                principalTable: "planos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_assinaturas_planos_plano_do_proximo_ciclo_id",
                table: "assinaturas");

            migrationBuilder.DropTable(
                name: "cobrancas_da_assinatura");

            migrationBuilder.DropIndex(
                name: "ix_assinaturas_plano_do_proximo_ciclo_id",
                table: "assinaturas");

            migrationBuilder.DropColumn(
                name: "meio",
                table: "assinaturas");

            migrationBuilder.DropColumn(
                name: "plano_do_proximo_ciclo_id",
                table: "assinaturas");
        }
    }
}
