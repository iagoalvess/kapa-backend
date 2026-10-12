using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class CondicoesFinanceiras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "apenas_gestao",
                table: "vinculos_de_formatura",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "desconto_em_centavos",
                table: "escolhas_da_cesta",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "condicoes_financeiras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    concedida_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    beneficios = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_condicoes_financeiras", x => x.id);
                    table.CheckConstraint("ck_condicoes_financeiras_dono", "(vinculo_id is null) <> (email is null)");
                    table.ForeignKey(
                        name: "fk_condicoes_financeiras_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_condicoes_financeiras_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_condicoes_financeiras_formatura_id",
                table: "condicoes_financeiras",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_condicoes_financeiras_formatura_id_email",
                table: "condicoes_financeiras",
                columns: new[] { "formatura_id", "email" },
                unique: true,
                filter: "email is not null");

            migrationBuilder.CreateIndex(
                name: "ix_condicoes_financeiras_vinculo_id",
                table: "condicoes_financeiras",
                column: "vinculo_id",
                unique: true,
                filter: "vinculo_id is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "condicoes_financeiras");

            migrationBuilder.DropColumn(
                name: "apenas_gestao",
                table: "vinculos_de_formatura");

            migrationBuilder.DropColumn(
                name: "desconto_em_centavos",
                table: "escolhas_da_cesta");
        }
    }
}
