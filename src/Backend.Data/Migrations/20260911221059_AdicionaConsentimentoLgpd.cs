using System;
using Backend.Data.Seed;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// Documentos legais versionados, consentimento append-only e a publicação da versão 1.
    /// </summary>
    /// <remarks>
    /// Os gatilhos tornam as duas tabelas append-only no banco, e não só no código: nem um
    /// <c>UPDATE</c> escrito à mão no psql altera um texto publicado ou uma prova de consentimento.
    /// <para>
    /// A vigência é meia-noite de Brasília (03:00 UTC): meia-noite UTC apareceria na tela como o
    /// dia anterior.
    /// </para>
    /// </remarks>
    public partial class AdicionaConsentimentoLgpd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "documentos_legais",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    versao = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    conteudo = table.Column<string>(type: "text", nullable: false),
                    vigente_desde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentos_legais", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consentimentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_legal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    versao = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    aceito_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    endereco_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    revogado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consentimentos", x => x.id);
                    table.ForeignKey(
                        name: "fk_consentimentos_asp_net_users_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_consentimentos_documentos_legais_documento_legal_id",
                        column: x => x.documento_legal_id,
                        principalTable: "documentos_legais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consentimentos_documento_legal_id",
                table: "consentimentos",
                column: "documento_legal_id");

            migrationBuilder.CreateIndex(
                name: "ix_consentimentos_usuario_id_aceito_em",
                table: "consentimentos",
                columns: new[] { "usuario_id", "aceito_em" });

            migrationBuilder.CreateIndex(
                name: "ix_documentos_legais_tipo_versao",
                table: "documentos_legais",
                columns: new[] { "tipo", "versao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documentos_legais_tipo_vigente_desde",
                table: "documentos_legais",
                columns: new[] { "tipo", "vigente_desde" });

            migrationBuilder.Sql(
                """
                CREATE FUNCTION recusar_alteracao() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'A tabela % é append-only: publique uma versão nova ou grave uma linha nova.', TG_TABLE_NAME;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER documentos_legais_append_only BEFORE UPDATE OR DELETE ON documentos_legais
                    FOR EACH ROW EXECUTE FUNCTION recusar_alteracao();

                CREATE TRIGGER consentimentos_append_only BEFORE UPDATE OR DELETE ON consentimentos
                    FOR EACH ROW EXECUTE FUNCTION recusar_alteracao();
                """
            );

            var vigencia = new DateTime(2026, 9, 11, 3, 0, 0, DateTimeKind.Utc);

            DocumentosLegais.Publicar(migrationBuilder, new Guid("0199386a-0000-7000-8000-000000000001"), "TermosDeUso", "1", vigencia);
            DocumentosLegais.Publicar(migrationBuilder, new Guid("0199386a-0000-7000-8000-000000000002"), "PoliticaDePrivacidade", "1", vigencia);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION recusar_alteracao() CASCADE;");

            migrationBuilder.DropTable(
                name: "consentimentos");

            migrationBuilder.DropTable(
                name: "documentos_legais");
        }
    }
}
