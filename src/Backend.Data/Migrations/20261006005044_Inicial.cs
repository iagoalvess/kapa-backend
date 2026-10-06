using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Backend.Data.Seed;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// Esquema inteiro, os planos à venda e a versão 1 dos documentos legais.
    /// </summary>
    /// <remarks>
    /// Nasceu da consolidação das 65 migrations de desenvolvimento, em 05/10/2026, antes de haver banco em
    /// produção. A prova da troca foi comparar, objeto a objeto, um banco montado pelas 65 com um montado por
    /// esta: colunas, índices, restrições, gatilhos, funções e o catálogo. O que o EF não gera sozinho está no
    /// fim de <see cref="Up"/>:
    /// <list type="bullet">
    /// <item><c>recusar_alteracao()</c> e os gatilhos <c>*_append_only</c> das sete tabelas que são prova
    /// (documentos legais, consentimentos, consentimentos de marketing, eventos de cobrança, termos, adesões e
    /// aditivos);</item>
    /// <item>a versão 1 dos Termos de Uso e da Política de Privacidade;</item>
    /// <item>o catálogo de planos, que nasce aqui e não no <c>SeedDePlanos</c> porque o seed é desligado em
    /// produção. O "Ampliado", fora de linha desde 15/09/2026 e sem turma em banco novo, ficou de fora.</item>
    /// </list>
    /// </remarks>
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,");

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documentos_legais",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    versao = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    conteudo = table.Column<string>(type: "text", nullable: false),
                    vigente_desde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentos_legais", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "emails_fila",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    para = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    assunto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    corpo_html = table.Column<string>(type: "text", nullable: false),
                    anexo_nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    anexo_content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    anexo_conteudo = table.Column<byte[]>(type: "bytea", nullable: true),
                    categoria = table.Column<int>(type: "integer", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    link_de_descadastro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    prioridade = table.Column<int>(type: "integer", nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    proxima_tentativa_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ultimo_erro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    enviado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emails_fila", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "eventos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ocorrido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    dados = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "eventos_de_cobranca",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_externo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tipo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    assinatura_id = table.Column<Guid>(type: "uuid", nullable: true),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload = table.Column<string>(type: "text", nullable: false),
                    recebido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_de_cobranca", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "formaturas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    instituicao = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    curso = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ano = table.Column<int>(type: "integer", nullable: false),
                    semestre = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    criado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ativada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    encerrada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status_desde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    eliminada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_formaturas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "perfis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_perfis", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "planos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    descricao = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    modulos = table.Column<List<string>>(type: "text[]", nullable: false),
                    preco_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    preco_cheio_em_centavos = table.Column<long>(type: "bigint", nullable: true),
                    ciclo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    limite_de_formandos = table.Column<int>(type: "integer", nullable: false),
                    recomendado = table.Column<bool>(type: "boolean", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    anonimizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    receber_comunicacao_do_kapa = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "avisos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    conteudo = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    visibilidade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fixado = table.Column<bool>(type: "boolean", nullable: false),
                    destaque = table.Column<bool>(type: "boolean", nullable: false),
                    publicado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avisos", x => x.id);
                    table.ForeignKey(
                        name: "fk_avisos_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eventos_da_turma",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    situacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    hora = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    local = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    descricao = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    capacidade = table.Column<int>(type: "integer", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_da_turma", x => x.id);
                    table.CheckConstraint("ck_eventos_da_turma_capacidade", "capacidade IS NULL OR capacidade > 0");
                    table.ForeignKey(
                        name: "fk_eventos_da_turma_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fornecedores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    documento = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    categoria = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    observacoes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fornecedores", x => x.id);
                    table.ForeignKey(
                        name: "fk_fornecedores_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "planos_de_cobranca",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    vigente_desde = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    percentual_de_multa = table.Column<int>(type: "integer", nullable: false),
                    percentual_de_juros_ao_mes = table.Column<int>(type: "integer", nullable: false),
                    carencia_em_dias = table.Column<int>(type: "integer", nullable: false),
                    percentual_de_desconto_por_antecipacao = table.Column<int>(type: "integer", nullable: false),
                    dias_minimos_para_desconto = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planos_de_cobranca", x => x.id);
                    table.ForeignKey(
                        name: "fk_planos_de_cobranca_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "regras_de_notificacao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gatilho = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dias_de_deslocamento = table.Column<int>(type: "integer", nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regras_de_notificacao", x => x.id);
                    table.ForeignKey(
                        name: "fk_regras_de_notificacao_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "saloes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    largura = table.Column<int>(type: "integer", nullable: false),
                    altura = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    elementos = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saloes", x => x.id);
                    table.CheckConstraint("ck_saloes_altura", "altura > 0");
                    table.CheckConstraint("ck_saloes_largura", "largura > 0");
                    table.ForeignKey(
                        name: "fk_saloes_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "termos_de_adesao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    versao = table.Column<int>(type: "integer", nullable: false),
                    conteudo = table.Column<string>(type: "text", nullable: false),
                    vigente_desde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_termos_de_adesao", x => x.id);
                    table.ForeignKey(
                        name: "fk_termos_de_adesao_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "perfis_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_perfis_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_perfis_claims_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "perfis",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assinaturas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plano_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    id_externo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    meio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    plano_do_proximo_ciclo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vigente_ate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ultimo_aviso_de_vencimento = table.Column<int>(type: "integer", nullable: true),
                    ultimo_evento_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assinaturas", x => x.id);
                    table.ForeignKey(
                        name: "fk_assinaturas_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assinaturas_planos_plano_do_proximo_ciclo_id",
                        column: x => x.plano_do_proximo_ciclo_id,
                        principalTable: "planos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assinaturas_planos_plano_id",
                        column: x => x.plano_id,
                        principalTable: "planos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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

            migrationBuilder.CreateTable(
                name: "consentimentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_legal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    versao = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    aceito_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    endereco_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    revogado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consentimentos", x => x.id);
                    table.ForeignKey(
                        name: "fk_consentimentos_asp_net_users_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_consentimentos_documentos_legais_documento_legal_id",
                        column: x => x.documento_legal_id,
                        principalTable: "documentos_legais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                name: "contas_de_recebimento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_de_chave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    chave = table.Column<string>(type: "character varying(77)", maxLength: 77, nullable: true),
                    nome_do_titular = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cidade = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    banco = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    agencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    conta = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tipo_de_conta = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    titular_da_conta = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    dinheiro_com = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    dinheiro_onde = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    conferida_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    conferida_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contas_de_recebimento", x => x.id);
                    table.ForeignKey(
                        name: "fk_contas_de_recebimento_asp_net_users_conferida_por_usuario_id",
                        column: x => x.conferida_por_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contas_de_recebimento_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "convites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    papel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    usos_maximos = table.Column<int>(type: "integer", nullable: true),
                    usos_feitos = table.Column<int>(type: "integer", nullable: false),
                    revogado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_convites", x => x.id);
                    table.ForeignKey(
                        name: "fk_convites_asp_net_users_criado_por_usuario_id",
                        column: x => x.criado_por_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_convites_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "credenciais_de_provedor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_token = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    refresh_token = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    id_no_provedor = table.Column<long>(type: "bigint", nullable: false),
                    conta_no_provedor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cadastrada_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chave_publica = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cartao_ligado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cartao_ligado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    taxa_do_cartao_repassada = table.Column<int>(type: "integer", nullable: true),
                    cobranca_automatica_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credenciais_de_provedor", x => x.id);
                    table.ForeignKey(
                        name: "fk_credenciais_de_provedor_asp_net_users_cadastrada_por_usuario_",
                        column: x => x.cadastrada_por_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_credenciais_de_provedor_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
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

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revogado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    substituido_por_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    criado_por_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_asp_net_users_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "solicitacoes_de_privacidade",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    titular_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    prazo_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    confirmada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    concluida_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    arquivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    motivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitacoes_de_privacidade", x => x.id);
                    table.ForeignKey(
                        name: "fk_solicitacoes_de_privacidade_asp_net_users_titular_usuario_id",
                        column: x => x.titular_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "usuarios_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_usuarios_claims_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuarios_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_usuarios_logins_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuarios_perfis",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios_perfis", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_usuarios_perfis_perfis_role_id",
                        column: x => x.role_id,
                        principalTable: "perfis",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_usuarios_perfis_usuarios_user_id",
                        column: x => x.user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuarios_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_usuarios_tokens_usuarios_user_id",
                        column: x => x.user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vinculos_de_formatura",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    papel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    desligado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    motivo_do_desligamento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    detalhe_do_desligamento = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    mural_visto_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vinculos_de_formatura", x => x.id);
                    table.ForeignKey(
                        name: "fk_vinculos_de_formatura_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vinculos_de_formatura_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "resumos_de_termo",
                columns: table => new
                {
                    termo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    texto = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resumos_de_termo", x => x.termo_id);
                    table.ForeignKey(
                        name: "fk_resumos_de_termo_termos_de_adesao_termo_id",
                        column: x => x.termo_id,
                        principalTable: "termos_de_adesao",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cobrancas_da_assinatura",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assinatura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plano_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    meio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    situacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    id_do_pagamento = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    paga_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    valor_estornado_em_centavos = table.Column<long>(type: "bigint", nullable: true),
                    estornada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cobrancas_da_assinatura", x => x.id);
                    table.ForeignKey(
                        name: "fk_cobrancas_da_assinatura_assinaturas_assinatura_id",
                        column: x => x.assinatura_id,
                        principalTable: "assinaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cobrancas_da_assinatura_planos_plano_id",
                        column: x => x.plano_id,
                        principalTable: "planos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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

            migrationBuilder.CreateTable(
                name: "solicitacoes_de_relatorio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    de = table.Column<DateOnly>(type: "date", nullable: false),
                    ate = table.Column<DateOnly>(type: "date", nullable: false),
                    fornecedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    categoria = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    situacao_da_despesa = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    formando_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_de_cobranca_id = table.Column<Guid>(type: "uuid", nullable: true),
                    situacao_da_parcela = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    solicitada_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    arquivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitacoes_de_relatorio", x => x.id);
                    table.ForeignKey(
                        name: "fk_solicitacoes_de_relatorio_arquivos_arquivo_id",
                        column: x => x.arquivo_id,
                        principalTable: "arquivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_solicitacoes_de_relatorio_asp_net_users_solicitada_por_usuari",
                        column: x => x.solicitada_por_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_solicitacoes_de_relatorio_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "aceites_de_convite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    convite_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aceito_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    endereco_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aceites_de_convite", x => x.id);
                    table.ForeignKey(
                        name: "fk_aceites_de_convite_asp_net_users_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_aceites_de_convite_convites_convite_id",
                        column: x => x.convite_id,
                        principalTable: "convites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "adesoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    termo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    versao = table.Column<int>(type: "integer", nullable: false),
                    hash_do_conteudo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    aceito_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    endereco_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    email_do_aceite = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    nome_completo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cpf = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    plano_aceito = table.Column<string>(type: "text", nullable: false),
                    cpf_hmac = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adesoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_adesoes_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_adesoes_termos_de_adesao_termo_id",
                        column: x => x.termo_id,
                        principalTable: "termos_de_adesao",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_adesoes_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mesas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identificacao = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    lugares = table.Column<int>(type: "integer", nullable: false),
                    observacao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reservada = table.Column<bool>(type: "boolean", nullable: false),
                    formato = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    x = table.Column<int>(type: "integer", nullable: true),
                    y = table.Column<int>(type: "integer", nullable: true),
                    girada = table.Column<bool>(type: "boolean", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mesas", x => x.id);
                    table.CheckConstraint("ck_mesas_lugares", "lugares > 0");
                    table.CheckConstraint("ck_mesas_posicao", "(x IS NULL) = (y IS NULL)");
                    table.CheckConstraint("ck_mesas_reservada_sem_dono", "NOT (reservada AND vinculo_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_mesas_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mesas_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
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
                name: "preferencias_de_notificacao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "itens_da_festa",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    categoria = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    o_que_inclui = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rateio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valor_previsto_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    quantidade_estimada = table.Column<int>(type: "integer", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    cancelado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_itens_da_festa", x => x.id);
                    table.ForeignKey(
                        name: "fk_itens_da_festa_documentos_documento_id",
                        column: x => x.documento_id,
                        principalTable: "documentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_itens_da_festa_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outras_receitas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    origem = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    categoria = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estorno_de_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outras_receitas", x => x.id);
                    table.ForeignKey(
                        name: "fk_outras_receitas_documentos_documento_id",
                        column: x => x.documento_id,
                        principalTable: "documentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_outras_receitas_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outras_receitas_outras_receitas_estorno_de_id",
                        column: x => x.estorno_de_id,
                        principalTable: "outras_receitas",
                        principalColumn: "id");
                });

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

            migrationBuilder.CreateTable(
                name: "despesas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lancamento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fornecedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_da_festa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    categoria = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    competencia = table.Column<DateOnly>(type: "date", nullable: false),
                    vencimento = table.Column<DateOnly>(type: "date", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    total_de_parcelas = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pago_em = table.Column<DateOnly>(type: "date", nullable: true),
                    comprovante_arquivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_despesas", x => x.id);
                    table.ForeignKey(
                        name: "fk_despesas_arquivos_comprovante_arquivo_id",
                        column: x => x.comprovante_arquivo_id,
                        principalTable: "arquivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_despesas_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_despesas_fornecedores_fornecedor_id",
                        column: x => x.fornecedor_id,
                        principalTable: "fornecedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_despesas_itens_da_festa_item_da_festa_id",
                        column: x => x.item_da_festa_id,
                        principalTable: "itens_da_festa",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "itens_de_cobranca",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plano_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    descricao = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    numero_de_parcelas = table.Column<int>(type: "integer", nullable: false),
                    dia_de_vencimento = table.Column<int>(type: "integer", nullable: false),
                    primeiro_mes = table.Column<DateOnly>(type: "date", nullable: false),
                    encerrado_em = table.Column<DateOnly>(type: "date", nullable: true),
                    origem_da_decisao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    opcional = table.Column<bool>(type: "boolean", nullable: false),
                    limite_por_formando = table.Column<int>(type: "integer", nullable: true),
                    pedidos_ate_dia = table.Column<DateOnly>(type: "date", nullable: true),
                    estoque = table.Column<int>(type: "integer", nullable: true),
                    reservados = table.Column<int>(type: "integer", nullable: false),
                    abertura_de_vendas = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modo_de_venda = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    preco_publico_em_centavos = table.Column<long>(type: "bigint", nullable: true),
                    item_da_festa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    grupo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    convites_da_festa = table.Column<int>(type: "integer", nullable: false),
                    convites_da_colacao = table.Column<int>(type: "integer", nullable: false),
                    ultimo_vencimento = table.Column<DateOnly>(type: "date", nullable: true),
                    cancelavel_ate = table.Column<DateOnly>(type: "date", nullable: true),
                    alvo_do_rateio = table.Column<Guid[]>(type: "uuid[]", nullable: false, defaultValueSql: "'{}'::uuid[]"),
                    vinculo_do_lancamento = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_itens_de_cobranca", x => x.id);
                    table.CheckConstraint("ck_itens_de_cobranca_beneficios", "convites_da_festa >= 0 AND convites_da_colacao >= 0");
                    table.CheckConstraint("ck_itens_de_cobranca_estoque", "estoque IS NULL OR estoque >= 0");
                    table.CheckConstraint("ck_itens_de_cobranca_reservados", "reservados >= 0 AND (estoque IS NULL OR reservados <= estoque)");
                    table.ForeignKey(
                        name: "fk_itens_de_cobranca_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_itens_de_cobranca_itens_da_festa_item_da_festa_id",
                        column: x => x.item_da_festa_id,
                        principalTable: "itens_da_festa",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_itens_de_cobranca_planos_de_cobranca_plano_id",
                        column: x => x.plano_id,
                        principalTable: "planos_de_cobranca",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_itens_de_cobranca_vinculos_vinculo_do_lancamento",
                        column: x => x.vinculo_do_lancamento,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "propostas_do_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_da_festa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    o_que_inclui = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_propostas_do_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_propostas_do_item_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_propostas_do_item_itens_da_festa_item_da_festa_id",
                        column: x => x.item_da_festa_id,
                        principalTable: "itens_da_festa",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "compras_de_convite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_de_cobranca_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantidade = table.Column<int>(type: "integer", nullable: false),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    meio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nome_do_comprador = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cpf = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    convidados = table.Column<string>(type: "text", nullable: true),
                    chave_de_idempotencia = table.Column<Guid>(type: "uuid", nullable: false),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    paga_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    valor_pago_em_centavos = table.Column<long>(type: "bigint", nullable: true),
                    cpf_do_pagador = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    versao_do_link = table.Column<int>(type: "integer", nullable: false),
                    outra_receita_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dados_apagados_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    convites_cancelados = table.Column<int>(type: "integer", nullable: false),
                    valor_estornado_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    valor_a_devolver_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    devolvida_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    comprovante_da_devolucao_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cpf_hmac = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compras_de_convite", x => x.id);
                    table.CheckConstraint("ck_compras_de_convite_cancelados", "convites_cancelados BETWEEN 0 AND quantidade");
                    table.CheckConstraint("ck_compras_de_convite_quantidade", "quantidade > 0");
                    table.CheckConstraint("ck_compras_de_convite_valor", "valor_em_centavos >= 0");
                    table.ForeignKey(
                        name: "fk_compras_de_convite_arquivos_comprovante_da_devolucao_id",
                        column: x => x.comprovante_da_devolucao_id,
                        principalTable: "arquivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_compras_de_convite_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_compras_de_convite_itens_de_cobranca_item_de_cobranca_id",
                        column: x => x.item_de_cobranca_id,
                        principalTable: "itens_de_cobranca",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_compras_de_convite_outras_receitas_outra_receita_id",
                        column: x => x.outra_receita_id,
                        principalTable: "outras_receitas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "escolhas_da_cesta",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_de_cobranca_id = table.Column<Guid>(type: "uuid", nullable: false),
                    observacao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_escolhas_da_cesta", x => x.id);
                    table.ForeignKey(
                        name: "fk_escolhas_da_cesta_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_escolhas_da_cesta_itens_de_cobranca_item_de_cobranca_id",
                        column: x => x.item_de_cobranca_id,
                        principalTable: "itens_de_cobranca",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_escolhas_da_cesta_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcelas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_de_cobranca_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    vencimento = table.Column<DateOnly>(type: "date", nullable: false),
                    valor_original_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valor_pago_em_centavos = table.Column<long>(type: "bigint", nullable: true),
                    pago_em = table.Column<DateOnly>(type: "date", nullable: true),
                    suspensa_ate = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcelas", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcelas_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_parcelas_itens_de_cobranca_item_de_cobranca_id",
                        column: x => x.item_de_cobranca_id,
                        principalTable: "itens_de_cobranca",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_parcelas_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pedidos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_de_cobranca_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantidade = table.Column<int>(type: "integer", nullable: false),
                    parcelas = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pedido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    cancelado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    observacao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedidos", x => x.id);
                    table.CheckConstraint("ck_pedidos_parcelas", "parcelas >= 1");
                    table.CheckConstraint("ck_pedidos_quantidade", "quantidade >= 1");
                    table.ForeignKey(
                        name: "fk_pedidos_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pedidos_itens_de_cobranca_item_de_cobranca_id",
                        column: x => x.item_de_cobranca_id,
                        principalTable: "itens_de_cobranca",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pedidos_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "votos_nas_propostas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_da_festa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "cobrancas_bancarias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    meio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    conta_no_provedor = table.Column<long>(type: "bigint", nullable: false),
                    parcela_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    compra_id = table.Column<Guid>(type: "uuid", nullable: true),
                    chave = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    acrescimo_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    receita_do_acrescimo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    id_externo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    copia_e_cola = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cobrancas_bancarias", x => x.id);
                    table.ForeignKey(
                        name: "fk_cobrancas_bancarias_compras_de_convite_compra_id",
                        column: x => x.compra_id,
                        principalTable: "compras_de_convite",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cobrancas_bancarias_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pedidos_de_cancelamento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    convite_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    motivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    pedido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_pedidos_de_cancelamento", x => x.id);
                    table.ForeignKey(
                        name: "fk_pedidos_de_cancelamento_compras_de_convite_compra_id",
                        column: x => x.compra_id,
                        principalTable: "compras_de_convite",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pedidos_de_cancelamento_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                    meio_escolhido = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
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
                name: "notificacoes_enviadas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcela_id = table.Column<Guid>(type: "uuid", nullable: true),
                    regra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_de_referencia = table.Column<DateOnly>(type: "date", nullable: false),
                    destinatario = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assunto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email_na_fila_id = table.Column<Guid>(type: "uuid", nullable: true),
                    erro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notificacoes_enviadas", x => x.id);
                    table.ForeignKey(
                        name: "fk_notificacoes_enviadas_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notificacoes_enviadas_parcelas_parcela_id",
                        column: x => x.parcela_id,
                        principalTable: "parcelas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notificacoes_enviadas_regras_de_notificacao_regra_id",
                        column: x => x.regra_id,
                        principalTable: "regras_de_notificacao",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notificacoes_enviadas_vinculos_vinculo_id",
                        column: x => x.vinculo_id,
                        principalTable: "vinculos_de_formatura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "convites_do_evento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    compra_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sequencial = table.Column<int>(type: "integer", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nome_do_convidado = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    tipo_do_documento = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    numero_do_documento = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    email_do_convidado = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    emitido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revogado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    motivo_da_revogacao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    liberado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_convites_do_evento", x => x.id);
                    table.CheckConstraint("ck_convites_do_evento_sequencial", "sequencial >= 0");
                    table.ForeignKey(
                        name: "fk_convites_do_evento_compras_de_convite_compra_id",
                        column: x => x.compra_id,
                        principalTable: "compras_de_convite",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_convites_do_evento_eventos_da_turma_evento_id",
                        column: x => x.evento_id,
                        principalTable: "eventos_da_turma",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_convites_do_evento_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_convites_do_evento_pedidos_pedido_id",
                        column: x => x.pedido_id,
                        principalTable: "pedidos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_convites_do_evento_vinculos_vinculo_id",
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

            migrationBuilder.CreateTable(
                name: "valores_a_devolver",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vinculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    item_de_cobranca_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valor_em_centavos = table.Column<long>(type: "bigint", nullable: false),
                    parcela_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cobranca_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolvido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolvido_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comprovante_arquivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    despesa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_valores_a_devolver", x => x.id);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_arquivos_comprovante_arquivo_id",
                        column: x => x.comprovante_arquivo_id,
                        principalTable: "arquivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_cobrancas_bancarias_cobranca_id",
                        column: x => x.cobranca_id,
                        principalTable: "cobrancas_bancarias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_despesas_despesa_id",
                        column: x => x.despesa_id,
                        principalTable: "despesas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_itens_de_cobranca_item_de_cobranca_id",
                        column: x => x.item_de_cobranca_id,
                        principalTable: "itens_de_cobranca",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_parcelas_parcela_id",
                        column: x => x.parcela_id,
                        principalTable: "parcelas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_pedidos_pedido_id",
                        column: x => x.pedido_id,
                        principalTable: "pedidos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_usuarios_resolvido_por_usuario_id",
                        column: x => x.resolvido_por_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_valores_a_devolver_vinculos_vinculo_id",
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
                    cobranca_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                        name: "fk_recebimentos_cobrancas_bancarias_cobranca_id",
                        column: x => x.cobranca_id,
                        principalTable: "cobrancas_bancarias",
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

            migrationBuilder.CreateTable(
                name: "check_ins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    convite_id = table.Column<Guid>(type: "uuid", nullable: false),
                    validado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    validado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    aparelho = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    desfeito_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    desfeito_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    formatura_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_ins", x => x.id);
                    table.ForeignKey(
                        name: "fk_check_ins_convites_do_evento_convite_id",
                        column: x => x.convite_id,
                        principalTable: "convites_do_evento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_check_ins_formaturas_formatura_id",
                        column: x => x.formatura_id,
                        principalTable: "formaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_aceites_de_convite_convite_id",
                table: "aceites_de_convite",
                column: "convite_id");

            migrationBuilder.CreateIndex(
                name: "ix_aceites_de_convite_usuario_id",
                table: "aceites_de_convite",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_adesoes_cpf_hmac",
                table: "adesoes",
                column: "cpf_hmac");

            migrationBuilder.CreateIndex(
                name: "ix_adesoes_formatura_id",
                table: "adesoes",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_adesoes_termo_id",
                table: "adesoes",
                column: "termo_id");

            migrationBuilder.CreateIndex(
                name: "ix_adesoes_vinculo_id_termo_id",
                table: "adesoes",
                columns: new[] { "vinculo_id", "termo_id" },
                unique: true);

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
                name: "ix_arquivos_chave",
                table: "arquivos",
                column: "chave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_arquivos_enviado_por_id_categoria_criado_em",
                table: "arquivos",
                columns: new[] { "enviado_por_id", "categoria", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_formatura_id",
                table: "assinaturas",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_pendente_por_formatura",
                table: "assinaturas",
                column: "formatura_id",
                unique: true,
                filter: "status = 'Pendente'");

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_plano_do_proximo_ciclo_id",
                table: "assinaturas",
                column: "plano_do_proximo_ciclo_id");

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_plano_id",
                table: "assinaturas",
                column: "plano_id");

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_status_vigente_ate",
                table: "assinaturas",
                columns: new[] { "status", "vigente_ate" });

            migrationBuilder.CreateIndex(
                name: "ix_avisos_formatura_id",
                table: "avisos",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_check_ins_convite_id_validado_em",
                table: "check_ins",
                columns: new[] { "convite_id", "validado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_check_ins_formatura_id",
                table: "check_ins",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ux_check_ins_ativo",
                table: "check_ins",
                column: "convite_id",
                unique: true,
                filter: "desfeito_em IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_bancarias_compra_id",
                table: "cobrancas_bancarias",
                column: "compra_id");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_bancarias_formatura_id",
                table: "cobrancas_bancarias",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_bancarias_formatura_id_chave",
                table: "cobrancas_bancarias",
                columns: new[] { "formatura_id", "chave" },
                unique: true,
                filter: "status IN ('Emitindo', 'Emitida')");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_bancarias_id_externo",
                table: "cobrancas_bancarias",
                column: "id_externo");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_bancarias_status_criado_em",
                table: "cobrancas_bancarias",
                columns: new[] { "status", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_da_assinatura_assinatura_id",
                table: "cobrancas_da_assinatura",
                column: "assinatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_da_assinatura_id_do_pagamento",
                table: "cobrancas_da_assinatura",
                column: "id_do_pagamento",
                unique: true,
                filter: "id_do_pagamento IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_da_assinatura_plano_id",
                table: "cobrancas_da_assinatura",
                column: "plano_id");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_da_assinatura_situacao_criado_em",
                table: "cobrancas_da_assinatura",
                columns: new[] { "situacao", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_compras_de_convite_chave_de_idempotencia",
                table: "compras_de_convite",
                column: "chave_de_idempotencia",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_compras_de_convite_comprovante_da_devolucao_id",
                table: "compras_de_convite",
                column: "comprovante_da_devolucao_id");

            migrationBuilder.CreateIndex(
                name: "ix_compras_de_convite_cpf_hmac_item_de_cobranca_id",
                table: "compras_de_convite",
                columns: new[] { "cpf_hmac", "item_de_cobranca_id" });

            migrationBuilder.CreateIndex(
                name: "ix_compras_de_convite_formatura_id",
                table: "compras_de_convite",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_compras_de_convite_formatura_id_email",
                table: "compras_de_convite",
                columns: new[] { "formatura_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_compras_de_convite_item_de_cobranca_id",
                table: "compras_de_convite",
                column: "item_de_cobranca_id");

            migrationBuilder.CreateIndex(
                name: "ix_compras_de_convite_outra_receita_id",
                table: "compras_de_convite",
                column: "outra_receita_id");

            migrationBuilder.CreateIndex(
                name: "ix_compras_de_convite_status_expira_em",
                table: "compras_de_convite",
                columns: new[] { "status", "expira_em" },
                filter: "status = 'Pendente'");

            migrationBuilder.CreateIndex(
                name: "ix_consentimentos_documento_legal_id",
                table: "consentimentos",
                column: "documento_legal_id");

            migrationBuilder.CreateIndex(
                name: "ix_consentimentos_usuario_id_aceito_em",
                table: "consentimentos",
                columns: new[] { "usuario_id", "aceito_em" });

            migrationBuilder.CreateIndex(
                name: "ix_consentimentos_de_marketing_usuario_id_registrado_em",
                table: "consentimentos_de_marketing",
                columns: new[] { "usuario_id", "registrado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_contas_de_recebimento_conferida_por_usuario_id",
                table: "contas_de_recebimento",
                column: "conferida_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_contas_de_recebimento_formatura_id",
                table: "contas_de_recebimento",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_contas_de_recebimento_uma_por_formatura",
                table: "contas_de_recebimento",
                column: "formatura_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_convites_criado_por_usuario_id",
                table: "convites",
                column: "criado_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_convites_formatura_id",
                table: "convites",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_convites_token_hash",
                table: "convites",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_convites_do_evento_codigo",
                table: "convites_do_evento",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_convites_do_evento_formatura_id",
                table: "convites_do_evento",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_convites_do_evento_formatura_id_evento_id",
                table: "convites_do_evento",
                columns: new[] { "formatura_id", "evento_id" });

            migrationBuilder.CreateIndex(
                name: "ix_convites_do_evento_pedido_id",
                table: "convites_do_evento",
                column: "pedido_id");

            migrationBuilder.CreateIndex(
                name: "ix_convites_do_evento_vinculo_id",
                table: "convites_do_evento",
                column: "vinculo_id");

            migrationBuilder.CreateIndex(
                name: "ux_convites_do_evento_posicao_da_compra",
                table: "convites_do_evento",
                columns: new[] { "compra_id", "sequencial" },
                unique: true,
                filter: "compra_id IS NOT NULL AND revogado_em IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_convites_do_evento_posicao_valida",
                table: "convites_do_evento",
                columns: new[] { "evento_id", "vinculo_id", "pedido_id", "sequencial" },
                unique: true,
                filter: "vinculo_id IS NOT NULL AND revogado_em IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_correcoes_de_perfil_autor_usuario_id",
                table: "correcoes_de_perfil",
                column: "autor_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_correcoes_de_perfil_perfil_id",
                table: "correcoes_de_perfil",
                column: "perfil_id");

            migrationBuilder.CreateIndex(
                name: "ix_credenciais_de_provedor_cadastrada_por_usuario_id",
                table: "credenciais_de_provedor",
                column: "cadastrada_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_credenciais_de_provedor_expira_em",
                table: "credenciais_de_provedor",
                column: "expira_em");

            migrationBuilder.CreateIndex(
                name: "ix_credenciais_de_provedor_formatura_id",
                table: "credenciais_de_provedor",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_credenciais_de_provedor_uma_por_formatura",
                table: "credenciais_de_provedor",
                column: "formatura_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_despesas_comprovante_arquivo_id",
                table: "despesas",
                column: "comprovante_arquivo_id");

            migrationBuilder.CreateIndex(
                name: "ix_despesas_formatura_id",
                table: "despesas",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_despesas_formatura_id_item_da_festa_id",
                table: "despesas",
                columns: new[] { "formatura_id", "item_da_festa_id" });

            migrationBuilder.CreateIndex(
                name: "ix_despesas_formatura_id_lancamento_id",
                table: "despesas",
                columns: new[] { "formatura_id", "lancamento_id" });

            migrationBuilder.CreateIndex(
                name: "ix_despesas_formatura_id_status_competencia",
                table: "despesas",
                columns: new[] { "formatura_id", "status", "competencia" });

            migrationBuilder.CreateIndex(
                name: "ix_despesas_formatura_id_vencimento",
                table: "despesas",
                columns: new[] { "formatura_id", "vencimento" });

            migrationBuilder.CreateIndex(
                name: "ix_despesas_fornecedor_id",
                table: "despesas",
                column: "fornecedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_despesas_item_da_festa_id",
                table: "despesas",
                column: "item_da_festa_id");

            migrationBuilder.CreateIndex(
                name: "ix_despesas_lancamento_unico",
                table: "despesas",
                columns: new[] { "formatura_id", "fornecedor_id", "descricao", "vencimento" },
                unique: true,
                filter: "status <> 'Cancelada'")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_documentos_arquivo_id",
                table: "documentos",
                column: "arquivo_id");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_formatura_id",
                table: "documentos",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_legais_tipo_versao",
                table: "documentos_legais",
                columns: new[] { "tipo", "versao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documentos_legais_tipo_vigente_desde",
                table: "documentos_legais",
                columns: new[] { "tipo", "vigente_desde" });

            migrationBuilder.CreateIndex(
                name: "ix_emails_fila_status_proxima_tentativa_em_prioridade",
                table: "emails_fila",
                columns: new[] { "status", "proxima_tentativa_em", "prioridade" },
                filter: "status = 0");

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

            migrationBuilder.CreateIndex(
                name: "ix_escolhas_da_cesta_formatura_id",
                table: "escolhas_da_cesta",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_escolhas_da_cesta_item_de_cobranca_id",
                table: "escolhas_da_cesta",
                column: "item_de_cobranca_id");

            migrationBuilder.CreateIndex(
                name: "ix_escolhas_da_cesta_vinculo_id_item_de_cobranca_id",
                table: "escolhas_da_cesta",
                columns: new[] { "vinculo_id", "item_de_cobranca_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_eventos_ocorrido_em",
                table: "eventos",
                column: "ocorrido_em")
                .Annotation("Npgsql:IndexInclude", new[] { "nome", "formatura_id", "usuario_id" });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_trilha_da_formatura",
                table: "eventos",
                columns: new[] { "formatura_id", "ocorrido_em" },
                filter: "formatura_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_usuario_id_ocorrido_em",
                table: "eventos",
                columns: new[] { "usuario_id", "ocorrido_em" });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_da_turma_formatura_id",
                table: "eventos_da_turma",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_da_turma_formatura_id_data",
                table: "eventos_da_turma",
                columns: new[] { "formatura_id", "data" });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_da_turma_formatura_id_tipo",
                table: "eventos_da_turma",
                columns: new[] { "formatura_id", "tipo" },
                unique: true,
                filter: "tipo IN ('Colacao', 'Festa')");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_de_cobranca_assinatura_id",
                table: "eventos_de_cobranca",
                column: "assinatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_de_cobranca_id_externo",
                table: "eventos_de_cobranca",
                column: "id_externo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fornecedores_formatura_id",
                table: "fornecedores",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_fornecedores_formatura_id_nome",
                table: "fornecedores",
                columns: new[] { "formatura_id", "nome" },
                unique: true);

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
                name: "ix_itens_da_festa_documento_id",
                table: "itens_da_festa",
                column: "documento_id");

            migrationBuilder.CreateIndex(
                name: "ix_itens_da_festa_formatura_id",
                table: "itens_da_festa",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_itens_da_festa_formatura_id_ordem",
                table: "itens_da_festa",
                columns: new[] { "formatura_id", "ordem" });

            migrationBuilder.CreateIndex(
                name: "ix_itens_de_cobranca_formatura_id",
                table: "itens_de_cobranca",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_itens_de_cobranca_item_da_festa",
                table: "itens_de_cobranca",
                column: "item_da_festa_id",
                unique: true,
                filter: "item_da_festa_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_itens_de_cobranca_plano_id",
                table: "itens_de_cobranca",
                column: "plano_id");

            migrationBuilder.CreateIndex(
                name: "ix_itens_de_cobranca_vinculo_do_lancamento",
                table: "itens_de_cobranca",
                column: "vinculo_do_lancamento");

            migrationBuilder.CreateIndex(
                name: "ix_mesas_formatura_id",
                table: "mesas",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_mesas_vinculo_id",
                table: "mesas",
                column: "vinculo_id");

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_enviadas_formatura_id",
                table: "notificacoes_enviadas",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_enviadas_formatura_id_data_de_referencia",
                table: "notificacoes_enviadas",
                columns: new[] { "formatura_id", "data_de_referencia" });

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_enviadas_formatura_id_status",
                table: "notificacoes_enviadas",
                columns: new[] { "formatura_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_enviadas_idempotencia",
                table: "notificacoes_enviadas",
                columns: new[] { "formatura_id", "parcela_id", "regra_id", "data_de_referencia", "destinatario" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_enviadas_parcela_id",
                table: "notificacoes_enviadas",
                column: "parcela_id");

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_enviadas_regra_id",
                table: "notificacoes_enviadas",
                column: "regra_id");

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_enviadas_vinculo_id",
                table: "notificacoes_enviadas",
                column: "vinculo_id");

            migrationBuilder.CreateIndex(
                name: "ix_outras_receitas_documento_id",
                table: "outras_receitas",
                column: "documento_id");

            migrationBuilder.CreateIndex(
                name: "ix_outras_receitas_estorno_de_id",
                table: "outras_receitas",
                column: "estorno_de_id");

            migrationBuilder.CreateIndex(
                name: "ix_outras_receitas_formatura_id",
                table: "outras_receitas",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_outras_receitas_formatura_id_status_data",
                table: "outras_receitas",
                columns: new[] { "formatura_id", "status", "data" });

            migrationBuilder.CreateIndex(
                name: "ix_outras_receitas_lancamento_unico",
                table: "outras_receitas",
                columns: new[] { "formatura_id", "descricao", "origem", "data" },
                unique: true,
                filter: "status <> 'Cancelada'")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_parcelas_formatura_id",
                table: "parcelas",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_parcelas_formatura_id_status_vencimento",
                table: "parcelas",
                columns: new[] { "formatura_id", "status", "vencimento" });

            migrationBuilder.CreateIndex(
                name: "ix_parcelas_item_de_cobranca_id_vencimento",
                table: "parcelas",
                columns: new[] { "item_de_cobranca_id", "vencimento" });

            migrationBuilder.CreateIndex(
                name: "ix_parcelas_vinculo_id_item_de_cobranca_id_numero",
                table: "parcelas",
                columns: new[] { "vinculo_id", "item_de_cobranca_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_formatura_id",
                table: "pedidos",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_item_de_cobranca_id_status",
                table: "pedidos",
                columns: new[] { "item_de_cobranca_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_vinculo_id_item_de_cobranca_id",
                table: "pedidos",
                columns: new[] { "vinculo_id", "item_de_cobranca_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_de_cancelamento_formatura_id",
                table: "pedidos_de_cancelamento",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_de_cancelamento_formatura_id_status",
                table: "pedidos_de_cancelamento",
                columns: new[] { "formatura_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_de_cancelamento_um_aberto",
                table: "pedidos_de_cancelamento",
                column: "compra_id",
                unique: true,
                filter: "status = 'Aberto'");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "perfis",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_perfis_claims_role_id",
                table: "perfis_claims",
                column: "role_id");

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

            migrationBuilder.CreateIndex(
                name: "ix_planos_codigo",
                table: "planos",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_planos_de_cobranca_formatura_id",
                table: "planos_de_cobranca",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_planos_de_cobranca_vigente_por_formatura",
                table: "planos_de_cobranca",
                column: "formatura_id",
                unique: true,
                filter: "status = 'Vigente'");

            migrationBuilder.CreateIndex(
                name: "ix_preferencias_de_notificacao_formatura_id",
                table: "preferencias_de_notificacao",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_preferencias_de_notificacao_vinculo_id_tipo",
                table: "preferencias_de_notificacao",
                columns: new[] { "vinculo_id", "tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_propostas_do_item_formatura_id",
                table: "propostas_do_item",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_propostas_do_item_item_da_festa_id",
                table: "propostas_do_item",
                column: "item_da_festa_id");

            migrationBuilder.CreateIndex(
                name: "ix_recebimentos_baixado_por_usuario_id",
                table: "recebimentos",
                column: "baixado_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_recebimentos_cobranca_id",
                table: "recebimentos",
                column: "cobranca_id");

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
                column: "parcela_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_expira_em",
                table: "refresh_tokens",
                column: "expira_em");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_revogado_em",
                table: "refresh_tokens",
                column: "revogado_em");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_usuario_id_revogado_em",
                table: "refresh_tokens",
                columns: new[] { "usuario_id", "revogado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_regras_de_notificacao_formatura_id",
                table: "regras_de_notificacao",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_regras_de_notificacao_formatura_id_gatilho_dias_de_deslocam",
                table: "regras_de_notificacao",
                columns: new[] { "formatura_id", "gatilho", "dias_de_deslocamento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_saloes_formatura_id",
                table: "saloes",
                column: "formatura_id",
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_privacidade_pendentes",
                table: "solicitacoes_de_privacidade",
                column: "prazo_em",
                filter: "status = 'Pendente'");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_privacidade_titular_usuario_id_criado_em",
                table: "solicitacoes_de_privacidade",
                columns: new[] { "titular_usuario_id", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_relatorio_arquivo_id",
                table: "solicitacoes_de_relatorio",
                column: "arquivo_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_relatorio_expira_em",
                table: "solicitacoes_de_relatorio",
                column: "expira_em",
                filter: "arquivo_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_relatorio_formatura_id",
                table: "solicitacoes_de_relatorio",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_relatorio_solicitada_por_usuario_id",
                table: "solicitacoes_de_relatorio",
                column: "solicitada_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_relatorio_status_criado_em",
                table: "solicitacoes_de_relatorio",
                columns: new[] { "status", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_de_relatorio_tipo_status_de_ate",
                table: "solicitacoes_de_relatorio",
                columns: new[] { "tipo", "status", "de", "ate" });

            migrationBuilder.CreateIndex(
                name: "ix_termos_de_adesao_formatura_id",
                table: "termos_de_adesao",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_termos_de_adesao_formatura_id_versao",
                table: "termos_de_adesao",
                columns: new[] { "formatura_id", "versao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "usuarios",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_nome",
                table: "usuarios",
                column: "nome");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "usuarios",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_claims_user_id",
                table: "usuarios_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_logins_user_id",
                table: "usuarios_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_perfis_role_id",
                table: "usuarios_perfis",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_cobranca_id",
                table: "valores_a_devolver",
                column: "cobranca_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_comprovante_arquivo_id",
                table: "valores_a_devolver",
                column: "comprovante_arquivo_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_criado_em",
                table: "valores_a_devolver",
                column: "criado_em",
                filter: "status = 'ADevolver'");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_despesa_id",
                table: "valores_a_devolver",
                column: "despesa_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_formatura_id",
                table: "valores_a_devolver",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_item_de_cobranca_id",
                table: "valores_a_devolver",
                column: "item_de_cobranca_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_parcela_id",
                table: "valores_a_devolver",
                column: "parcela_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_pedido_id",
                table: "valores_a_devolver",
                column: "pedido_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_resolvido_por_usuario_id",
                table: "valores_a_devolver",
                column: "resolvido_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_a_devolver_vinculo_id",
                table: "valores_a_devolver",
                column: "vinculo_id");

            migrationBuilder.CreateIndex(
                name: "ix_vinculos_de_formatura_formatura_id",
                table: "vinculos_de_formatura",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_vinculos_de_formatura_usuario_id_formatura_id",
                table: "vinculos_de_formatura",
                columns: new[] { "usuario_id", "formatura_id" },
                unique: true);

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

            migrationBuilder.Sql(
                """
                CREATE FUNCTION recusar_alteracao() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'A tabela % é append-only: publique uma versão nova ou grave uma linha nova.', TG_TABLE_NAME;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER documentos_legais_append_only BEFORE UPDATE OR DELETE ON documentos_legais
                    FOR EACH ROW EXECUTE FUNCTION recusar_alteracao();
                CREATE TRIGGER consentimentos_append_only BEFORE UPDATE OR DELETE ON consentimentos
                    FOR EACH ROW EXECUTE FUNCTION recusar_alteracao();
                CREATE TRIGGER consentimentos_de_marketing_append_only BEFORE UPDATE OR DELETE ON consentimentos_de_marketing
                    FOR EACH ROW EXECUTE FUNCTION recusar_alteracao();
                CREATE TRIGGER eventos_de_cobranca_append_only BEFORE UPDATE OR DELETE ON eventos_de_cobranca
                    FOR EACH ROW EXECUTE FUNCTION recusar_alteracao();
                CREATE TRIGGER termos_de_adesao_append_only BEFORE UPDATE OR DELETE ON termos_de_adesao
                    FOR EACH ROW EXECUTE FUNCTION recusar_alteracao();
                CREATE TRIGGER adesoes_append_only BEFORE UPDATE OR DELETE ON adesoes
                    FOR EACH ROW EXECUTE FUNCTION recusar_alteracao();
                CREATE TRIGGER aditivos_da_adesao_append_only BEFORE UPDATE OR DELETE ON aditivos_da_adesao
                    FOR EACH ROW EXECUTE FUNCTION recusar_alteracao();
                """
            );

            var vigencia = new DateTime(2026, 9, 11, 3, 0, 0, DateTimeKind.Utc);

            DocumentosLegais.Publicar(migrationBuilder, new Guid("0199386a-0000-7000-8000-000000000001"), "TermosDeUso", "1", vigencia);
            DocumentosLegais.Publicar(migrationBuilder, new Guid("0199386a-0000-7000-8000-000000000002"), "PoliticaDePrivacidade", "1", vigencia);

            migrationBuilder.Sql(
                """
                INSERT INTO planos (id, codigo, nome, preco_em_centavos, ciclo, limite_de_formandos, recomendado, ativo, criado_em, atualizado_em, descricao, modulos, preco_cheio_em_centavos) VALUES
                ('0199407e-0000-7000-8000-000000000001', 'essencial', 'Essencial', 2990, 'Mensal', 50, false, true, '2026-09-12 03:00:00+00', '2026-10-05 03:00:00+00', 'Cobrar a turma, pagar os fornecedores e fechar o caixa.', '{membros,termo,cobrancas,pix,despesas,caixa,festa}', NULL),
                ('0199407e-0000-7000-8000-000000000005', 'essencial-anual', 'Essencial', 28700, 'Anual', 50, false, true, '2026-09-15 03:00:00+00', '2026-10-05 03:00:00+00', 'Cobrar a turma, pagar os fornecedores e fechar o caixa.', '{membros,termo,cobrancas,pix,despesas,caixa,festa}', 35880),
                ('0199407e-0000-7000-8000-000000000008', 'premium', 'Premium', 4990, 'Mensal', 400, true, true, '2026-09-21 03:00:00+00', '2026-10-05 03:00:00+00', 'Turma grande, com mural, régua de cobrança e prestação de contas.', '{membros,termo,cobrancas,pix,despesas,caixa,festa,mesas,mural,avisos,relatorios,auditoria}', NULL),
                ('0199407e-0000-7000-8000-000000000009', 'premium-anual', 'Premium', 47900, 'Anual', 400, true, true, '2026-09-21 03:00:00+00', '2026-10-05 03:00:00+00', 'Turma grande, com mural, régua de cobrança e prestação de contas.', '{membros,termo,cobrancas,pix,despesas,caixa,festa,mesas,mural,avisos,relatorios,auditoria}', 59880);
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION recusar_alteracao() CASCADE;");

            migrationBuilder.DropTable(
                name: "aceites_de_convite");

            migrationBuilder.DropTable(
                name: "aditivos_da_adesao");

            migrationBuilder.DropTable(
                name: "avisos");

            migrationBuilder.DropTable(
                name: "check_ins");

            migrationBuilder.DropTable(
                name: "cobrancas_da_assinatura");

            migrationBuilder.DropTable(
                name: "consentimentos");

            migrationBuilder.DropTable(
                name: "consentimentos_de_marketing");

            migrationBuilder.DropTable(
                name: "contas_de_recebimento");

            migrationBuilder.DropTable(
                name: "correcoes_de_perfil");

            migrationBuilder.DropTable(
                name: "credenciais_de_provedor");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "emails_fila");

            migrationBuilder.DropTable(
                name: "envios_de_marketing");

            migrationBuilder.DropTable(
                name: "escolhas_da_cesta");

            migrationBuilder.DropTable(
                name: "eventos");

            migrationBuilder.DropTable(
                name: "eventos_de_cobranca");

            migrationBuilder.DropTable(
                name: "mesas");

            migrationBuilder.DropTable(
                name: "notificacoes_enviadas");

            migrationBuilder.DropTable(
                name: "pedidos_de_cancelamento");

            migrationBuilder.DropTable(
                name: "perfis_claims");

            migrationBuilder.DropTable(
                name: "preferencias_de_notificacao");

            migrationBuilder.DropTable(
                name: "recebimentos");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "resumos_de_termo");

            migrationBuilder.DropTable(
                name: "saloes");

            migrationBuilder.DropTable(
                name: "solicitacoes_de_cancelamento");

            migrationBuilder.DropTable(
                name: "solicitacoes_de_privacidade");

            migrationBuilder.DropTable(
                name: "solicitacoes_de_relatorio");

            migrationBuilder.DropTable(
                name: "usuarios_claims");

            migrationBuilder.DropTable(
                name: "usuarios_logins");

            migrationBuilder.DropTable(
                name: "usuarios_perfis");

            migrationBuilder.DropTable(
                name: "usuarios_tokens");

            migrationBuilder.DropTable(
                name: "valores_a_devolver");

            migrationBuilder.DropTable(
                name: "votos_nas_propostas");

            migrationBuilder.DropTable(
                name: "convites");

            migrationBuilder.DropTable(
                name: "adesoes");

            migrationBuilder.DropTable(
                name: "convites_do_evento");

            migrationBuilder.DropTable(
                name: "assinaturas");

            migrationBuilder.DropTable(
                name: "documentos_legais");

            migrationBuilder.DropTable(
                name: "perfis_de_formandos");

            migrationBuilder.DropTable(
                name: "regras_de_notificacao");

            migrationBuilder.DropTable(
                name: "informes_de_pagamento");

            migrationBuilder.DropTable(
                name: "perfis");

            migrationBuilder.DropTable(
                name: "cobrancas_bancarias");

            migrationBuilder.DropTable(
                name: "despesas");

            migrationBuilder.DropTable(
                name: "propostas_do_item");

            migrationBuilder.DropTable(
                name: "termos_de_adesao");

            migrationBuilder.DropTable(
                name: "eventos_da_turma");

            migrationBuilder.DropTable(
                name: "pedidos");

            migrationBuilder.DropTable(
                name: "planos");

            migrationBuilder.DropTable(
                name: "parcelas");

            migrationBuilder.DropTable(
                name: "compras_de_convite");

            migrationBuilder.DropTable(
                name: "fornecedores");

            migrationBuilder.DropTable(
                name: "itens_de_cobranca");

            migrationBuilder.DropTable(
                name: "outras_receitas");

            migrationBuilder.DropTable(
                name: "itens_da_festa");

            migrationBuilder.DropTable(
                name: "planos_de_cobranca");

            migrationBuilder.DropTable(
                name: "vinculos_de_formatura");

            migrationBuilder.DropTable(
                name: "documentos");

            migrationBuilder.DropTable(
                name: "arquivos");

            migrationBuilder.DropTable(
                name: "formaturas");

            migrationBuilder.DropTable(
                name: "usuarios");
        }
    }
}
