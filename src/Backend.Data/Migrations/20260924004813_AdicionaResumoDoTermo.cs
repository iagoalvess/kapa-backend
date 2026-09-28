using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// Sprint 24: a tabela do resumo do termo por IA.
    /// </summary>
    /// <remarks>
    /// Publicava também a versão 2 da Política de Privacidade, com o provedor de modelos como operador.
    /// Em 28/09/2026, antes de qualquer aceite em produção, o texto dela passou a ser a própria v1
    /// (<c>Seed/legal/PoliticaDePrivacidade.1.md</c>) e a publicação saiu daqui: lançar com uma "versão 2"
    /// e uma v1 que nunca valeu para ninguém seria histórico inventado. Bancos de desenvolvimento criados
    /// antes disso guardam a v2 — é append-only, e ela continua valendo lá sem quebrar nada.
    /// </remarks>
    public partial class AdicionaResumoDoTermo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "resumos_de_termo",
                columns: table => new
                {
                    termo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    texto = table.Column<string>(type: "text", nullable: false),
                    modelo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    gerado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resumos_de_termo", x => x.termo_id);
                    table.ForeignKey(
                        name: "fk_resumos_de_termo_termos_de_adesao_termo_id",
                        column: x => x.termo_id,
                        principalTable: "termos_de_adesao",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "resumos_de_termo");
        }
    }
}
