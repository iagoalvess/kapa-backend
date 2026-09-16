using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaContasDeRecebimento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contas_de_recebimento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_de_chave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    chave = table.Column<string>(type: "character varying(77)", maxLength: 77, nullable: false),
                    nome_do_titular = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cidade = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    conferida_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    conferida_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contas_de_recebimento", x => x.id);
                    table.ForeignKey(
                        name: "fk_contas_de_recebimento_asp_net_users_conferida_por_usuario_id",
                        column: x => x.conferida_por_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contas_de_recebimento_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contas_de_recebimento_conferida_por_usuario_id",
                table: "contas_de_recebimento",
                column: "conferida_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_contas_de_recebimento_formatura_id",
                table: "contas_de_recebimento",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_contas_de_recebimento_uma_por_formatura",
                table: "contas_de_recebimento",
                column: "formatura_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contas_de_recebimento");
        }
    }
}
