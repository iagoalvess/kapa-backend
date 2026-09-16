using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class FiltrosDeRelatorio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "categoria",
                table: "solicitacoes_de_relatorio",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "formando_id",
                table: "solicitacoes_de_relatorio",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "fornecedor_id",
                table: "solicitacoes_de_relatorio",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "item_de_cobranca_id",
                table: "solicitacoes_de_relatorio",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "situacao_da_despesa",
                table: "solicitacoes_de_relatorio",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "situacao_da_parcela",
                table: "solicitacoes_de_relatorio",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_relatorio_tipo_status_de_ate",
                table: "solicitacoes_de_relatorio",
                columns: new[] { "tipo", "status", "de", "ate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_solicitacoes_de_relatorio_tipo_status_de_ate",
                table: "solicitacoes_de_relatorio");

            migrationBuilder.DropColumn(
                name: "categoria",
                table: "solicitacoes_de_relatorio");

            migrationBuilder.DropColumn(
                name: "formando_id",
                table: "solicitacoes_de_relatorio");

            migrationBuilder.DropColumn(
                name: "fornecedor_id",
                table: "solicitacoes_de_relatorio");

            migrationBuilder.DropColumn(
                name: "item_de_cobranca_id",
                table: "solicitacoes_de_relatorio");

            migrationBuilder.DropColumn(
                name: "situacao_da_despesa",
                table: "solicitacoes_de_relatorio");

            migrationBuilder.DropColumn(
                name: "situacao_da_parcela",
                table: "solicitacoes_de_relatorio");
        }
    }
}
