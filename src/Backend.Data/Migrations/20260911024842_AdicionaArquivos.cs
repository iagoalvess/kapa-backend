using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaArquivos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "arquivos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    chave = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tamanho = table.Column<long>(type: "bigint", nullable: false),
                    categoria = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    enviado_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arquivos", x => x.id);
                    table.ForeignKey(
                        name: "fk_arquivos_asp_net_users_enviado_por_id",
                        column: x => x.enviado_por_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_arquivos_chave",
                table: "arquivos",
                column: "chave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_arquivos_enviado_por_id_categoria_criado_em",
                table: "arquivos",
                columns: new[] { "enviado_por_id", "categoria", "criado_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "arquivos");
        }
    }
}
