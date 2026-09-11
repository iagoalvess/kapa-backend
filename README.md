# Backend — template .NET 10

Base para backends .NET: camadas, autenticação JWT com refresh token rotativo, painel
administrativo, worker de fundo, observabilidade e testes de integração contra banco real.

Feito para ser **copiado e renomeado**, não referenciado como biblioteca.

---

## Começar

```bash
git clone <este-repo> meu-projeto && cd meu-projeto
cp .env.example .env          # preencha JWT_CHAVE_SECRETA e ADMIN_SENHA

docker compose up --build     # banco + migrations + api + worker
```

API em `http://localhost:8080`, documentação em `http://localhost:8080/scalar`.

### Ou rodando pela IDE (o fluxo do dia a dia)

```bash
docker compose up postgres    # só o banco

dotnet tool restore
dotnet ef database update --project src/Backend.Data --startup-project src/Backend.Api
dotnet run --project src/Backend.Api
```

### Comandos que o CI roda

```bash
dotnet csharpier check .                        # formatação
dotnet build --warnaserror                      # compilação, aviso é erro
dotnet test tests/Backend.UnitTests             # rápido, sem I/O
dotnet test tests/Backend.IntegrationTests      # sobe Postgres em container — exige Docker
```

---

## Renomear para o seu projeto

O nome `Backend` aparece em namespaces, nomes de projeto e arquivos de solução.

```bash
# Linux/macOS/Git Bash — troque MeuProjeto pelo nome desejado
NOVO=MeuProjeto
grep -rl "Backend" --include="*.cs" --include="*.csproj" --include="*.sln" \
     --include="*.json" --include="*.yml" --include="Dockerfile" . \
  | xargs sed -i "s/Backend/$NOVO/g"

for f in $(find . -name "Backend.*" -not -path "*/obj/*" -not -path "*/bin/*"); do
  git mv "$f" "$(echo $f | sed "s/Backend/$NOVO/")"
done
```

Depois: apague `src/Backend.Data/Migrations/` e gere a migration inicial do seu domínio.

---

## O que já vem pronto

| | |
|---|---|
| **Autenticação** | Registro, login, refresh com rotação e detecção de reúso, logout. Refresh token em cookie `HttpOnly`, fora do alcance do JavaScript. ASP.NET Identity com bloqueio por tentativas e política de senha. |
| **Ciclo de conta** | Confirmação de e-mail, esqueci a senha, redefinição e troca de senha. Resposta constante contra enumeração, sessões derrubadas na troca e aviso por e-mail. |
| **Autorização** | Papéis + políticas nomeadas, com o administrador passando por qualquer política. |
| **Painel administrativo** | `GET /api/v1/admin/resumo` (contagens) e gestão completa de usuários, com travas contra ficar sem administrador. |
| **Erros** | `Result<T>` para falha prevista, exceção para o resto; tudo sai em `ProblemDetails` (RFC 9457) com `traceId`. |
| **Persistência** | EF Core + PostgreSQL, `snake_case`, UUIDv7, Unit of Work, mapeamento por `IEntityTypeConfiguration`. |
| **E-mail** | Fila em tabela + envio pelo worker com retentativa exponencial. SMTP (MailKit), que serve SES, SendGrid, Mailgun, Resend e Gmail sem trocar código. |
| **Arquivos** | Upload, download e remoção com metadados no banco e bytes no provedor. Disco local em dev, S3 em produção (ou MinIO/R2/B2). Lista de permissão de extensões, tipo de conteúdo derivado da extensão, cota por usuário e chave gerada pela aplicação. |
| **Eventos** | `[RegistrarEvento("nome")]` no controller → fila em memória → gravação em lote → job de retenção. |
| **Observabilidade** | `ILogger` com console JSON + OpenTelemetry (traces e métricas via OTLP) + health check. |
| **Borda** | Versionamento por URL, rate limiting nativo, CORS por configuração, OpenAPI + Scalar. |
| **Worker** | Três jobs prontos: envio de e-mail, limpeza de refresh tokens e retenção de eventos. |
| **Testes** | 162 testes (101 unitários + 61 de integração): xUnit v3 + Shouldly + NSubstitute; integração com API e Postgres reais via Testcontainers. |
| **Infra** | Dockerfile multi-alvo, docker compose, GitHub Actions, CSharpier, Central Package Management. |

---

## Endpoints

| Método | Rota | Acesso |
|---|---|---|
| `POST` | `/api/v1/auth/registrar` | anônimo |
| `POST` | `/api/v1/auth/login` | anônimo |
| `POST` | `/api/v1/auth/refresh` | anônimo |
| `POST` | `/api/v1/auth/logout` | anônimo |
| `POST` | `/api/v1/conta/esqueci-senha` | anônimo |
| `POST` | `/api/v1/conta/redefinir-senha` | anônimo |
| `POST` | `/api/v1/conta/confirmar-email` | anônimo |
| `POST` | `/api/v1/conta/reenviar-confirmacao` | anônimo |
| `POST` | `/api/v1/conta/alterar-senha` | autenticado |
| `GET` | `/api/v1/usuarios/eu` | autenticado |
| `PUT` | `/api/v1/usuarios/eu` | autenticado |
| `GET` | `/api/v1/usuarios` | administrador |
| `GET` | `/api/v1/usuarios/{id}` | administrador |
| `PUT` | `/api/v1/usuarios/{id}` | administrador |
| `PUT` | `/api/v1/usuarios/{id}/ativacao` | administrador |
| `PUT` | `/api/v1/usuarios/{id}/perfis` | administrador |
| `POST` | `/api/v1/arquivos` | autenticado (multipart) |
| `GET` | `/api/v1/arquivos` | autenticado (só os próprios; admin vê todos) |
| `GET` | `/api/v1/arquivos/{id}` | dono ou administrador |
| `GET` | `/api/v1/arquivos/{id}/conteudo` | dono ou administrador |
| `DELETE` | `/api/v1/arquivos/{id}` | dono ou administrador |
| `GET` | `/api/v1/admin/resumo` | administrador |
| `GET` | `/api/v1/admin/perfis` | administrador |
| `GET` | `/health` | anônimo |

