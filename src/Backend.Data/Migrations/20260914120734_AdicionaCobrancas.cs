using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaCobrancas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "planos_de_cobranca",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    vigente_desde = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    percentual_de_multa = table.Column<int>(type: "integer", nullable: false),
                    percentual_de_juros_ao_mes = table.Column<int>(type: "integer", nullable: false),
                    carencia_em_dias = table.Column<int>(type: "integer", nullable: false),
                    percentual_de_desconto_por_antecipacao = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planos_de_cobranca", x => x.id);
                    table.ForeignKey(
                        name: "fk_planos_de_cobranca_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "itens_de_cobranca",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plano_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    descricao = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    numero_de_parcelas = table.Column<int>(type: "integer", nullable: false),
                    dia_de_vencimento = table.Column<int>(type: "integer", nullable: false),
                    primeiro_mes = table.Column<DateOnly>(type: "date", nullable: false),
                    encerrado_em = table.Column<DateOnly>(type: "date", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_itens_de_cobranca", x => x.id);
                    table.ForeignKey(
                        name: "fk_itens_de_cobranca_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_itens_de_cobranca_planos_de_cobranca_plano_id",
                        column: x => x.plano_id,
                        principalTable: "planos_de_cobranca",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcelas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_de_cobranca_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    vencimento = table.Column<DateOnly>(type: "date", nullable: false),
                    valor_original_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcelas", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcelas_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_parcelas_itens_de_cobranca_item_de_cobranca_id",
                        column: x => x.item_de_cobranca_id,
                        principalTable: "itens_de_cobranca",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_parcelas_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_itens_de_cobranca_formatura_id",
                table: "itens_de_cobranca",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_itens_de_cobranca_plano_id",
                table: "itens_de_cobranca",
                column: "plano_id");

            migrationBuilder.CreateIndex(
                name: "ix_parcelas_formatura_id",
                table: "parcelas",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_parcelas_item_de_cobranca_id_vencimento",
                table: "parcelas",
                columns: new[] { "item_de_cobranca_id", "vencimento" });

            migrationBuilder.CreateIndex(
                name: "ix_parcelas_vinculo_id_item_de_cobranca_id_numero",
                table: "parcelas",
                columns: new[] { "vinculo_id", "item_de_cobranca_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_planos_de_cobranca_formatura_id",
                table: "planos_de_cobranca",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_planos_de_cobranca_vigente_por_formatura",
                table: "planos_de_cobranca",
                column: "formatura_id",
                unique: true,
                filter: "status = 'Vigente'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parcelas");

            migrationBuilder.DropTable(
                name: "itens_de_cobranca");

            migrationBuilder.DropTable(
                name: "planos_de_cobranca");
        }
    }
}
