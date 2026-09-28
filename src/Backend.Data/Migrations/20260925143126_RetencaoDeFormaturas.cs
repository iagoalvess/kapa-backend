using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RetencaoDeFormaturas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "eliminada_em",
                table: "formaturas",
                type: "timestamp with time zone",
                nullable: true);

            // Editado à mão: o padrão gerado (ano 1) faria toda turma descartada ou suspensa parecer vencida
            // na primeira passada do job. As que já existem começam a contar de hoje; o default sai em seguida,
            // porque quem carimba a coluna é a entidade.
            migrationBuilder.AddColumn<DateTime>(
                name: "status_desde",
                table: "formaturas",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.Sql("ALTER TABLE formaturas ALTER COLUMN status_desde DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "eliminada_em",
                table: "formaturas");

            migrationBuilder.DropColumn(
                name: "status_desde",
                table: "formaturas");
        }
    }
}
