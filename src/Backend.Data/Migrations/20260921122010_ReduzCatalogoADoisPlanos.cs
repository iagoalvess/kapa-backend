using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// Reduz o catálogo a dois pacotes: Essencial (até 50 formandos) e Premium (até 400).
    /// </summary>
    /// <remarks>
    /// Três pacotes obrigavam a comissão a comparar três listas de módulos para descobrir de qual
    /// precisava. Agora a pergunta é uma só — a turma cabe em 50? —, e o Premium acrescenta os
    /// quatro diferenciais (mural, régua de cobrança, painel contábil e auditoria) em vez de
    /// devolver um pedaço do necessário que tinha sido retirado do Essencial.
    /// <para>
    /// Preços novos, bem abaixo dos antigos: R$ 29,90 e R$ 49,90 por mês. O anual desce 20% sobre
    /// doze meses avulsos — o teto usual do mercado —, cobrado de uma vez.
    /// </para>
    /// <para>
    /// O catálogo entra aqui, e não no <c>SeedDePlanos</c>, porque o seed é desligado em produção
    /// (<c>Seed:AoIniciar</c>) e só insere o que falta: ele nunca reescreveria o Essencial antigo.
    /// </para>
    /// <para>
    /// <b>Assinatura em Completo ou Turma Grande passa a apontar para o Premium</b> — ele custa
    /// menos que os dois e libera os mesmos dez módulos, então ninguém perde acesso nem paga mais.
    /// Só depois disso as quatro linhas antigas são removidas; sem o reapontamento a
    /// <c>fk_assinaturas_planos_plano_id</c> barraria o <c>DELETE</c>.
    /// </para>
    /// </remarks>
    public partial class ReduzCatalogoADoisPlanos : Migration
    {
        /// <summary>Os seis códigos de módulo do Essencial, na ordem em que a vitrine os lista.</summary>
        private const string ModulosDoEssencial = "ARRAY['membros','termo','cobrancas','pix','despesas','caixa']";

        /// <summary>Os dez do Premium: os do Essencial mais os quatro diferenciais.</summary>
        private const string ModulosDoPremium =
            "ARRAY['membros','termo','cobrancas','pix','despesas','caixa','mural','avisos','contabil','auditoria']";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                UPDATE planos SET
                    descricao = 'Cobrar a turma, pagar os fornecedores e fechar o caixa.',
                    preco_em_centavos = 2990,
                    preco_cheio_em_centavos = NULL,
                    limite_de_formandos = 50,
                    modulos = {ModulosDoEssencial},
                    recomendado = false,
                    ativo = true,
                    atualizado_em = now()
                WHERE codigo = 'essencial';

                UPDATE planos SET
                    descricao = 'Cobrar a turma, pagar os fornecedores e fechar o caixa.',
                    preco_em_centavos = 28700,
                    preco_cheio_em_centavos = 35880,
                    limite_de_formandos = 50,
                    modulos = {ModulosDoEssencial},
                    recomendado = false,
                    ativo = true,
                    atualizado_em = now()
                WHERE codigo = 'essencial-anual';

                INSERT INTO planos (
                    id, codigo, nome, descricao, preco_em_centavos, preco_cheio_em_centavos,
                    ciclo, limite_de_formandos, modulos, recomendado, ativo, criado_em, atualizado_em
                )
                VALUES
                    ('0199407e-0000-7000-8000-000000000008', 'premium', 'Premium',
                     'Turma grande, com mural, régua de cobrança e prestação de contas.', 4990, NULL,
                     'Mensal', 400, {ModulosDoPremium}, true, true, now(), now()),
                    ('0199407e-0000-7000-8000-000000000009', 'premium-anual', 'Premium',
                     'Turma grande, com mural, régua de cobrança e prestação de contas.', 47900, 59880,
                     'Anual', 400, {ModulosDoPremium}, true, true, now(), now())
                ON CONFLICT (codigo) DO UPDATE SET
                    nome = EXCLUDED.nome,
                    descricao = EXCLUDED.descricao,
                    preco_em_centavos = EXCLUDED.preco_em_centavos,
                    preco_cheio_em_centavos = EXCLUDED.preco_cheio_em_centavos,
                    limite_de_formandos = EXCLUDED.limite_de_formandos,
                    modulos = EXCLUDED.modulos,
                    recomendado = EXCLUDED.recomendado,
                    ativo = EXCLUDED.ativo,
                    atualizado_em = now();

                UPDATE planos SET
                    modulos = {ModulosDoEssencial},
                    atualizado_em = now()
                WHERE codigo = 'gratuito';

                UPDATE assinaturas a SET plano_id = novo.id, atualizado_em = now()
                FROM planos antigo, planos novo
                WHERE a.plano_id = antigo.id
                  AND antigo.codigo IN ('completo', 'completo-anual', 'turma-grande', 'turma-grande-anual')
                  AND novo.codigo = CASE WHEN antigo.ciclo = 'Anual' THEN 'premium-anual' ELSE 'premium' END;

                DELETE FROM planos
                WHERE codigo IN ('completo', 'completo-anual', 'turma-grande', 'turma-grande-anual');
                """
            );
        }

        /// <summary>
        /// Devolve o catálogo de três pacotes e os preços antigos.
        /// </summary>
        /// <remarks>
        /// O Premium sai de vitrine com <c>ativo = false</c> em vez de ser apagado: quem foi
        /// reapontado para ele no <c>Up</c> continua apontando, e <c>DELETE</c> esbarraria na FK.
        /// Pelo mesmo motivo o reapontamento não se desfaz — não há como saber, depois do fato, se
        /// a assinatura veio do Completo ou do Turma Grande.
        /// </remarks>
        /// <param name="migrationBuilder">Builder da migration.</param>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE planos SET ativo = false, atualizado_em = now()
                WHERE codigo IN ('premium', 'premium-anual');

                UPDATE planos SET
                    descricao = 'Para a turma que está começando a se organizar.',
                    preco_em_centavos = 14990,
                    preco_cheio_em_centavos = NULL,
                    limite_de_formandos = 60,
                    modulos = ARRAY['membros','termo','cobrancas','pix'],
                    atualizado_em = now()
                WHERE codigo = 'essencial';

                UPDATE planos SET
                    descricao = 'Para a turma que está começando a se organizar.',
                    preco_em_centavos = 152900,
                    preco_cheio_em_centavos = 179880,
                    limite_de_formandos = 60,
                    modulos = ARRAY['membros','termo','cobrancas','pix'],
                    atualizado_em = now()
                WHERE codigo = 'essencial-anual';

                UPDATE planos SET
                    modulos = ARRAY['membros','termo','cobrancas','pix'],
                    atualizado_em = now()
                WHERE codigo = 'gratuito';

                INSERT INTO planos (
                    id, codigo, nome, descricao, preco_em_centavos, preco_cheio_em_centavos,
                    ciclo, limite_de_formandos, modulos, recomendado, ativo, criado_em, atualizado_em
                )
                VALUES
                    ('0199407e-0000-7000-8000-000000000002', 'completo', 'Completo',
                     'O dia a dia da comissão inteiro, do termo ao caixa.', 34990, NULL, 'Mensal', 150,
                     ARRAY['membros','termo','cobrancas','pix','despesas','caixa','mural','avisos'],
                     true, true, now(), now()),
                    ('0199407e-0000-7000-8000-000000000006', 'completo-anual', 'Completo',
                     'O dia a dia da comissão inteiro, do termo ao caixa.', 356900, 419880, 'Anual', 150,
                     ARRAY['membros','termo','cobrancas','pix','despesas','caixa','mural','avisos'],
                     true, true, now(), now()),
                    ('0199407e-0000-7000-8000-000000000004', 'turma-grande', 'Turma Grande',
                     'Turmas grandes, com prestação de contas e auditoria.', 69990, NULL, 'Mensal', 400,
                     ARRAY['membros','termo','cobrancas','pix','despesas','caixa','mural','avisos','contabil','auditoria'],
                     false, true, now(), now()),
                    ('0199407e-0000-7000-8000-000000000007', 'turma-grande-anual', 'Turma Grande',
                     'Turmas grandes, com prestação de contas e auditoria.', 713900, 839880, 'Anual', 400,
                     ARRAY['membros','termo','cobrancas','pix','despesas','caixa','mural','avisos','contabil','auditoria'],
                     false, true, now(), now())
                ON CONFLICT (codigo) DO NOTHING;
                """
            );
        }
    }
}
