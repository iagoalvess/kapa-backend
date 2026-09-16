using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaMuralEAcervo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "texto",
                table: "avisos",
                newName: "conteudo");

            migrationBuilder.AlterColumn<string>(
                name: "conteudo",
                table: "avisos",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AddColumn<bool>(
                name: "destaque",
                table: "avisos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "fixado",
                table: "avisos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "publicado_em",
                table: "avisos",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "publicado_por_usuario_id",
                table: "avisos",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "titulo",
                table: "avisos",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "visibilidade",
                table: "avisos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Turma");

            migrationBuilder.CreateTable(
                name: "documentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    categoria = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    arquivo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visibilidade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    versao = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentos", x => x.id);
                    table.ForeignKey(
                        name: "fk_documentos_arquivos_arquivo_id",
                        column: x => x.arquivo_id,
                        principalTable: "arquivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documentos_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documentos_arquivo_id",
                table: "documentos",
                column: "arquivo_id");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_formatura_id",
                table: "documentos",
                column: "formatura_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documentos");

            migrationBuilder.DropColumn(
                name: "destaque",
                table: "avisos");

            migrationBuilder.DropColumn(
                name: "fixado",
                table: "avisos");

            migrationBuilder.DropColumn(
                name: "publicado_em",
                table: "avisos");

            migrationBuilder.DropColumn(
                name: "publicado_por_usuario_id",
                table: "avisos");

            migrationBuilder.DropColumn(
                name: "titulo",
                table: "avisos");

            migrationBuilder.DropColumn(
                name: "visibilidade",
                table: "avisos");

            migrationBuilder.AlterColumn<string>(
                name: "conteudo",
                table: "avisos",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20000)",
                oldMaxLength: 20000);

            migrationBuilder.RenameColumn(
                name: "conteudo",
                table: "avisos",
                newName: "texto");
        }
    }
}
