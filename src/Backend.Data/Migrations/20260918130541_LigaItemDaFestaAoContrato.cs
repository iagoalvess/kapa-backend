using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class LigaItemDaFestaAoContrato : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "documento_id",
                table: "itens_da_festa",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_itens_da_festa_documento_id",
                table: "itens_da_festa",
                column: "documento_id");

            migrationBuilder.AddForeignKey(
                name: "fk_itens_da_festa_documentos_documento_id",
                table: "itens_da_festa",
                column: "documento_id",
                principalTable: "documentos",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_itens_da_festa_documentos_documento_id",
                table: "itens_da_festa");

            migrationBuilder.DropIndex(
                name: "ix_itens_da_festa_documento_id",
                table: "itens_da_festa");

            migrationBuilder.DropColumn(
                name: "documento_id",
                table: "itens_da_festa");
        }
    }
}
