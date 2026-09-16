using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaPagamentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "pago_em",
                table: "parcelas",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "valor_pago_em_centavos",
                table: "parcelas",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "informes_de_pagamento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcela_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pago_em = table.Column<DateOnly>(type: "date", nullable: false),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    comprovante_arquivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    motivo_da_recusa = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    conferido_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    conferido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_informes_de_pagamento", x => x.id);
                    table.ForeignKey(
                        name: "fk_informes_de_pagamento_arquivos_comprovante_arquivo_id",
                        column: x => x.comprovante_arquivo_id,
                        principalTable: "arquivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_informes_de_pagamento_asp_net_users_conferido_por_usuario_id",
                        column: x => x.conferido_por_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_informes_de_pagamento_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_informes_de_pagamento_parcelas_parcela_id",
                        column: x => x.parcela_id,
                        principalTable: "parcelas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_informes_de_pagamento_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recebimentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcela_id = table.Column<Guid>(type: "uuid", nullable: false),
                    informe_id = table.Column<Guid>(type: "uuid", nullable: true),
                    forma = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    devido_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    pago_em = table.Column<DateOnly>(type: "date", nullable: false),
                    comprovante_arquivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    baixado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    baixado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    endereco_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    estornado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    estornado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    justificativa_do_estorno = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recebimentos", x => x.id);
                    table.ForeignKey(
                        name: "fk_recebimentos_arquivos_comprovante_arquivo_id",
                        column: x => x.comprovante_arquivo_id,
                        principalTable: "arquivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recebimentos_asp_net_users_baixado_por_usuario_id",
                        column: x => x.baixado_por_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recebimentos_asp_net_users_estornado_por_usuario_id",
                        column: x => x.estornado_por_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recebimentos_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recebimentos_informes_de_pagamento_informe_id",
                        column: x => x.informe_id,
                        principalTable: "informes_de_pagamento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recebimentos_parcelas_parcela_id",
                        column: x => x.parcela_id,
                        principalTable: "parcelas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_informes_de_pagamento_comprovante_arquivo_id",
                table: "informes_de_pagamento",
                column: "comprovante_arquivo_id");

            migrationBuilder.CreateIndex(
                name: "ix_informes_de_pagamento_conferido_por_usuario_id",
                table: "informes_de_pagamento",
                column: "conferido_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_informes_de_pagamento_formatura_id",
                table: "informes_de_pagamento",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_informes_de_pagamento_parcela_id",
                table: "informes_de_pagamento",
                column: "parcela_id",
                unique: true,
                filter: "status = 'Pendente'");

            migrationBuilder.CreateIndex(
                name: "ix_informes_de_pagamento_status_criado_em",
                table: "informes_de_pagamento",
                columns: new[] { "status", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_informes_de_pagamento_vinculo_id",
                table: "informes_de_pagamento",
                column: "vinculo_id");

            migrationBuilder.CreateIndex(
                name: "ix_recebimentos_baixado_por_usuario_id",
                table: "recebimentos",
                column: "baixado_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_recebimentos_comprovante_arquivo_id",
                table: "recebimentos",
                column: "comprovante_arquivo_id");

            migrationBuilder.CreateIndex(
                name: "ix_recebimentos_estornado_por_usuario_id",
                table: "recebimentos",
                column: "estornado_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_recebimentos_formatura_id",
                table: "recebimentos",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_recebimentos_informe_id",
                table: "recebimentos",
                column: "informe_id");

            migrationBuilder.CreateIndex(
                name: "ix_recebimentos_parcela_id",
                table: "recebimentos",
                column: "parcela_id",
                unique: true,
                filter: "estornado_em IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recebimentos");

            migrationBuilder.DropTable(
                name: "informes_de_pagamento");

            migrationBuilder.DropColumn(
                name: "pago_em",
                table: "parcelas");

            migrationBuilder.DropColumn(
                name: "valor_pago_em_centavos",
                table: "parcelas");
        }
    }
}
