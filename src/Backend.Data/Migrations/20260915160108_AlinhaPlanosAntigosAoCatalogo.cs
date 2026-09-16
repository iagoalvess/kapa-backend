using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// Põe o catálogo de planos no formato da vitrine: descrição, módulos e o ciclo anual.
    /// </summary>
    /// <remarks>
    /// A <c>Inicial</c> gravou três planos mensais sem descrição e sem módulos — de antes de
    /// <c>AdicionaVitrineDosPlanos</c> —, e por isso a aba Mensal mostrava cards vazios ao lado de
    /// um "Ampliado" que saiu de linha. O catálogo entra aqui, e não no <c>SeedDePlanos</c>, pela
    /// mesma razão da <c>Inicial</c>: o seed é desligado em produção (<c>Seed:AoIniciar</c>).
    /// <para>
    /// O <c>ON CONFLICT</c> só completa as colunas da vitrine: preço e limite de um plano que já
    /// existe ficam como estão, porque o catálogo é editável no banco. Plano fora de linha vira
    /// <c>ativo = false</c>, nunca <c>DELETE</c> — assinatura antiga aponta para ele.
    /// </para>
    /// </remarks>
    public partial class AlinhaPlanosAntigosAoCatalogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO planos (
                    id, codigo, nome, descricao, preco_em_centavos, preco_cheio_em_centavos,
                    ciclo, limite_de_formandos, modulos, recomendado, ativo, criado_em, atualizado_em
                )
                VALUES
                    ('0199407e-0000-7000-8000-000000000001', 'essencial', 'Essencial',
                     'Para a turma que está começando a se organizar.', 14990, NULL, 'Mensal', 60,
                     ARRAY['Membros e convites','Termo de adesão','Cobranças e parcelas','Recebimento PIX e conferência'],
                     false, true, '2026-09-15 03:00:00+00', '2026-09-15 03:00:00+00'),
                    ('0199407e-0000-7000-8000-000000000002', 'completo', 'Completo',
                     'O dia a dia da comissão inteiro, do termo ao caixa.', 34990, NULL, 'Mensal', 150,
                     ARRAY['Membros e convites','Termo de adesão','Cobranças e parcelas','Recebimento PIX e conferência','Despesas e fornecedores','Caixa e relatórios','Mural e acervo de documentos','Avisos e régua de cobrança'],
                     true, true, '2026-09-15 03:00:00+00', '2026-09-15 03:00:00+00'),
                    ('0199407e-0000-7000-8000-000000000004', 'turma-grande', 'Turma Grande',
                     'Turmas grandes, com prestação de contas e auditoria.', 69990, NULL, 'Mensal', 400,
                     ARRAY['Membros e convites','Termo de adesão','Cobranças e parcelas','Recebimento PIX e conferência','Despesas e fornecedores','Caixa e relatórios','Mural e acervo de documentos','Avisos e régua de cobrança','Painel e exportação contábil','Portal LGPD e auditoria'],
                     false, true, '2026-09-15 03:00:00+00', '2026-09-15 03:00:00+00'),
                    ('0199407e-0000-7000-8000-000000000005', 'essencial-anual', 'Essencial',
                     'Para a turma que está começando a se organizar.', 152900, 179880, 'Anual', 60,
                     ARRAY['Membros e convites','Termo de adesão','Cobranças e parcelas','Recebimento PIX e conferência'],
                     false, true, '2026-09-15 03:00:00+00', '2026-09-15 03:00:00+00'),
                    ('0199407e-0000-7000-8000-000000000006', 'completo-anual', 'Completo',
                     'O dia a dia da comissão inteiro, do termo ao caixa.', 356900, 419880, 'Anual', 150,
                     ARRAY['Membros e convites','Termo de adesão','Cobranças e parcelas','Recebimento PIX e conferência','Despesas e fornecedores','Caixa e relatórios','Mural e acervo de documentos','Avisos e régua de cobrança'],
                     true, true, '2026-09-15 03:00:00+00', '2026-09-15 03:00:00+00'),
                    ('0199407e-0000-7000-8000-000000000007', 'turma-grande-anual', 'Turma Grande',
                     'Turmas grandes, com prestação de contas e auditoria.', 713900, 839880, 'Anual', 400,
                     ARRAY['Membros e convites','Termo de adesão','Cobranças e parcelas','Recebimento PIX e conferência','Despesas e fornecedores','Caixa e relatórios','Mural e acervo de documentos','Avisos e régua de cobrança','Painel e exportação contábil','Portal LGPD e auditoria'],
                     false, true, '2026-09-15 03:00:00+00', '2026-09-15 03:00:00+00')
                ON CONFLICT (codigo) DO UPDATE SET
                    descricao = EXCLUDED.descricao,
                    modulos = EXCLUDED.modulos,
                    preco_cheio_em_centavos = EXCLUDED.preco_cheio_em_centavos,
                    atualizado_em = EXCLUDED.atualizado_em;

                UPDATE planos SET ativo = false, atualizado_em = now() WHERE codigo = 'ampliado';
                """
            );
        }

        /// <summary>
        /// Só desfaz o que não quebra nada: tira da vitrine os planos que esta migration criou.
        /// </summary>
        /// <remarks>
        /// Apagá-los esbarraria na <c>fk_assinaturas_planos_plano_id</c> assim que uma turma
        /// tivesse contratado um deles, e reabrir o "Ampliado" ou zerar a descrição dos outros
        /// devolveria a vitrine quebrada que esta migration veio consertar.
        /// </remarks>
        /// <param name="migrationBuilder">Builder da migration.</param>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE planos SET ativo = false
                WHERE id IN (
                    '0199407e-0000-7000-8000-000000000004',
                    '0199407e-0000-7000-8000-000000000005',
                    '0199407e-0000-7000-8000-000000000006',
                    '0199407e-0000-7000-8000-000000000007'
                );
                """
            );
        }
    }
}
