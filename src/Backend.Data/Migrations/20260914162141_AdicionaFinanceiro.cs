using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaFinanceiro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fornecedores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    documento = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    categoria = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    observacoes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fornecedores", x => x.id);
                    table.ForeignKey(
                        name: "fk_fornecedores_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "despesas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fornecedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    categoria = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    competencia = table.Column<DateOnly>(type: "date", nullable: false),
                    vencimento = table.Column<DateOnly>(type: "date", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    total_de_parcelas = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pago_em = table.Column<DateOnly>(type: "date", nullable: true),
                    comprovante_arquivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_despesas", x => x.id);
                    table.ForeignKey(
                        name: "fk_despesas_arquivos_comprovante_arquivo_id",
                        column: x => x.comprovante_arquivo_id,
                        principalTable: "arquivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_despesas_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_despesas_fornecedores_fornecedor_id",
                        column: x => x.fornecedor_id,
                        principalTable: "fornecedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_despesas_comprovante_arquivo_id",
                table: "despesas",
                column: "comprovante_arquivo_id");

            migrationBuilder.CreateIndex(
                name: "ix_despesas_formatura_id",
                table: "despesas",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_despesas_formatura_id_status_competencia",
                table: "despesas",
                columns: new[] { "formatura_id", "status", "competencia" });

            migrationBuilder.CreateIndex(
                name: "ix_despesas_formatura_id_vencimento",
                table: "despesas",
                columns: new[] { "formatura_id", "vencimento" });

            migrationBuilder.CreateIndex(
                name: "ix_despesas_fornecedor_id",
                table: "despesas",
                column: "fornecedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_despesas_lancamento_unico",
                table: "despesas",
                columns: new[] { "formatura_id", "fornecedor_id", "descricao", "vencimento" },
                unique: true,
                filter: "status <> 'Cancelada'")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_fornecedores_formatura_id",
                table: "fornecedores",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_fornecedores_formatura_id_nome",
                table: "fornecedores",
                columns: new[] { "formatura_id", "nome" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "despesas");

            migrationBuilder.DropTable(
                name: "fornecedores");
        }
    }
}
