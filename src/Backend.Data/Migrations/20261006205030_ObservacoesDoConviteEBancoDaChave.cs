using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class ObservacoesDoConviteEBancoDaChave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "observacoes",
                table: "perfis_de_formandos");

            migrationBuilder.AddColumn<string>(
                name: "observacoes",
                table: "convites_do_evento",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "banco_da_chave",
                table: "contas_de_recebimento",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "observacoes",
                table: "convites_do_evento");

            migrationBuilder.DropColumn(
                name: "banco_da_chave",
                table: "contas_de_recebimento");

            migrationBuilder.AddColumn<string>(
                name: "observacoes",
                table: "perfis_de_formandos",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }
    }
}
