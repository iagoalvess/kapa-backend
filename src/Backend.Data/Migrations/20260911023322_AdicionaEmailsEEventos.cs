using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaEmailsEEventos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "emails_fila",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    para = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    assunto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    corpo_html = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    prioridade = table.Column<int>(type: "integer", nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    proxima_tentativa_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ultimo_erro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    enviado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emails_fila", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "eventos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ocorrido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    rota = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    dados = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_emails_fila_status_proxima_tentativa_em_prioridade",
                table: "emails_fila",
                columns: new[] { "status", "proxima_tentativa_em", "prioridade" },
                filter: "status = 0");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_ocorrido_em_nome",
                table: "eventos",
                columns: new[] { "ocorrido_em", "nome" });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_usuario_id_ocorrido_em",
                table: "eventos",
                columns: new[] { "usuario_id", "ocorrido_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "emails_fila");

            migrationBuilder.DropTable(
                name: "eventos");
        }
    }
}
