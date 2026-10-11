using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class LoginComGoogle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "usuarios");

            migrationBuilder.CreateTable(
                name: "tentativas_google",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    navegador_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    nonce = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    etapa = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sub = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    autoridade_sobre_email = table.Column<bool>(type: "boolean", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    security_stamp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    exige_recuperacao = table.Column<bool>(type: "boolean", nullable: false),
                    comprovante_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    convite = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    consumida_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tentativas_google", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "usuarios",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tentativas_google_expira_em",
                table: "tentativas_google",
                column: "expira_em");

            migrationBuilder.CreateIndex(
                name: "ix_tentativas_google_token_hash",
                table: "tentativas_google",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tentativas_google");

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "usuarios");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "usuarios",
                column: "normalized_email");
        }
    }
}
