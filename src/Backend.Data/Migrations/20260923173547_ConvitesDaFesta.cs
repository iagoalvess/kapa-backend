using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConvitesDaFesta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "convites_do_evento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sequencial = table.Column<int>(type: "integer", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nome_do_convidado = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    tipo_do_documento = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    numero_do_documento = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    email_do_convidado = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    emitido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revogado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    motivo_da_revogacao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_convites_do_evento", x => x.id);
                    table.CheckConstraint("ck_convites_do_evento_sequencial", "sequencial >= 0");
                    table.ForeignKey(
                        name: "fk_convites_do_evento_eventos_da_turma_evento_id",
                        column: x => x.evento_id,
                        principalTable: "eventos_da_turma",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_convites_do_evento_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_convites_do_evento_pedidos_pedido_id",
                        column: x => x.pedido_id,
                        principalTable: "pedidos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_convites_do_evento_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "check_ins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    convite_id = table.Column<Guid>(type: "uuid", nullable: false),
                    validado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    validado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    aparelho = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    desfeito_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    desfeito_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_ins", x => x.id);
                    table.ForeignKey(
                        name: "fk_check_ins_convites_do_evento_convite_id",
                        column: x => x.convite_id,
                        principalTable: "convites_do_evento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_check_ins_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_check_ins_convite_id_validado_em",
                table: "check_ins",
                columns: new[] { "convite_id", "validado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_check_ins_formatura_id",
                table: "check_ins",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ux_check_ins_ativo",
                table: "check_ins",
                column: "convite_id",
                unique: true,
                filter: "desfeito_em IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_convites_do_evento_codigo",
                table: "convites_do_evento",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_convites_do_evento_formatura_id",
                table: "convites_do_evento",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_convites_do_evento_formatura_id_evento_id",
                table: "convites_do_evento",
                columns: new[] { "formatura_id", "evento_id" });

            migrationBuilder.CreateIndex(
                name: "ix_convites_do_evento_pedido_id",
                table: "convites_do_evento",
                column: "pedido_id");

            migrationBuilder.CreateIndex(
                name: "ix_convites_do_evento_vinculo_id",
                table: "convites_do_evento",
                column: "vinculo_id");

            migrationBuilder.CreateIndex(
                name: "ux_convites_do_evento_posicao_valida",
                table: "convites_do_evento",
                columns: new[] { "evento_id", "vinculo_id", "pedido_id", "sequencial" },
                unique: true,
                filter: "vinculo_id IS NOT NULL AND revogado_em IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "check_ins");

            migrationBuilder.DropTable(
                name: "convites_do_evento");
        }
    }
}
