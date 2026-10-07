using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class PrecosECupons : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Sprint 51: cupons e os preços novos. O catálogo nasce das migrations e o seed só insere o que falta — sem o
        /// <c>UPDATE</c>, o banco dos testes e o de produção ficariam com os preços antigos.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cupom_id",
                table: "assinaturas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cupons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    percentual = table.Column<int>(type: "integer", nullable: false),
                    valido_ate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    limite_de_usos = table.Column<int>(type: "integer", nullable: false),
                    usos = table.Column<int>(type: "integer", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cupons", x => x.id);
                    table.CheckConstraint("ck_cupons_percentual", "percentual BETWEEN 1 AND 50");
                    table.CheckConstraint("ck_cupons_usos", "limite_de_usos > 0 AND usos >= 0 AND usos <= limite_de_usos");
                });

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_cupom_id",
                table: "assinaturas",
                column: "cupom_id");

            migrationBuilder.CreateIndex(
                name: "ix_cupons_codigo",
                table: "cupons",
                column: "codigo",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_assinaturas_cupons_cupom_id",
                table: "assinaturas",
                column: "cupom_id",
                principalTable: "cupons",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                UPDATE planos SET preco_em_centavos = 8900, preco_cheio_em_centavos = NULL, limite_de_formandos = 60 WHERE codigo = 'essencial';
                UPDATE planos SET preco_em_centavos = 85400, preco_cheio_em_centavos = 106800, limite_de_formandos = 60 WHERE codigo = 'essencial-anual';
                UPDATE planos SET preco_em_centavos = 17900, preco_cheio_em_centavos = NULL, limite_de_formandos = 200 WHERE codigo = 'premium';
                UPDATE planos SET preco_em_centavos = 171800, preco_cheio_em_centavos = 214800, limite_de_formandos = 200 WHERE codigo = 'premium-anual';
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE planos SET preco_em_centavos = 2990, preco_cheio_em_centavos = NULL, limite_de_formandos = 50 WHERE codigo = 'essencial';
                UPDATE planos SET preco_em_centavos = 28700, preco_cheio_em_centavos = 35880, limite_de_formandos = 50 WHERE codigo = 'essencial-anual';
                UPDATE planos SET preco_em_centavos = 4990, preco_cheio_em_centavos = NULL, limite_de_formandos = 400 WHERE codigo = 'premium';
                UPDATE planos SET preco_em_centavos = 47900, preco_cheio_em_centavos = 59880, limite_de_formandos = 400 WHERE codigo = 'premium-anual';
                """
            );

            migrationBuilder.DropForeignKey(
                name: "fk_assinaturas_cupons_cupom_id",
                table: "assinaturas");

            migrationBuilder.DropTable(
                name: "cupons");

            migrationBuilder.DropIndex(
                name: "ix_assinaturas_cupom_id",
                table: "assinaturas");

            migrationBuilder.DropColumn(
                name: "cupom_id",
                table: "assinaturas");
        }
    }
}
