using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class EmailComAnexo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "anexo_content_type",
                table: "emails_fila",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "anexo_conteudo",
                table: "emails_fila",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "anexo_nome",
                table: "emails_fila",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "anexo_content_type",
                table: "emails_fila");

            migrationBuilder.DropColumn(
                name: "anexo_conteudo",
                table: "emails_fila");

            migrationBuilder.DropColumn(
                name: "anexo_nome",
                table: "emails_fila");
        }
    }
}
