using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class IndiceDeUsoDosEventos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_eventos_ocorrido_em_nome",
                table: "eventos");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_ocorrido_em",
                table: "eventos",
                column: "ocorrido_em")
                .Annotation("Npgsql:IndexInclude", new[] { "nome", "formatura_id", "usuario_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_eventos_ocorrido_em",
                table: "eventos");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_ocorrido_em_nome",
                table: "eventos",
                columns: new[] { "ocorrido_em", "nome" });
        }
    }
}
