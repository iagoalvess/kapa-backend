using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// Sprint 48 — o ciclo de vida da cesta: a solicitação de cancelamento (D8), a suspensão das parcelas (D12), o alvo
    /// do rateio (D19), o lançamento avulso (D23), a descrição livre (D26), o "cancelável até" (D36) e o aditivo (D38).
    /// </summary>
    /// <remarks>
    /// O aditivo é append-only como a adesão, com a mesma <c>recusar_alteracao()</c> da migration inicial: é prova do
    /// que o formando aceitou.
    /// </remarks>
    public partial class CicloDeVidaDaCesta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "observacao",
                table: "pedidos",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "suspensa_ate",
                table: "parcelas",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "alvo_do_rateio",
                table: "itens_de_cobranca",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<DateOnly>(
                name: "cancelavel_ate",
                table: "itens_de_cobranca",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "vinculo_do_lancamento",
                table: "itens_de_cobranca",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "observacao",
                table: "escolhas_da_cesta",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "aditivos_da_adesao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adesao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash_do_conteudo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    aceito_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    endereco_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    email_do_aceite = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    conteudo = table.Column<string>(type: "text", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aditivos_da_adesao", x => x.id);
                    table.ForeignKey(
                        name: "fk_aditivos_da_adesao_adesoes_adesao_id",
                        column: x => x.adesao_id,
                        principalTable: "adesoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_aditivos_da_adesao_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_aditivos_da_adesao_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "solicitacoes_de_cancelamento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_de_cobranca_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    pedido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resposta_ate = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    respondido_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    respondido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    motivo_da_resposta = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitacoes_de_cancelamento", x => x.id);
                    table.ForeignKey(
                        name: "fk_solicitacoes_de_cancelamento_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_solicitacoes_de_cancelamento_itens_de_cobranca_item_de_cobr",
                        column: x => x.item_de_cobranca_id,
                        principalTable: "itens_de_cobranca",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_solicitacoes_de_cancelamento_pedidos_pedido_id",
                        column: x => x.pedido_id,
                        principalTable: "pedidos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_solicitacoes_de_cancelamento_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_itens_de_cobranca_vinculo_do_lancamento",
                table: "itens_de_cobranca",
                column: "vinculo_do_lancamento");

            migrationBuilder.CreateIndex(
                name: "ix_aditivos_da_adesao_adesao_id",
                table: "aditivos_da_adesao",
                column: "adesao_id");

            migrationBuilder.CreateIndex(
                name: "ix_aditivos_da_adesao_formatura_id",
                table: "aditivos_da_adesao",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_aditivos_da_adesao_vinculo_id",
                table: "aditivos_da_adesao",
                column: "vinculo_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_cancelamento_formatura_id",
                table: "solicitacoes_de_cancelamento",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_cancelamento_formatura_id_status",
                table: "solicitacoes_de_cancelamento",
                columns: new[] { "formatura_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_cancelamento_item_de_cobranca_id",
                table: "solicitacoes_de_cancelamento",
                column: "item_de_cobranca_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_cancelamento_pedido_id",
                table: "solicitacoes_de_cancelamento",
                column: "pedido_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_cancelamento_uma_aberta",
                table: "solicitacoes_de_cancelamento",
                columns: new[] { "vinculo_id", "item_de_cobranca_id" },
                unique: true,
                filter: "status = 'Aberto'");

            migrationBuilder.AddForeignKey(
                name: "fk_itens_de_cobranca_vinculos_vinculo_do_lancamento",
                table: "itens_de_cobranca",
                column: "vinculo_do_lancamento",
                principalTable: "vinculos_de_formatura",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER aditivos_da_adesao_append_only BEFORE UPDATE OR DELETE ON aditivos_da_adesao
                    FOR EACH ROW EXECUTE FUNCTION recusar_alteracao();
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_itens_de_cobranca_vinculos_vinculo_do_lancamento",
                table: "itens_de_cobranca");

            migrationBuilder.DropTable(
                name: "aditivos_da_adesao");

            migrationBuilder.DropTable(
                name: "solicitacoes_de_cancelamento");

            migrationBuilder.DropIndex(
                name: "ix_itens_de_cobranca_vinculo_do_lancamento",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "observacao",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "suspensa_ate",
                table: "parcelas");

            migrationBuilder.DropColumn(
                name: "alvo_do_rateio",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "cancelavel_ate",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "vinculo_do_lancamento",
                table: "itens_de_cobranca");

            migrationBuilder.DropColumn(
                name: "observacao",
                table: "escolhas_da_cesta");
        }
    }
}
