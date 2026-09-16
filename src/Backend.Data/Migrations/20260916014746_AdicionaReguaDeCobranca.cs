using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaReguaDeCobranca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "preferencias_de_notificacao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preferencias_de_notificacao", x => x.id);
                    table.ForeignKey(
                        name: "fk_preferencias_de_notificacao_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_preferencias_de_notificacao_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "regras_de_notificacao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gatilho = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dias_de_deslocamento = table.Column<int>(type: "integer", nullable: false),
                    canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    assunto = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    template = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    avisar_tesouraria = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regras_de_notificacao", x => x.id);
                    table.ForeignKey(
                        name: "fk_regras_de_notificacao_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notificacoes_enviadas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcela_id = table.Column<Guid>(type: "uuid", nullable: true),
                    regra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_de_referencia = table.Column<DateOnly>(type: "date", nullable: false),
                    canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    destinatario = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assunto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email_na_fila_id = table.Column<Guid>(type: "uuid", nullable: true),
                    erro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notificacoes_enviadas", x => x.id);
                    table.ForeignKey(
                        name: "fk_notificacoes_enviadas_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notificacoes_enviadas_parcelas_parcela_id",
                        column: x => x.parcela_id,
                        principalTable: "parcelas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notificacoes_enviadas_regras_de_notificacao_regra_id",
                        column: x => x.regra_id,
                        principalTable: "regras_de_notificacao",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notificacoes_enviadas_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_enviadas_formatura_id",
                table: "notificacoes_enviadas",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_enviadas_formatura_id_data_de_referencia",
                table: "notificacoes_enviadas",
                columns: new[] { "formatura_id", "data_de_referencia" });

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_enviadas_formatura_id_status",
                table: "notificacoes_enviadas",
                columns: new[] { "formatura_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_enviadas_idempotencia",
                table: "notificacoes_enviadas",
                columns: new[] { "formatura_id", "parcela_id", "regra_id", "data_de_referencia", "destinatario" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_enviadas_parcela_id",
                table: "notificacoes_enviadas",
                column: "parcela_id");

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_enviadas_regra_id",
                table: "notificacoes_enviadas",
                column: "regra_id");

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_enviadas_vinculo_id",
                table: "notificacoes_enviadas",
                column: "vinculo_id");

            migrationBuilder.CreateIndex(
                name: "ix_preferencias_de_notificacao_formatura_id",
                table: "preferencias_de_notificacao",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_preferencias_de_notificacao_vinculo_id_tipo",
                table: "preferencias_de_notificacao",
                columns: new[] { "vinculo_id", "tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_regras_de_notificacao_formatura_id",
                table: "regras_de_notificacao",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_regras_de_notificacao_formatura_id_gatilho_dias_de_deslocam",
                table: "regras_de_notificacao",
                columns: new[] { "formatura_id", "gatilho", "dias_de_deslocamento" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notificacoes_enviadas");

            migrationBuilder.DropTable(
                name: "preferencias_de_notificacao");

            migrationBuilder.DropTable(
                name: "regras_de_notificacao");
        }
    }
}
