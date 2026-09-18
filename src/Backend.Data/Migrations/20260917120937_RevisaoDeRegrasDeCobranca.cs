using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RevisaoDeRegrasDeCobranca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_recebimentos_parcela_id",
                table: "recebimentos");

            migrationBuilder.AddColumn<int>(
                name: "dias_minimos_para_desconto",
                table: "planos_de_cobranca",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_recebimentos_parcela_id",
                table: "recebimentos",
                column: "parcela_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_recebimentos_parcela_id",
                table: "recebimentos");

            migrationBuilder.DropColumn(
                name: "dias_minimos_para_desconto",
                table: "planos_de_cobranca");

            migrationBuilder.CreateIndex(
                name: "ix_recebimentos_parcela_id",
                table: "recebimentos",
                column: "parcela_id",
                unique: true,
                filter: "estornado_em IS NULL");
        }
    }
}
