using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// Só dados do catálogo (Sprint 45): o módulo <c>festa</c> entra em todo plano pago, logo depois de
    /// <c>caixa</c>, e o <c>mesas</c> entra só nos que têm o <c>mural</c> (o Premium), logo antes dele. O
    /// gratuito não ganha nenhum dos dois.
    /// </summary>
    /// <remarks>
    /// As linhas de <c>planos</c> nascem das migrations (<c>ReduzCatalogoADoisPlanos</c>), e o seed só insere o
    /// que falta. Sem esta, todo banco novo — o dos testes de integração e o de produção — nasceria com os
    /// planos pagos sem os dois módulos, e a loja, a portaria e as mesas responderiam 403 a quem pagou.
    /// </remarks>
    public partial class ModulosDaFestaEDasMesas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE planos
                SET modulos = modulos[1:array_position(modulos, 'caixa')] || ARRAY['festa'] || modulos[array_position(modulos, 'caixa') + 1:],
                    atualizado_em = now()
                WHERE codigo <> 'gratuito' AND 'caixa' = ANY(modulos) AND NOT 'festa' = ANY(modulos);
                """
            );

            migrationBuilder.Sql(
                """
                UPDATE planos
                SET modulos = modulos[1:array_position(modulos, 'mural') - 1] || ARRAY['mesas'] || modulos[array_position(modulos, 'mural'):],
                    atualizado_em = now()
                WHERE codigo <> 'gratuito' AND 'mural' = ANY(modulos) AND NOT 'mesas' = ANY(modulos);
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE planos SET modulos = array_remove(array_remove(modulos, 'mesas'), 'festa');");
        }
    }
}
