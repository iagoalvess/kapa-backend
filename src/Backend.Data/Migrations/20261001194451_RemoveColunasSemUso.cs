using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveColunasSemUso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_termos_de_adesao_asp_net_users_publicado_por_usuario_id",
                table: "termos_de_adesao");

            migrationBuilder.DropIndex(
                name: "ix_termos_de_adesao_publicado_por_usuario_id",
                table: "termos_de_adesao");

            migrationBuilder.DropColumn(
                name: "publicado_por_usuario_id",
                table: "termos_de_adesao");

            migrationBuilder.DropColumn(
                name: "gerado_em",
                table: "resumos_de_termo");

            migrationBuilder.DropColumn(
                name: "modelo",
                table: "resumos_de_termo");

            migrationBuilder.DropColumn(
                name: "rota",
                table: "eventos");

            migrationBuilder.DropColumn(
                name: "valor_unitario_em_centavos",
                table: "compras_de_convite");

            migrationBuilder.DropColumn(
                name: "publicado_em",
                table: "avisos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "publicado_por_usuario_id",
                table: "termos_de_adesao",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "gerado_em",
                table: "resumos_de_termo",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "modelo",
                table: "resumos_de_termo",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "rota",
                table: "eventos",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "valor_unitario_em_centavos",
                table: "compras_de_convite",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "publicado_em",
                table: "avisos",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "ix_termos_de_adesao_publicado_por_usuario_id",
                table: "termos_de_adesao",
                column: "publicado_por_usuario_id");

            migrationBuilder.AddForeignKey(
                name: "fk_termos_de_adesao_asp_net_users_publicado_por_usuario_id",
                table: "termos_de_adesao",
                column: "publicado_por_usuario_id",
                principalTable: "usuarios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
