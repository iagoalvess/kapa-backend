using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class CotaDaColacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "capacidade",
                table: "eventos_da_turma",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cota_aberta_em",
                table: "eventos_da_turma",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cota_por_formando",
                table: "eventos_da_turma",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_eventos_da_turma_capacidade",
                table: "eventos_da_turma",
                sql: "capacidade IS NULL OR capacidade > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_eventos_da_turma_cota",
                table: "eventos_da_turma",
                sql: "cota_por_formando IS NULL OR cota_por_formando > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_eventos_da_turma_capacidade",
                table: "eventos_da_turma");

            migrationBuilder.DropCheckConstraint(
                name: "ck_eventos_da_turma_cota",
                table: "eventos_da_turma");

            migrationBuilder.DropColumn(
                name: "capacidade",
                table: "eventos_da_turma");

            migrationBuilder.DropColumn(
                name: "cota_aberta_em",
                table: "eventos_da_turma");

            migrationBuilder.DropColumn(
                name: "cota_por_formando",
                table: "eventos_da_turma");
        }
    }
}
