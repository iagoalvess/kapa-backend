using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class PrecosDeLancamento : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Preços de 21/10/2026: o Essencial cai de R$ 89 para R$ 69 e o Premium de R$ 179 para R$ 139; o anual segue
        /// com 20% sobre doze meses (R$ 662 e R$ 1.334, cheios R$ 828 e R$ 1.668). O seed só insere o que falta — sem o
        /// <c>UPDATE</c>, o banco dos testes e o de produção ficariam com os preços antigos.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE planos SET preco_em_centavos = 6900, preco_cheio_em_centavos = NULL WHERE codigo = 'essencial';
                UPDATE planos SET preco_em_centavos = 66200, preco_cheio_em_centavos = 82800 WHERE codigo = 'essencial-anual';
                UPDATE planos SET preco_em_centavos = 13900, preco_cheio_em_centavos = NULL WHERE codigo = 'premium';
                UPDATE planos SET preco_em_centavos = 133400, preco_cheio_em_centavos = 166800 WHERE codigo = 'premium-anual';
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE planos SET preco_em_centavos = 8900, preco_cheio_em_centavos = NULL WHERE codigo = 'essencial';
                UPDATE planos SET preco_em_centavos = 85400, preco_cheio_em_centavos = 106800 WHERE codigo = 'essencial-anual';
                UPDATE planos SET preco_em_centavos = 17900, preco_cheio_em_centavos = NULL WHERE codigo = 'premium';
                UPDATE planos SET preco_em_centavos = 171800, preco_cheio_em_centavos = 214800 WHERE codigo = 'premium-anual';
                """
            );
        }
    }
}
