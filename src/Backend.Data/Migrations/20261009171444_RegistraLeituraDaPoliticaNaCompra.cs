using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RegistraLeituraDaPoliticaNaCompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ip_da_leitura_da_politica",
                table: "compras_de_convite",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "politica_lida_em",
                table: "compras_de_convite",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "user_agent_da_leitura_da_politica",
                table: "compras_de_convite",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "versao_da_politica_lida",
                table: "compras_de_convite",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ip_da_leitura_da_politica",
                table: "compras_de_convite");

            migrationBuilder.DropColumn(
                name: "politica_lida_em",
                table: "compras_de_convite");

            migrationBuilder.DropColumn(
                name: "user_agent_da_leitura_da_politica",
                table: "compras_de_convite");

            migrationBuilder.DropColumn(
                name: "versao_da_politica_lida",
                table: "compras_de_convite");
        }
    }
}
