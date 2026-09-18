using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class PortalDoTitularLgpd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "anonimizado_em",
                table: "usuarios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "formatura_id",
                table: "eventos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "solicitacoes_de_privacidade",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    titular_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    prazo_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    confirmada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    concluida_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    arquivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    motivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitacoes_de_privacidade", x => x.id);
                    table.ForeignKey(
                        name: "fk_solicitacoes_de_privacidade_asp_net_users_titular_usuario_id",
                        column: x => x.titular_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_trilha_da_formatura",
                table: "eventos",
                columns: new[] { "formatura_id", "ocorrido_em" },
                filter: "formatura_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_privacidade_pendentes",
                table: "solicitacoes_de_privacidade",
                column: "prazo_em",
                filter: "status = 'Pendente'");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_privacidade_titular_usuario_id_criado_em",
                table: "solicitacoes_de_privacidade",
                columns: new[] { "titular_usuario_id", "criado_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "solicitacoes_de_privacidade");

            migrationBuilder.DropIndex(
                name: "ix_eventos_trilha_da_formatura",
                table: "eventos");

            migrationBuilder.DropColumn(
                name: "anonimizado_em",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "formatura_id",
                table: "eventos");
        }
    }
}
