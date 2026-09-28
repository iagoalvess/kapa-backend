using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenomeiaCatalogoParaOpcionais : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// O evento é renomeado junto porque a trilha lê o nome dele para dar o rótulo: o antigo
        /// ficaria órfão na auditoria.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "sob_demanda",
                table: "itens_de_cobranca",
                newName: "opcional");

            migrationBuilder.Sql("UPDATE eventos SET nome = 'cobranca.opcional_criado' WHERE nome = 'cobranca.catalogo_item_criado';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "opcional",
                table: "itens_de_cobranca",
                newName: "sob_demanda");

            migrationBuilder.Sql("UPDATE eventos SET nome = 'cobranca.catalogo_item_criado' WHERE nome = 'cobranca.opcional_criado';");
        }
    }
}
