using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class CestaDoFormando : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_eventos_da_turma_cota",
                table: "eventos_da_turma");

            migrationBuilder.DropColumn(
                name: "cota_aberta_em",
                table: "eventos_da_turma");

            migrationBuilder.DropColumn(
                name: "cota_por_formando",
                table: "eventos_da_turma");

            migrationBuilder.AddColumn<int>(
                name: "convites_da_colacao",
                table: "itens_de_cobranca",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "convites_da_festa",
                table: "itens_de_cobranca",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "grupo",
                table: "itens_de_cobranca",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ultimo_vencimento",
                table: "itens_de_cobranca",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "liberado_em",
                table: "convites_do_evento",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "escolhas_da_cesta",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_de_cobranca_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_escolhas_da_cesta", x => x.id);
                    table.ForeignKey(
                        name: "fk_escolhas_da_cesta_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_escolhas_da_cesta_itens_de_cobranca_item_de_cobranca_id",
                        column: x => x.item_de_cobranca_id,
                        principalTable: "itens_de_cobranca",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_escolhas_da_cesta_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_itens_de_cobranca_beneficios",
                table: "itens_de_cobranca",
                sql: "convites_da_festa >= 0 AND convites_da_colacao >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_escolhas_da_cesta_formatura_id",
                table: "escolhas_da_cesta",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_escolhas_da_cesta_item_de_cobranca_id",
                table: "escolhas_da_cesta",
                column: "item_de_cobranca_id");

            migrationBuilder.CreateIndex(
                name: "ix_escolhas_da_cesta_vinculo_id_item_de_cobranca_id",
                table: "escolhas_da_cesta",
                columns: new[] { "vinculo_id", "item_de_cobranca_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "escolhas_da_cesta");

            migrationBuilder.DropCheckConstraint(
                name: "ck_itens_de_cobranca_beneficios",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "convites_da_colacao",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "convites_da_festa",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "grupo",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "ultimo_vencimento",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "liberado_em",
                table: "convites_do_evento");

            migrationBuilder.AddColumn<DateTime>(
                name: "cota_aberta_em",
                table: "eventos_da_turma",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cota_por_formando",
                table: "eventos_da_turma",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_eventos_da_turma_cota",
                table: "eventos_da_turma",
                sql: "cota_por_formando IS NULL OR cota_por_formando > 0");
        }
    }
}
