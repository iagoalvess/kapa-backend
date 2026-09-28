using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class ParcelasEscolhidasNoPedido : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Os pedidos de antes foram gerados com a grade inteira do item: o número de lá era regra, e
        /// é essa a divisão que as parcelas deles já têm.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "parcelas",
                table: "pedidos",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql(
                "UPDATE pedidos p SET parcelas = i.numero_de_parcelas FROM itens_de_cobranca i WHERE i.id = p.item_de_cobranca_id;"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_pedidos_parcelas",
                table: "pedidos",
                sql: "parcelas >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pedidos_parcelas",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "parcelas",
                table: "pedidos");
        }
    }
}
