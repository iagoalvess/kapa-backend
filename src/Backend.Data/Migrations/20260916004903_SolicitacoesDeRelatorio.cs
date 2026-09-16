using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class SolicitacoesDeRelatorio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "solicitacoes_de_relatorio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    de = table.Column<DateOnly>(type: "date", nullable: false),
                    ate = table.Column<DateOnly>(type: "date", nullable: false),
                    solicitada_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    arquivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitacoes_de_relatorio", x => x.id);
                    table.ForeignKey(
                        name: "fk_solicitacoes_de_relatorio_arquivos_arquivo_id",
                        column: x => x.arquivo_id,
                        principalTable: "arquivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_solicitacoes_de_relatorio_asp_net_users_solicitada_por_usuari",
                        column: x => x.solicitada_por_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_solicitacoes_de_relatorio_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parcelas_formatura_id_status_vencimento",
                table: "parcelas",
                columns: new[] { "formatura_id", "status", "vencimento" });

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_relatorio_arquivo_id",
                table: "solicitacoes_de_relatorio",
                column: "arquivo_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_relatorio_expira_em",
                table: "solicitacoes_de_relatorio",
                column: "expira_em",
                filter: "arquivo_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_relatorio_formatura_id",
                table: "solicitacoes_de_relatorio",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_relatorio_solicitada_por_usuario_id",
                table: "solicitacoes_de_relatorio",
                column: "solicitada_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_relatorio_status_criado_em",
                table: "solicitacoes_de_relatorio",
                columns: new[] { "status", "criado_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "solicitacoes_de_relatorio");

            migrationBuilder.DropIndex(
                name: "ix_parcelas_formatura_id_status_vencimento",
                table: "parcelas");
        }
    }
}
