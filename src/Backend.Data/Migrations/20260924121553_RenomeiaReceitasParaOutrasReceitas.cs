using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// "Receitas" virou "Outras receitas" em todas as camadas (24/09/2026): parcela paga também é
    /// receita, e o nome antigo fazia parecer que as mensalidades não entravam no caixa.
    /// <para>
    /// Escrita à mão. O EF gerou <c>DropTable</c> + <c>CreateTable</c>, que apagaria toda receita já
    /// lançada; aqui é renomeação de tabela, chaves e índices, sem tocar nos dados.
    /// </para>
    /// <para>
    /// Os eventos são renomeados junto, como na <c>RenomeiaCatalogoParaOpcionais</c>: a trilha lê o
    /// nome para dar o rótulo, e o antigo ficaria órfão na auditoria.
    /// </para>
    /// </remarks>
    public partial class RenomeiaReceitasParaOutrasReceitas : Migration
    {
        private static readonly string[] Indices = ["documento_id", "formatura_id", "formatura_id_status_data", "lancamento_unico"];

        private static readonly string[] ChavesEstrangeiras = ["documentos_documento_id", "formaturas_formatura_id"];

        private static readonly string[] Eventos = ["lancada", "alterada", "recebida", "cancelada"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => Renomear(migrationBuilder, "receitas", "outras_receitas", "receita", "outra_receita");

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            Renomear(migrationBuilder, "outras_receitas", "receitas", "outra_receita", "receita");

        private static void Renomear(MigrationBuilder migrationBuilder, string de, string para, string eventoDe, string eventoPara)
        {
            migrationBuilder.RenameTable(name: de, newName: para);

            migrationBuilder.Sql($"ALTER TABLE {para} RENAME CONSTRAINT pk_{de} TO pk_{para};");

            foreach (var chave in ChavesEstrangeiras)
                migrationBuilder.Sql($"ALTER TABLE {para} RENAME CONSTRAINT fk_{de}_{chave} TO fk_{para}_{chave};");

            foreach (var indice in Indices)
                migrationBuilder.RenameIndex(name: $"ix_{de}_{indice}", table: para, newName: $"ix_{para}_{indice}");

            foreach (var evento in Eventos)
                migrationBuilder.Sql($"UPDATE eventos SET nome = 'financeiro.{eventoPara}_{evento}' WHERE nome = 'financeiro.{eventoDe}_{evento}';");
        }
    }
}
