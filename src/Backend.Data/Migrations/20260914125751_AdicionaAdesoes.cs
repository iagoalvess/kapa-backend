using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// Termo de adesão da turma e as adesões, com o plano congelado de cada uma.
    /// </summary>
    /// <remarks>
    /// O que o EF não gera está no fim de <see cref="Up"/>: os gatilhos que tornam as duas tabelas
    /// append-only, com a mesma <c>recusar_alteracao()</c> da migration inicial — termo publicado e
    /// aceite são prova, e nem um <c>UPDATE</c> escrito à mão no psql os altera.
    /// </remarks>
    public partial class AdicionaAdesoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "termos_de_adesao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    versao = table.Column<int>(type: "integer", nullable: false),
                    conteudo = table.Column<string>(type: "text", nullable: false),
                    vigente_desde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    publicado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_termos_de_adesao", x => x.id);
                    table.ForeignKey(
                        name: "fk_termos_de_adesao_asp_net_users_publicado_por_usuario_id",
                        column: x => x.publicado_por_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_termos_de_adesao_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "adesoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    termo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    versao = table.Column<int>(type: "integer", nullable: false),
                    hash_do_conteudo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    aceito_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    endereco_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    nome_completo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cpf = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    plano_aceito = table.Column<string>(type: "text", nullable: false),
                    cpf_hmac = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adesoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_adesoes_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_adesoes_termos_de_adesao_termo_id",
                        column: x => x.termo_id,
                        principalTable: "termos_de_adesao",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_adesoes_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_adesoes_cpf_hmac",
                table: "adesoes",
                column: "cpf_hmac");

            migrationBuilder.CreateIndex(
                name: "ix_adesoes_formatura_id",
                table: "adesoes",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_adesoes_termo_id",
                table: "adesoes",
                column: "termo_id");

            migrationBuilder.CreateIndex(
                name: "ix_adesoes_vinculo_id_termo_id",
                table: "adesoes",
                columns: new[] { "vinculo_id", "termo_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_termos_de_adesao_formatura_id",
                table: "termos_de_adesao",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_termos_de_adesao_formatura_id_versao",
                table: "termos_de_adesao",
                columns: new[] { "formatura_id", "versao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_termos_de_adesao_publicado_por_usuario_id",
                table: "termos_de_adesao",
                column: "publicado_por_usuario_id");

            migrationBuilder.Sql(
                """
                CREATE TRIGGER termos_de_adesao_append_only BEFORE UPDATE OR DELETE ON termos_de_adesao
                    FOR EACH ROW EXECUTE FUNCTION recusar_alteracao();

                CREATE TRIGGER adesoes_append_only BEFORE UPDATE OR DELETE ON adesoes
                    FOR EACH ROW EXECUTE FUNCTION recusar_alteracao();
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adesoes");

            migrationBuilder.DropTable(
                name: "termos_de_adesao");
        }
    }
}
