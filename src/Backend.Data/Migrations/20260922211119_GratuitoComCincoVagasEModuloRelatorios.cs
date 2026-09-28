using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// Só dados do catálogo, que o seed não reescreve: o gratuito passa a comportar cinco pessoas
    /// (todo papel ocupa vaga desde 22/09/2026) e o módulo <c>contabil</c> vira <c>relatorios</c>.
    /// </summary>
    public partial class GratuitoComCincoVagasEModuloRelatorios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE planos SET limite_de_formandos = 5 WHERE codigo = 'gratuito';");
            migrationBuilder.Sql("UPDATE planos SET modulos = array_replace(modulos, 'contabil', 'relatorios');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE planos SET modulos = array_replace(modulos, 'relatorios', 'contabil');");
            migrationBuilder.Sql("UPDATE planos SET limite_de_formandos = 0 WHERE codigo = 'gratuito';");
        }
    }
}
