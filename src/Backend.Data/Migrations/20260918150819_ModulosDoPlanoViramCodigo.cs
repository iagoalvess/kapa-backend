using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// Troca o texto de vitrine gravado em <c>planos.modulos</c> pelos códigos de <c>Modulo</c>.
    /// </summary>
    /// <remarks>
    /// Sem isto, todo banco que já rodou o seed fica com os nomes por extenso — e como os módulos
    /// passaram a ser regra de acesso, nenhum deles casaria com o código exigido pela política: a
    /// turma inteira perderia todas as áreas de uma vez. É migração de dado, não de esquema.
    /// <para>
    /// A troca é item a item com <c>unnest</c>, e não <c>replace</c> no array inteiro: preserva a
    /// ordem (que é a ordem em que o card lista) e não toca em módulo que alguém tenha cadastrado à
    /// mão e não esteja na tabela.
    /// </para>
    /// </remarks>
    public partial class ModulosDoPlanoViramCodigo : Migration
    {
        private const string DeNomeParaCodigo = """
            UPDATE planos SET modulos = ARRAY(
                SELECT CASE m
                    WHEN 'Membros e convites'            THEN 'membros'
                    WHEN 'Termo de adesão'               THEN 'termo'
                    WHEN 'Cobranças e parcelas'          THEN 'cobrancas'
                    WHEN 'Recebimento PIX e conferência' THEN 'pix'
                    WHEN 'Despesas e fornecedores'       THEN 'despesas'
                    WHEN 'Caixa e relatórios'            THEN 'caixa'
                    WHEN 'Mural e acervo de documentos'  THEN 'mural'
                    WHEN 'Avisos e régua de cobrança'    THEN 'avisos'
                    WHEN 'Painel e exportação contábil'  THEN 'contabil'
                    WHEN 'Portal LGPD e auditoria'       THEN 'auditoria'
                    ELSE m
                END
                FROM unnest(modulos) AS m
            );
            """;

        private const string DeCodigoParaNome = """
            UPDATE planos SET modulos = ARRAY(
                SELECT CASE m
                    WHEN 'membros'   THEN 'Membros e convites'
                    WHEN 'termo'     THEN 'Termo de adesão'
                    WHEN 'cobrancas' THEN 'Cobranças e parcelas'
                    WHEN 'pix'       THEN 'Recebimento PIX e conferência'
                    WHEN 'despesas'  THEN 'Despesas e fornecedores'
                    WHEN 'caixa'     THEN 'Caixa e relatórios'
                    WHEN 'mural'     THEN 'Mural e acervo de documentos'
                    WHEN 'avisos'    THEN 'Avisos e régua de cobrança'
                    WHEN 'contabil'  THEN 'Painel e exportação contábil'
                    WHEN 'auditoria' THEN 'Portal LGPD e auditoria'
                    ELSE m
                END
                FROM unnest(modulos) AS m
            );
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DeNomeParaCodigo);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DeCodigoParaNome);
    }
}
