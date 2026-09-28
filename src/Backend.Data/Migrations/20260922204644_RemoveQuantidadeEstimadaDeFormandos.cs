using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveQuantidadeEstimadaDeFormandos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "quantidade_estimada_de_formandos",
                table: "formaturas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "quantidade_estimada_de_formandos",
                table: "formaturas",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
