using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// Sprint 35: o meio de pagamento único (<c>MeioDePagamento</c>) e o fim do boleto — saem as colunas dele, e
    /// o valor gravado <c>PixDinamico</c> vira <c>Pix</c> na cobrança e na compra da loja.
    /// </summary>
    /// <remarks>
    /// As linhas de boleto do banco de desenvolvimento foram apagadas à mão antes (P3 de 25/09/2026); não houve
    /// go-live, então nenhum outro banco tem boleto.
    /// </remarks>
    public partial class NucleoDePagamentosSemBoleto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "boleto_habilitado",
                table: "credenciais_de_provedor");

            migrationBuilder.DropColumn(
                name: "linha_digitavel",
                table: "cobrancas_bancarias");

            migrationBuilder.DropColumn(
                name: "url_do_documento",
                table: "cobrancas_bancarias");

            migrationBuilder.RenameColumn(
                name: "tipo",
                table: "cobrancas_bancarias",
                newName: "meio");

            migrationBuilder.Sql("UPDATE cobrancas_bancarias SET meio = 'Pix' WHERE meio = 'PixDinamico';");
            migrationBuilder.Sql("UPDATE compras_de_convite SET meio = 'Pix' WHERE meio = 'PixDinamico';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE compras_de_convite SET meio = 'PixDinamico' WHERE meio = 'Pix';");
            migrationBuilder.Sql("UPDATE cobrancas_bancarias SET meio = 'PixDinamico' WHERE meio = 'Pix';");

            migrationBuilder.RenameColumn(
                name: "meio",
                table: "cobrancas_bancarias",
                newName: "tipo");

            migrationBuilder.AddColumn<bool>(
                name: "boleto_habilitado",
                table: "credenciais_de_provedor",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "linha_digitavel",
                table: "cobrancas_bancarias",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "url_do_documento",
                table: "cobrancas_bancarias",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }
    }
}