---

## Configuração

Tudo por variável de ambiente (`Secao__Chave`) ou `appsettings.json`.

| Chave | Obrigatória | Padrão |
|---|---|---|
| `ConnectionStrings__Postgres` | sim | — |
| `Jwt__ChaveSecreta` | sim | — (mínimo 32 caracteres) |
| `Jwt__Emissor` / `Jwt__Audiencia` | sim | `backend-api` / `backend-clientes` |
| `Jwt__MinutosDeValidadeDoAccessToken` | não | `15` |
| `Jwt__DiasDeValidadeDoRefreshToken` | não | `7` |
| `Cors__Origens__0` | fora de dev | vazio = nenhuma origem liberada |
| `CookieDeSessao__Habilitado` | não | `true` — refresh token em cookie `HttpOnly`; `false` devolve no corpo |
| `CookieDeSessao__SameSite` | não | `Lax`; use `None` só com front e API em sites registráveis diferentes |
| `Rede__ProxiesConfiaveis__0` | atrás de proxy | vazio = `X-Forwarded-For` ignorado. IP ou CIDR (`10.0.0.0/8`) |
| `RateLimit__PadraoPorMinuto` | não | `120` |
| `RateLimit__AutenticacaoPorMinuto` | não | `10` |
| `Documentacao__Habilitada` | não | `false` (sempre ligada em dev) |
| `Aplicacao__Nome` | não | `Backend` — aparece nos e-mails |
| `Aplicacao__UrlDoFrontend` | sim, p/ e-mails | `http://localhost:3000` — base dos links de confirmação e redefinição |
| `Conta__ExigirEmailConfirmado` | não | `false`; ligue junto com o SMTP |
| `Conta__HorasDeValidadeDoLink` | não | `4` |
| `Smtp__Host` | não | vazio = e-mails só vão para o log |
| `Smtp__Porta` | não | `587`; use `465` para SSL implícito |
| `Smtp__Usuario` / `Smtp__Senha` | não | vazio = envia sem autenticar |
| `Smtp__RemetenteEmail` / `Smtp__RemetenteNome` | com SMTP | — |
| `Eventos__DiasDeRetencao` | não | `180` |
| `Armazenamento__Provedor` | não | `Local` (dev) — use `S3` em produção |
| `Armazenamento__CaminhoLocal` | provedor Local | `arquivos` |
| `Armazenamento__TamanhoMaximoEmMB` | não | `25` — teto de **cada** arquivo |
| `Armazenamento__CotaPorUsuarioEmMB` | não | `500` — espaço total por usuário; `0` desliga |
| `Armazenamento__MaximoDeArquivosPorUsuario` | não | `200` — quantidade por usuário; `0` desliga |
| `Armazenamento__ExtensoesPermitidas__0` | não | pdf, png, jpg, jpeg, webp, gif, csv, txt, xlsx, docx |
| `Armazenamento__S3__Bucket` / `__Regiao` | provedor S3 | — |
| `Armazenamento__S3__ServiceUrl` | não | vazio = AWS; preencha para MinIO, R2 ou B2 |
| `Seed__AoIniciar` | não | `false` |
| `Seed__AdminEmail` / `Seed__AdminSenha` | não | vazio = nenhum admin é criado |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | não | vazio = telemetria não exportada |

A aplicação **não sobe** sem `Jwt__ChaveSecreta` válida ou sem string de conexão. É proposital:
descobrir isso no boot é melhor que descobrir em produção.

---

## Documentação

| Documento | Para quê |
|---|---|
| [docs/arquitetura.md](docs/arquitetura.md) | Como o sistema é montado: camadas, erros, autenticação, e-mail, eventos, observabilidade. |
| [docs/padroes.md](docs/padroes.md) | Como usar cada padrão no código, com exemplos. Referência de consulta enquanto se escreve. |
| [docs/estrutura.md](docs/estrutura.md) | Onde colocar cada arquivo. Tem tabela de referência rápida. |
| [docs/nova-feature.md](docs/nova-feature.md) | Passo a passo para criar uma feature do zero. |
| [docs/operacao.md](docs/operacao.md) | Migrations, deploy, segredos, observabilidade. |
| [docs/decisoes.md](docs/decisoes.md) | As escolhas não óbvias, com o motivo e a condição que faria reabri-las. |

Começando no projeto: **arquitetura.md** para entender o desenho, **padroes.md** ao lado enquanto
escreve o primeiro código.
