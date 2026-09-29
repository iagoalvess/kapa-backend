using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class CartaoNaComissao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "cartao_ligado_em",
                table: "credenciais_de_provedor",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cartao_ligado_por_usuario_id",
                table: "credenciais_de_provedor",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "chave_publica",
                table: "credenciais_de_provedor",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "taxa_do_cartao_repassada",
                table: "credenciais_de_provedor",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "receita_do_acrescimo_id",
                table: "cobrancas_bancarias",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "acrescimo_em_centavos",
                table: "cobrancas_bancarias",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cartao_ligado_em",
                table: "credenciais_de_provedor");

            migrationBuilder.DropColumn(
                name: "cartao_ligado_por_usuario_id",
                table: "credenciais_de_provedor");

            migrationBuilder.DropColumn(
                name: "chave_publica",
                table: "credenciais_de_provedor");

            migrationBuilder.DropColumn(
                name: "taxa_do_cartao_repassada",
                table: "credenciais_de_provedor");

            migrationBuilder.DropColumn(
                name: "acrescimo_em_centavos",
                table: "cobrancas_bancarias");

            migrationBuilder.DropColumn(
                name: "receita_do_acrescimo_id",
                table: "cobrancas_bancarias");
        }
    }
}
