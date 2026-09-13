using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaPerfisDeFormandos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "perfis_de_formandos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome_completo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    nome_no_diploma = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cpf = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    rg = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    matricula = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    telefone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    data_de_nascimento = table.Column<DateOnly>(type: "date", nullable: true),
                    observacoes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    endereco_cep = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    endereco_logradouro = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    endereco_numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    endereco_complemento = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    endereco_bairro = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    endereco_cidade = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    endereco_uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    contato_de_emergencia_nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contato_de_emergencia_telefone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    contato_de_emergencia_parentesco = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    foto_arquivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completude = table.Column<int>(type: "integer", nullable: false),
                    essencial_preenchido = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_perfis_de_formandos", x => x.id);
                    table.ForeignKey(
                        name: "fk_perfis_de_formandos_arquivos_foto_arquivo_id",
                        column: x => x.foto_arquivo_id,
                        principalTable: "arquivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_perfis_de_formandos_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_perfis_de_formandos_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "correcoes_de_perfil",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    perfil_id = table.Column<Guid>(type: "uuid", nullable: false),
                    autor_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    corrigido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    secoes = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_correcoes_de_perfil", x => x.id);
                    table.ForeignKey(
                        name: "fk_correcoes_de_perfil_asp_net_users_autor_usuario_id",
                        column: x => x.autor_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_correcoes_de_perfil_perfis_de_formandos_perfil_id",
                        column: x => x.perfil_id,
                        principalTable: "perfis_de_formandos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_correcoes_de_perfil_autor_usuario_id",
                table: "correcoes_de_perfil",
                column: "autor_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_correcoes_de_perfil_perfil_id",
                table: "correcoes_de_perfil",
                column: "perfil_id");

            migrationBuilder.CreateIndex(
                name: "ix_perfis_de_formandos_formatura_id",
                table: "perfis_de_formandos",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_perfis_de_formandos_foto_arquivo_id",
                table: "perfis_de_formandos",
                column: "foto_arquivo_id");

            migrationBuilder.CreateIndex(
                name: "ix_perfis_de_formandos_vinculo_id",
                table: "perfis_de_formandos",
                column: "vinculo_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "correcoes_de_perfil");

            migrationBuilder.DropTable(
                name: "perfis_de_formandos");
        }
    }
}
