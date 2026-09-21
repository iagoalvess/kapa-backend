using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLeads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leads");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    campanha = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    consentido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    curso = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    endereco_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    instituicao = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    meio = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    mensagem = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    origem = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    previsao_de_colacao = table.Column<DateOnly>(type: "date", nullable: true),
                    privacidade_versao = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tamanho_da_turma = table.Column<int>(type: "integer", nullable: false),
                    telefone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leads", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leads_criado_em",
                table: "leads",
                column: "criado_em");

            migrationBuilder.CreateIndex(
                name: "ix_leads_email_criado_em",
                table: "leads",
                columns: new[] { "email", "criado_em" });
        }
    }
}
