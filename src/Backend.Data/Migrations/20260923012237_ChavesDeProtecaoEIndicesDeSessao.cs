using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// Auditoria de 22/09/2026: as chaves do DataProtection no banco e os índices da limpeza de sessões.
    /// </summary>
    /// <remarks>
    /// <c>data_protection_keys</c> guarda as chaves que assinam os links de redefinir senha e confirmar
    /// e-mail — no disco do contêiner elas mudavam a cada deploy e réplica. Os índices de
    /// <c>refresh_tokens</c> atendem a limpeza diária, que varria a tabela inteira.
    /// <para>
    /// O <c>xmin</c> de <c>parcelas</c> e <c>despesas</c> (token de concorrência) <b>não</b> aparece aqui,
    /// de propósito: é coluna de sistema, e o <c>AddColumn</c> gerado foi removido à mão, como em
    /// <c>ProducaoEGoLive</c>. Ver <c>Mappings/ConcorrenciaOtimista.cs</c>.
    /// </para>
    /// </remarks>
    public partial class ChavesDeProtecaoEIndicesDeSessao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_expira_em",
                table: "refresh_tokens",
                column: "expira_em");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_revogado_em",
                table: "refresh_tokens",
                column: "revogado_em");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_expira_em",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_revogado_em",
                table: "refresh_tokens");


        }
    }
}
