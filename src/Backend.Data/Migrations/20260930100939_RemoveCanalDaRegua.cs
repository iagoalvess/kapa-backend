using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCanalDaRegua : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "canal",
                table: "regras_de_notificacao");

            migrationBuilder.DropColumn(
                name: "canal",
                table: "notificacoes_enviadas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "canal",
                table: "regras_de_notificacao",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Email");

            migrationBuilder.AddColumn<string>(
                name: "canal",
                table: "notificacoes_enviadas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Email");
        }
    }
}
