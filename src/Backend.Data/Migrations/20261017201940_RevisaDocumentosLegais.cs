using System;
using Backend.Data.Seed;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RevisaDocumentosLegais : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Revisão da versão 1 antes do lançamento (sem resumo, sem Encarregado, pagamentos passo a passo,
            // bases legais conferidas, cancelamentos com a comissão). Nenhum aceite em produção ainda: ajusta os
            // bancos locais sem recriar contas/turmas; a imutabilidade volta na mesma transação da migration.
            migrationBuilder.Sql("ALTER TABLE documentos_legais DISABLE TRIGGER documentos_legais_append_only;");
            migrationBuilder.UpdateData(
                table: "documentos_legais",
                keyColumn: "id",
                keyValue: new Guid("0199386a-0000-7000-8000-000000000001"),
                column: "conteudo",
                value: DocumentosLegais.Ler("TermosDeUso", "1"));
            migrationBuilder.UpdateData(
                table: "documentos_legais",
                keyColumn: "id",
                keyValue: new Guid("0199386a-0000-7000-8000-000000000002"),
                column: "conteudo",
                value: DocumentosLegais.Ler("PoliticaDePrivacidade", "1"));
            migrationBuilder.Sql("ALTER TABLE documentos_legais ENABLE TRIGGER documentos_legais_append_only;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // O texto anterior não fica no repositório (os arquivos .1.md foram revisados): não há o que restaurar.
        }
    }
}
