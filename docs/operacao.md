# Operação

## Migrations

```bash
# Criar
dotnet ef migrations add NomeDescritivo --project src/Backend.Data --startup-project src/Backend.Api

# Aplicar (desenvolvimento)
dotnet ef database update --project src/Backend.Data --startup-project src/Backend.Api

# Desfazer a última, ainda não aplicada
dotnet ef migrations remove --project src/Backend.Data --startup-project src/Backend.Api
```

**Revise o arquivo gerado antes de commitar.** O EF infere `DROP COLUMN` a partir de um rename
de propriedade — o que apaga a coluna e os dados dela em vez de renomear. Renomeação é o caso em
que o gerado quase sempre está errado.

### Em produção

Migration **não** roda no boot da aplicação (ver `arquitetura.md`). São dois passos:

```bash
# 1. Aplicar o esquema
docker run --rm -e ConnectionStrings__Postgres="$CONEXAO" minha-imagem-migrations

# 2. Só então subir a nova versão da API
```

Sem rede para o banco a partir do CI, gere um script idempotente e entregue ao DBA:

```bash
dotnet ef migrations script --idempotent --output esquema.sql \
  --project src/Backend.Data --startup-project src/Backend.Api
```

### Migration compatível com a versão anterior

Enquanto o deploy roda, a versão antiga e a nova convivem. Uma migration que remove ou renomeia
coluna derruba a versão antiga que ainda está atendendo. Faça em duas entregas:

1. **Entrega 1** — adiciona a coluna nova (anulável), o código escreve nas duas.
2. **Entrega 2** — remove a antiga, depois que nenhuma instância a usa.

---

## Segredos

Nada de segredo em `appsettings.json` versionado.

```bash
# Desenvolvimento
dotnet user-secrets set "Jwt:ChaveSecreta" "$(openssl rand -base64 48)" --project src/Backend.Api

# Docker / produção: variável de ambiente, com "__" no lugar de ":"
export Jwt__ChaveSecreta="..."
export ConnectionStrings__Postgres="Host=...;Database=...;Username=...;Password=..."
```

Gere uma chave **por ambiente**. Chave compartilhada entre homologação e produção significa que
um token emitido em homologação vale em produção.

### Trocar a chave do JWT

Trocar invalida todos os access tokens em circulação. Os usuários seguem logados, porque o
refresh token não depende dela — na próxima renovação (no máximo 15 minutos) recebem um token
assinado com a chave nova. Troque fora do horário de pico e não precisa de mais nada.

---

## Primeiro administrador

Não existe usuário padrão embutido. Para criar o primeiro:

```bash
Seed__AoIniciar=true
Seed__AdminEmail=admin@empresa.com
Seed__AdminSenha=<senha forte>
```

Suba uma vez, confirme que autentica e **remova as três variáveis**. O seed é idempotente: se a
conta já existir, não faz nada.

A partir daí a promoção de administrador é pela API
(`PUT /api/v1/usuarios/{id}/perfis`), com as travas contra ficar sem administrador.

---

## Observabilidade

### Logs

JSON de uma linha no stdout fora de desenvolvimento. Não há arquivo de log e não há sink
configurado no aplicativo: quem coleta é o agente da plataforma (Loki via Promtail, agente do
Datadog, driver do CloudWatch). Aplicação que escreve direto no agregador trava quando o
agregador cai.

### Traces e métricas

Defina `OTEL_EXPORTER_OTLP_ENDPOINT` (ex.: `http://otel-collector:4317`). Vazio desliga a
exportação — útil em desenvolvimento e nos testes.

O que já vem instrumentado: requisições HTTP de entrada e de saída, consultas do Npgsql,
métricas de runtime (GC, thread pool, exceções).

### Health check

`GET /health` — verifica a conexão com o Postgres. É o endpoint do liveness/readiness do
orquestrador. Fica fora do rastreamento para não encher a amostragem de traces com sondas.

---

## Ajuste de capacidade

| Sintoma | Onde olhar |
|---|---|
| 429 em uso legítimo | `RateLimit__PadraoPorMinuto`. Lembre: o limite é **por processo**, então N réplicas multiplicam por N. |
| Login lento sob carga | O hash de senha do Identity é caro por design. Escale horizontalmente; não reduza as iterações. |
| Consulta lenta na listagem | Confira o índice da coluna usada na ordenação e no filtro. O trace do Npgsql mostra o SQL real. |
| Tabela `refresh_tokens` grande | O worker limpa a cada 6 horas, com retenção de 30 dias. Ajuste em `LimpezaRefreshTokensJob`. |

---

## Atualizar dependências

Todas as versões estão em `Directory.Packages.props`.

```bash
dotnet list package --outdated
```

Suba uma família por vez (EF Core inteiro, OpenTelemetry inteiro) e rode a suíte. Os testes de
integração usam banco real, então quebra de compatibilidade do provider aparece no CI e não em
produção.
