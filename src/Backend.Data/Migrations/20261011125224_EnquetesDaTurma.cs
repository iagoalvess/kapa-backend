using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnquetesDaTurma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE planos SET modulos = array_append(modulos, 'enquetes') WHERE codigo IN ('premium', 'premium-anual') AND NOT ('enquetes' = ANY(modulos));");
            migrationBuilder.CreateTable(
                name: "enquetes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pergunta = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descricao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    maximo_de_escolhas = table.Column<int>(type: "integer", nullable: true),
                    quorum_minimo_percentual = table.Column<int>(type: "integer", nullable: false),
                    total_aptos_na_publicacao = table.Column<int>(type: "integer", nullable: false),
                    primeira_resposta_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    encerra_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    encerrada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criada_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_enquetes", x => x.id);
                    table.CheckConstraint("ck_enquetes_escolhas", "(tipo = 'UnicaEscolha' AND maximo_de_escolhas IS NULL) OR (tipo = 'MultiplaEscolha' AND maximo_de_escolhas BETWEEN 1 AND 10)");
                    table.CheckConstraint("ck_enquetes_quorum", "quorum_minimo_percentual BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "fk_enquetes_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "opcoes_de_enquetes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    enquete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    texto = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_opcoes_de_enquetes", x => x.id);
                    table.ForeignKey(
                        name: "fk_opcoes_de_enquetes_enquetes_enquete_id",
                        column: x => x.enquete_id,
                        principalTable: "enquetes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_opcoes_de_enquetes_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "respostas_de_enquetes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    enquete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    respondida_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_respostas_de_enquetes", x => x.id);
                    table.ForeignKey(
                        name: "fk_respostas_de_enquetes_enquetes_enquete_id",
                        column: x => x.enquete_id,
                        principalTable: "enquetes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_respostas_de_enquetes_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_respostas_de_enquetes_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "escolhas_de_enquetes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resposta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opcao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_escolhas_de_enquetes", x => x.id);
                    table.ForeignKey(
                        name: "fk_escolhas_de_enquetes_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_escolhas_de_enquetes_opcoes_de_enquetes_opcao_id",
                        column: x => x.opcao_id,
                        principalTable: "opcoes_de_enquetes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_escolhas_de_enquetes_respostas_de_enquetes_resposta_id",
                        column: x => x.resposta_id,
                        principalTable: "respostas_de_enquetes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_enquetes_formatura_id",
                table: "enquetes",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_enquetes_formatura_id_encerra_em",
                table: "enquetes",
                columns: new[] { "formatura_id", "encerra_em" });

            migrationBuilder.CreateIndex(
                name: "ix_escolhas_de_enquetes_formatura_id",
                table: "escolhas_de_enquetes",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_escolhas_de_enquetes_opcao_id",
                table: "escolhas_de_enquetes",
                column: "opcao_id");

            migrationBuilder.CreateIndex(
                name: "ix_escolhas_de_enquetes_resposta_id_opcao_id",
                table: "escolhas_de_enquetes",
                columns: new[] { "resposta_id", "opcao_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_opcoes_de_enquetes_enquete_id",
                table: "opcoes_de_enquetes",
                column: "enquete_id");

            migrationBuilder.CreateIndex(
                name: "ix_opcoes_de_enquetes_formatura_id",
                table: "opcoes_de_enquetes",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_respostas_de_enquetes_enquete_id_vinculo_id",
                table: "respostas_de_enquetes",
                columns: new[] { "enquete_id", "vinculo_id" },
                unique: true,
                filter: "vinculo_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_respostas_de_enquetes_formatura_id",
                table: "respostas_de_enquetes",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_respostas_de_enquetes_vinculo_id",
                table: "respostas_de_enquetes",
                column: "vinculo_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE planos SET modulos = array_remove(modulos, 'enquetes') WHERE codigo IN ('premium', 'premium-anual');");
            migrationBuilder.DropTable(
                name: "escolhas_de_enquetes");

            migrationBuilder.DropTable(
                name: "opcoes_de_enquetes");

            migrationBuilder.DropTable(
                name: "respostas_de_enquetes");

            migrationBuilder.DropTable(
                name: "enquetes");
        }
    }
}
