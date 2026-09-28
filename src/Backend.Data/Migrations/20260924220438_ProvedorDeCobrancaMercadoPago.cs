using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProvedorDeCobrancaMercadoPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cobrancas_bancarias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    parcela_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    chave = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    id_externo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    copia_e_cola = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    linha_digitavel = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    url_do_documento = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cobrancas_bancarias", x => x.id);
                    table.ForeignKey(
                        name: "fk_cobrancas_bancarias_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "credenciais_de_provedor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_token = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    refresh_token = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    id_no_provedor = table.Column<long>(type: "bigint", nullable: false),
                    conta_no_provedor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cadastrada_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boleto_habilitado = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credenciais_de_provedor", x => x.id);
                    table.ForeignKey(
                        name: "fk_credenciais_de_provedor_asp_net_users_cadastrada_por_usuario_",
                        column: x => x.cadastrada_por_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_credenciais_de_provedor_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_bancarias_formatura_id",
                table: "cobrancas_bancarias",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_bancarias_formatura_id_chave",
                table: "cobrancas_bancarias",
                columns: new[] { "formatura_id", "chave" },
                unique: true,
                filter: "status IN ('Emitindo', 'Emitida')");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_bancarias_id_externo",
                table: "cobrancas_bancarias",
                column: "id_externo");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_bancarias_status_criado_em",
                table: "cobrancas_bancarias",
                columns: new[] { "status", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_credenciais_de_provedor_cadastrada_por_usuario_id",
                table: "credenciais_de_provedor",
                column: "cadastrada_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_credenciais_de_provedor_expira_em",
                table: "credenciais_de_provedor",
                column: "expira_em");

            migrationBuilder.CreateIndex(
                name: "ix_credenciais_de_provedor_formatura_id",
                table: "credenciais_de_provedor",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_credenciais_de_provedor_uma_por_formatura",
                table: "credenciais_de_provedor",
                column: "formatura_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cobrancas_bancarias");

            migrationBuilder.DropTable(
                name: "credenciais_de_provedor");
        }
    }
}
