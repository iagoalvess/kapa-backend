using System;
using Backend.Data.Seed;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <summary>
    /// Esquema inteiro, os planos e a versão 1 dos documentos legais.
    /// </summary>
    /// <remarks>
    /// Nasceu da consolidação das migrations de desenvolvimento, em 13/09/2026, antes de haver banco
    /// em produção. O que o EF não gera sozinho está no fim de <see cref="Up"/>:
    /// <list type="bullet">
    ///   <item>
    ///     <c>recusar_alteracao()</c> e os gatilhos que tornam <c>documentos_legais</c>,
    ///     <c>consentimentos</c> e <c>eventos_de_cobranca</c> append-only no banco, e não só no
    ///     código — nem um <c>UPDATE</c> escrito à mão no psql altera um texto publicado, uma prova
    ///     de consentimento ou um evento de cobrança;
    ///   </item>
    ///   <item>
    ///     planos e documentos legais, que entram aqui, e não no <c>SeedInicial</c>: o seed é
    ///     desligado em produção. A vigência é meia-noite de Brasília (03:00 UTC) — meia-noite UTC
    ///     apareceria na tela como o dia anterior. Os preços são placeholders até o produto fixar a
    ///     tabela.
    ///   </item>
    /// </list>
    /// </remarks>
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    ocorrido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    rota = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
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
                    previsao_de_colacao = table.Column<DateOnly>(type: "date", nullable: true),
                    previsao_da_festa = table.Column<DateOnly>(type: "date", nullable: true),
                    quantidade_estimada_de_formandos = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    criado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ativada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    encerrada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    preco_em_centavos = table.Column<long>(type: "bigint", nullable: false),
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
                    texto = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
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
                    vigente_ate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ultimo_aviso_de_vencimento = table.Column<int>(type: "integer", nullable: true),
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
                name: "convites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                name: "ix_aceites_de_convite_convite_id",
                table: "aceites_de_convite",
                column: "convite_id");

            migrationBuilder.CreateIndex(
                name: "ix_aceites_de_convite_usuario_id",
                table: "aceites_de_convite",
                column: "usuario_id");

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
                name: "ix_consentimentos_documento_legal_id",
                table: "consentimentos",
                column: "documento_legal_id");

            migrationBuilder.CreateIndex(
                name: "ix_consentimentos_usuario_id_aceito_em",
                table: "consentimentos",
                columns: new[] { "usuario_id", "aceito_em" });

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
                name: "ix_correcoes_de_perfil_autor_usuario_id",
                table: "correcoes_de_perfil",
                column: "autor_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_correcoes_de_perfil_perfil_id",
                table: "correcoes_de_perfil",
                column: "perfil_id");

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
                name: "ix_eventos_ocorrido_em_nome",
                table: "eventos",
                columns: new[] { "ocorrido_em", "nome" });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_usuario_id_ocorrido_em",
                table: "eventos",
                columns: new[] { "usuario_id", "ocorrido_em" });

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
                name: "ix_formaturas_rascunho_por_criador",
                table: "formaturas",
                column: "criado_por_usuario_id",
                unique: true,
                filter: "status = 'Rascunho'");

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
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_usuario_id_revogado_em",
                table: "refresh_tokens",
                columns: new[] { "usuario_id", "revogado_em" });

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
                name: "ix_vinculos_de_formatura_formatura_id",
                table: "vinculos_de_formatura",
                column: "formatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_vinculos_de_formatura_usuario_id_formatura_id",
                table: "vinculos_de_formatura",
                columns: new[] { "usuario_id", "formatura_id" },
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

                CREATE TRIGGER eventos_de_cobranca_append_only BEFORE UPDATE OR DELETE ON eventos_de_cobranca
                    FOR EACH ROW EXECUTE FUNCTION recusar_alteracao();
                """
            );

            var vigencia = new DateTime(2026, 9, 11, 3, 0, 0, DateTimeKind.Utc);

            DocumentosLegais.Publicar(migrationBuilder, new Guid("0199386a-0000-7000-8000-000000000001"), "TermosDeUso", "1", vigencia);
            DocumentosLegais.Publicar(migrationBuilder, new Guid("0199386a-0000-7000-8000-000000000002"), "PoliticaDePrivacidade", "1", vigencia);

            var publicadoEm = new DateTime(2026, 9, 12, 3, 0, 0, DateTimeKind.Utc);

            migrationBuilder.InsertData(
                table: "planos",
                columns: ["id", "codigo", "nome", "preco_em_centavos", "ciclo", "limite_de_formandos", "recomendado", "ativo", "criado_em", "atualizado_em"],
                values: new object[,]
                {
                    { new Guid("0199407e-0000-7000-8000-000000000001"), "essencial", "Essencial", 14990L, "Mensal", 60, false, true, publicadoEm, publicadoEm },
                    { new Guid("0199407e-0000-7000-8000-000000000002"), "completo", "Completo", 34990L, "Mensal", 150, true, true, publicadoEm, publicadoEm },
                    { new Guid("0199407e-0000-7000-8000-000000000003"), "ampliado", "Ampliado", 59990L, "Mensal", 400, false, true, publicadoEm, publicadoEm },
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION recusar_alteracao() CASCADE;");

            migrationBuilder.DropTable(
                name: "aceites_de_convite");

            migrationBuilder.DropTable(
                name: "assinaturas");

            migrationBuilder.DropTable(
                name: "avisos");

            migrationBuilder.DropTable(
                name: "consentimentos");

            migrationBuilder.DropTable(
                name: "correcoes_de_perfil");

            migrationBuilder.DropTable(
                name: "emails_fila");

            migrationBuilder.DropTable(
                name: "eventos");

            migrationBuilder.DropTable(
                name: "eventos_de_cobranca");

            migrationBuilder.DropTable(
                name: "perfis_claims");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "usuarios_claims");

            migrationBuilder.DropTable(
                name: "usuarios_logins");

            migrationBuilder.DropTable(
                name: "usuarios_perfis");

            migrationBuilder.DropTable(
                name: "usuarios_tokens");

            migrationBuilder.DropTable(
                name: "convites");

            migrationBuilder.DropTable(
                name: "planos");

            migrationBuilder.DropTable(
                name: "documentos_legais");

            migrationBuilder.DropTable(
                name: "perfis_de_formandos");

            migrationBuilder.DropTable(
                name: "perfis");

            migrationBuilder.DropTable(
                name: "arquivos");

            migrationBuilder.DropTable(
                name: "vinculos_de_formatura");

            migrationBuilder.DropTable(
                name: "formaturas");

            migrationBuilder.DropTable(
                name: "usuarios");
        }
    }
}
