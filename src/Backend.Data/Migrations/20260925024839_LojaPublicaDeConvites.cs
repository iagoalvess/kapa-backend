using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class LojaPublicaDeConvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE itens_de_cobranca
                    ALTER COLUMN abertura_de_vendas TYPE timestamp with time zone
                    USING (abertura_de_vendas::timestamp AT TIME ZONE 'America/Sao_Paulo');
                """
            );

            migrationBuilder.AddColumn<string>(
                name: "modo_de_venda",
                table: "itens_de_cobranca",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "AoFormando");

            migrationBuilder.AddColumn<long>(
                name: "preco_publico_em_centavos",
                table: "itens_de_cobranca",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "compra_id",
                table: "convites_do_evento",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "compra_id",
                table: "cobrancas_bancarias",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "compras_de_convite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_de_cobranca_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantidade = table.Column<int>(type: "integer", nullable: false),
                    valor_unitario_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    meio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nome_do_comprador = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cpf = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    chave_de_idempotencia = table.Column<Guid>(type: "uuid", nullable: false),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    paga_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    valor_pago_em_centavos = table.Column<long>(type: "bigint", nullable: true),
                    cpf_do_pagador = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    versao_do_link = table.Column<int>(type: "integer", nullable: false),
                    outra_receita_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dados_apagados_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cpf_hmac = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compras_de_convite", x => x.id);
                    table.CheckConstraint("ck_compras_de_convite_quantidade", "quantidade > 0");
                    table.CheckConstraint("ck_compras_de_convite_valor", "valor_em_centavos >= 0");
                    table.ForeignKey(
                        name: "fk_compras_de_convite_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_compras_de_convite_itens_de_cobranca_item_de_cobranca_id",
                        column: x => x.item_de_cobranca_id,
                        principalTable: "itens_de_cobranca",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_compras_de_convite_outras_receitas_outra_receita_id",
                        column: x => x.outra_receita_id,
                        principalTable: "outras_receitas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_convites_do_evento_posicao_da_compra",
                table: "convites_do_evento",
                columns: new[] { "compra_id", "sequencial" },
                unique: true,
                filter: "compra_id IS NOT NULL AND revogado_em IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_bancarias_compra_id",
                table: "cobrancas_bancarias",
                column: "compra_id");

            migrationBuilder.CreateIndex(
                name: "ix_compras_de_convite_chave_de_idempotencia",
                table: "compras_de_convite",
                column: "chave_de_idempotencia",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_compras_de_convite_cpf_hmac_item_de_cobranca_id",
                table: "compras_de_convite",
                columns: new[] { "cpf_hmac", "item_de_cobranca_id" });

            migrationBuilder.CreateIndex(
                name: "ix_compras_de_convite_formatura_id",
                table: "compras_de_convite",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_compras_de_convite_formatura_id_email",
                table: "compras_de_convite",
                columns: new[] { "formatura_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_compras_de_convite_item_de_cobranca_id",
                table: "compras_de_convite",
                column: "item_de_cobranca_id");

            migrationBuilder.CreateIndex(
                name: "ix_compras_de_convite_outra_receita_id",
                table: "compras_de_convite",
                column: "outra_receita_id");

            migrationBuilder.CreateIndex(
                name: "ix_compras_de_convite_status_expira_em",
                table: "compras_de_convite",
                columns: new[] { "status", "expira_em" },
                filter: "status = 'Pendente'");

            migrationBuilder.AddForeignKey(
                name: "fk_cobrancas_bancarias_compras_de_convite_compra_id",
                table: "cobrancas_bancarias",
                column: "compra_id",
                principalTable: "compras_de_convite",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_convites_do_evento_compras_de_convite_compra_id",
                table: "convites_do_evento",
                column: "compra_id",
                principalTable: "compras_de_convite",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cobrancas_bancarias_compras_de_convite_compra_id",
                table: "cobrancas_bancarias");

            migrationBuilder.DropForeignKey(
                name: "fk_convites_do_evento_compras_de_convite_compra_id",
                table: "convites_do_evento");

            migrationBuilder.DropTable(
                name: "compras_de_convite");

            migrationBuilder.DropIndex(
                name: "ux_convites_do_evento_posicao_da_compra",
                table: "convites_do_evento");

            migrationBuilder.DropIndex(
                name: "ix_cobrancas_bancarias_compra_id",
                table: "cobrancas_bancarias");

            migrationBuilder.DropColumn(
                name: "modo_de_venda",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "preco_publico_em_centavos",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "compra_id",
                table: "convites_do_evento");

            migrationBuilder.DropColumn(
                name: "compra_id",
                table: "cobrancas_bancarias");

            migrationBuilder.Sql(
                """
                ALTER TABLE itens_de_cobranca
                    ALTER COLUMN abertura_de_vendas TYPE date
                    USING ((abertura_de_vendas AT TIME ZONE 'America/Sao_Paulo')::date);
                """
            );
        }
    }
}
