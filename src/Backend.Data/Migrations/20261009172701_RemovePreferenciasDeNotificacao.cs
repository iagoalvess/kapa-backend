using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemovePreferenciasDeNotificacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "preferencias_de_notificacao");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "preferencias_de_notificacao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preferencias_de_notificacao", x => x.id);
                    table.ForeignKey(
                        name: "fk_preferencias_de_notificacao_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_preferencias_de_notificacao_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_preferencias_de_notificacao_formatura_id",
                table: "preferencias_de_notificacao",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_preferencias_de_notificacao_vinculo_id_tipo",
                table: "preferencias_de_notificacao",
                columns: new[] { "vinculo_id", "tipo" },
                unique: true);
        }
    }
}
