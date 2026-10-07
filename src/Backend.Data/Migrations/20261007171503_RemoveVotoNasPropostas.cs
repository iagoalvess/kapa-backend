using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveVotoNasPropostas : Migration
    {
        /// <inheritdoc />
        /// <remarks>07/10/2026: o voto da turma nas propostas da festa saiu — quem contrata é a comissão.</remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "votos_nas_propostas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "votos_nas_propostas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_da_festa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_votos_nas_propostas", x => x.id);
                    table.ForeignKey(
                        name: "fk_votos_nas_propostas_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_votos_nas_propostas_itens_da_festa_item_da_festa_id",
                        column: x => x.item_da_festa_id,
                        principalTable: "itens_da_festa",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_votos_nas_propostas_propostas_do_item_proposta_id",
                        column: x => x.proposta_id,
                        principalTable: "propostas_do_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_votos_nas_propostas_vinculos_de_formatura_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_votos_nas_propostas_formatura_id",
                table: "votos_nas_propostas",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_votos_nas_propostas_item_da_festa_id",
                table: "votos_nas_propostas",
                column: "item_da_festa_id");

            migrationBuilder.CreateIndex(
                name: "ix_votos_nas_propostas_proposta_id",
                table: "votos_nas_propostas",
                column: "proposta_id");

            migrationBuilder.CreateIndex(
                name: "ix_votos_nas_propostas_vinculo_id_item_da_festa_id",
                table: "votos_nas_propostas",
                columns: new[] { "vinculo_id", "item_da_festa_id" },
                unique: true);
        }
    }
}
