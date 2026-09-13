using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// Planos, assinaturas e eventos de cobrança.
    /// </summary>
    /// <remarks>
    /// Os planos entram aqui, e não no <c>SeedInicial</c>, pelo mesmo motivo dos documentos legais:
    /// o seed é desligado em produção. Os preços são placeholders até o produto fixar a tabela.
    /// <para>
    /// <c>eventos_de_cobranca</c> é append-only no banco (reaproveita <c>recusar_alteracao()</c>).
    /// </para>
    /// </remarks>
    public partial class AdicionaAssinaturas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "eventos_de_cobranca",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_externo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tipo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    assinatura_id = table.Column<Guid>(type: "uuid", nullable: true),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload = table.Column<string>(type: "text", nullable: false),
                    recebido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_de_cobranca", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "planos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    preco_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    ciclo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    limite_de_formandos = table.Column<int>(type: "integer", nullable: false),
                    recomendado = table.Column<bool>(type: "boolean", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assinaturas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plano_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    id_externo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    vigente_ate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ultimo_aviso_de_vencimento = table.Column<int>(type: "integer", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assinaturas", x => x.id);
                    table.ForeignKey(
                        name: "fk_assinaturas_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assinaturas_planos_plano_id",
                        column: x => x.plano_id,
                        principalTable: "planos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_formatura_id",
                table: "assinaturas",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_pendente_por_formatura",
                table: "assinaturas",
                column: "formatura_id",
                unique: true,
                filter: "status = 'Pendente'");

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_plano_id",
                table: "assinaturas",
                column: "plano_id");

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_status_vigente_ate",
                table: "assinaturas",
                columns: new[] { "status", "vigente_ate" });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_de_cobranca_assinatura_id",
                table: "eventos_de_cobranca",
                column: "assinatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_de_cobranca_id_externo",
                table: "eventos_de_cobranca",
                column: "id_externo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_planos_codigo",
                table: "planos",
                column: "codigo",
                unique: true);

            var publicadoEm = new DateTime(2026, 9, 12, 3, 0, 0, DateTimeKind.Utc);

            migrationBuilder.InsertData(
                table: "planos",
                columns: ["id", "codigo", "nome", "preco_em_centavos", "ciclo", "limite_de_formandos", "recomendado", "ativo", "criado_em", "atualizado_em"],
                values: new object[,]
                {
                    { new Guid("0199407e-0000-7000-8000-000000000001"), "essencial", "Essencial", 14990L, "Mensal", 60, false, true, publicadoEm, publicadoEm },
                    { new Guid("0199407e-0000-7000-8000-000000000002"), "completo", "Completo", 34990L, "Mensal", 150, true, true, publicadoEm, publicadoEm },
                    { new Guid("0199407e-0000-7000-8000-000000000003"), "ampliado", "Ampliado", 59990L, "Mensal", 400, false, true, publicadoEm, publicadoEm },
                }
            );

            migrationBuilder.Sql(
                """
                CREATE TRIGGER eventos_de_cobranca_append_only BEFORE UPDATE OR DELETE ON eventos_de_cobranca
                    FOR EACH ROW EXECUTE FUNCTION recusar_alteracao();
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assinaturas");

            migrationBuilder.DropTable(
                name: "eventos_de_cobranca");

            migrationBuilder.DropTable(
                name: "planos");
        }
    }
}
