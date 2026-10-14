using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Backend.Data.Seed;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemovePortalDePrivacidade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "solicitacoes_de_privacidade");

            migrationBuilder.DropColumn(
                name: "revogado",
                table: "consentimentos");

            // A versão 1 ainda está em desenvolvimento. Ajusta os bancos locais sem recriar
            // contas/turmas; a imutabilidade volta na mesma transação da migration.
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
            migrationBuilder.AddColumn<bool>(
                name: "revogado",
                table: "consentimentos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "solicitacoes_de_privacidade",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    arquivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    concluida_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    confirmada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    motivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    prazo_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    titular_usuario_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitacoes_de_privacidade", x => x.id);
                    table.ForeignKey(
                        name: "fk_solicitacoes_de_privacidade_asp_net_users_titular_usuario_id",
                        column: x => x.titular_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_privacidade_pendentes",
                table: "solicitacoes_de_privacidade",
                column: "prazo_em",
                filter: "status = 'Pendente'");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_privacidade_titular_usuario_id_criado_em",
                table: "solicitacoes_de_privacidade",
                columns: new[] { "titular_usuario_id", "criado_em" });
        }
    }
}
