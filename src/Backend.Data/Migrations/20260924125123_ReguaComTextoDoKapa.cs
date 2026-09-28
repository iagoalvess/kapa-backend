using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReguaComTextoDoKapa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "assunto",
                table: "regras_de_notificacao");

            migrationBuilder.DropColumn(
                name: "avisar_tesouraria",
                table: "regras_de_notificacao");

            migrationBuilder.DropColumn(
                name: "template",
                table: "regras_de_notificacao");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "assunto",
                table: "regras_de_notificacao",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "avisar_tesouraria",
                table: "regras_de_notificacao",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "template",
                table: "regras_de_notificacao",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");
        }
    }
}
