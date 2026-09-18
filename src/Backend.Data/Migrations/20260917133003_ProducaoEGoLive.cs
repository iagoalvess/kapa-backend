using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// Sprint 16: a data do último evento do provedor e a tabela de contatos da página institucional.
    /// </summary>
    /// <remarks>
    /// O token de concorrência <c>xmin</c> de <c>assinaturas</c>, <c>formaturas</c>,
    /// <c>perfis_de_formandos</c> e <c>refresh_tokens</c> <b>não</b> aparece aqui, e é de propósito:
    /// <c>xmin</c> é coluna de <i>sistema</i> do Postgres e já existe em toda tabela. O
    /// <c>AddColumn</c> que o <c>migrations add</c> gerou para as quatro foi removido à mão — ele
    /// falharia com <c>column name "xmin" conflicts with a system column name</c>.
    /// <para>
    /// O <c>ModelSnapshot</c> continua com as quatro propriedades sombra, então nenhuma migration
    /// futura volta a gerá-las. Ver <c>Mappings/ConcorrenciaOtimista.cs</c>.
    /// </para>
    /// </remarks>
    public partial class ProducaoEGoLive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ultimo_evento_em",
                table: "assinaturas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "leads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    telefone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    instituicao = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    curso = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    tamanho_da_turma = table.Column<int>(type: "integer", nullable: false),
                    previsao_de_colacao = table.Column<DateOnly>(type: "date", nullable: true),
                    mensagem = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    origem = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    meio = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    campanha = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    privacidade_versao = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    consentido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    endereco_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leads", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leads_criado_em",
                table: "leads",
                column: "criado_em");

            migrationBuilder.CreateIndex(
                name: "ix_leads_email_criado_em",
                table: "leads",
                columns: new[] { "email", "criado_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leads");

            migrationBuilder.DropColumn(
                name: "ultimo_evento_em",
                table: "assinaturas");
        }
    }
}
