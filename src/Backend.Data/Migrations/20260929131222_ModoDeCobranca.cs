using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class ModoDeCobranca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "cobranca_automatica_em",
                table: "credenciais_de_provedor",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE credenciais_de_provedor SET cobranca_automatica_em = atualizado_em;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cobranca_automatica_em",
                table: "credenciais_de_provedor");
        }
    }
}
