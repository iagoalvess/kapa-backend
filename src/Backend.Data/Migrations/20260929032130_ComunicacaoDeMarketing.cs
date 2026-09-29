using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class ComunicacaoDeMarketing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "receber_comunicacao_do_kapa",
                table: "usuarios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "categoria",
                table: "emails_fila",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "link_de_descadastro",
                table: "emails_fila",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "usuario_id",
                table: "emails_fila",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "consentimentos_de_marketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aceito = table.Column<bool>(type: "boolean", nullable: false),
                    origem = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    versao_do_texto = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    registrado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    endereco_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consentimentos_de_marketing", x => x.id);
                    table.ForeignKey(
                        name: "fk_consentimentos_de_marketing_asp_net_users_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "envios_de_marketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jornada = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    enviado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_envios_de_marketing", x => x.id);
                    table.ForeignKey(
                        name: "fk_envios_de_marketing_asp_net_users_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_envios_de_marketing_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consentimentos_de_marketing_usuario_id_registrado_em",
                table: "consentimentos_de_marketing",
                columns: new[] { "usuario_id", "registrado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_envios_de_marketing_formatura_id",
                table: "envios_de_marketing",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_envios_de_marketing_usuario_id_enviado_em",
                table: "envios_de_marketing",
                columns: new[] { "usuario_id", "enviado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_envios_de_marketing_usuario_id_formatura_id_jornada",
                table: "envios_de_marketing",
                columns: new[] { "usuario_id", "formatura_id", "jornada" },
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER consentimentos_de_marketing_append_only BEFORE UPDATE OR DELETE ON consentimentos_de_marketing
                    FOR EACH ROW EXECUTE FUNCTION recusar_alteracao();
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consentimentos_de_marketing");

            migrationBuilder.DropTable(
                name: "envios_de_marketing");

            migrationBuilder.DropColumn(
                name: "receber_comunicacao_do_kapa",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "categoria",
                table: "emails_fila");

            migrationBuilder.DropColumn(
                name: "link_de_descadastro",
                table: "emails_fila");

            migrationBuilder.DropColumn(
                name: "usuario_id",
                table: "emails_fila");
        }
    }
}
