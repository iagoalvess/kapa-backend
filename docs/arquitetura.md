# Arquitetura

Arquitetura em camadas, com uma feature cortando todas elas. Quatro projetos executáveis ou
bibliotecas, e um grafo de dependência que só aponta para dentro.

```
        ┌─────────────┐        ┌──────────────┐
        │ Backend.Api │        │Backend.Worker│      hosts
        │   (HTTP)    │        │   (jobs)     │
        └──────┬──────┘        └──────┬───────┘
               │                      │
               └──────────┬───────────┘
                          ▼
                 ┌─────────────────┐
                 │ Backend.Business│               regra de negócio
                 │  (não referencia│               (não conhece EF nem ASP.NET)
                 │    ninguém)     │
                 └────────▲────────┘
                          │
                 ┌────────┴────────┐
                 │  Backend.Data   │               persistência
                 │ (implementa as  │
                 │  interfaces)    │
                 └─────────────────┘
```

A seta de `Data` aponta **para cima**: a interface do repositório é declarada em `Business` e
implementada em `Data`. É o que torna a regra de negócio testável sem banco e o que impede o EF
Core de vazar para dentro do domínio.

`Api` e `Worker` são apenas portas de entrada. Recebem estímulo — uma requisição HTTP, um tick de
relógio —, chamam um service e devolvem a saída. Regra de negócio dentro de controller ou de job
é erro de camada.

---

## O caminho de uma requisição

```
HTTP
 │
 ├─ Middleware de exceção ──── falha imprevista vira ProblemDetails 500
 ├─ CORS
 ├─ Rate limiter ───────────── 429 com Retry-After
 ├─ Autenticação ──────────── valida o JWT, monta as claims
 ├─ Autorização ───────────── aplica a política do endpoint
 │
 ▼
Controller ─── mapeia DTO → modelo de domínio
 │
 ▼
Service ─────── valida, consulta estado, decide, devolve Result<T>
 │      └─ Repository ────── monta a consulta (não persiste)
 │      └─ IUnitOfWork ───── commita tudo de uma vez
 ▼
Controller ─── traduz Result → 200 / 400 / 404 / 409 …
 │
 ▼
JSON
```

O controller faz três coisas e nada mais: mapear, chamar, traduzir. Não tem `if` de regra, não
acessa repositório e não tem `try/catch`.

---

## Tratamento de erro

Dois canais, com uma fronteira de uma frase: **se você previu, é `Result`; se te surpreendeu, é
exceção.**

| Situação | Canal | Resposta |
|---|---|---|
| E-mail já cadastrado, senha errada, registro inexistente, sem permissão | `Result` com `Erro` | 400 / 401 / 403 / 404 / 409 |
| Banco fora do ar, referência nula, JSON malformado | exceção | 500, 502 ou 503 |

O `MainController` traduz `Result` em HTTP. O `GlobalExceptionHandler` captura o que escapou.
Os dois produzem o mesmo formato de resposta.

### Formato do erro

`ProblemDetails` (RFC 9457), o padrão que gerador de client, proxy e ferramenta de
observabilidade já entendem:

```json
{
  "type": "https://httpstatuses.io/409",
  "title": "Já existe uma conta com este e-mail.",
  "status": 409,
  "instance": "POST /api/v1/auth/registrar",
  "codigo": "usuario.email_em_uso",
  "traceId": "0HNOFFGM8ADKM:00000001"
}
```

Erro de validação sai como `ValidationProblemDetails`, com os campos em `errors`.

`codigo` é o que o cliente deve usar para ramificar. `title` é texto para humano e pode mudar;
`codigo` é estável. `traceId` liga a resposta ao trace e às linhas de log daquela requisição.

Resposta de sucesso vai **sem envelope** — o recurso direto no corpo. Listagem usa `PaginaDTO<T>`,
o único envelope que carrega informação real.

---

## Autenticação e autorização

ASP.NET Identity para as contas (hash de senha, bloqueio por tentativas, confirmação de e-mail,
2FA disponível) e JWT para as sessões.

```
POST /auth/login
      └─> access token  (JWT, 15 min, não revogável)
      └─> refresh token (opaco, 7 dias, revogável, guardado como hash)

POST /auth/refresh
      └─> rotaciona: revoga o apresentado, emite um par novo
      └─> se um token já rotacionado reaparecer → todas as sessões do usuário caem
```

**Rotação com detecção de reúso.** Cada renovação invalida o token usado. Um token já rotacionado
voltar a aparecer só tem uma explicação — alguém copiou a credencial — e, como não há como saber
se quem apresenta é o dono ou o atacante, derrubam-se todas as sessões.

O refresh token é guardado **como hash**, pela mesma razão que uma senha: banco vazado entrega
hashes inúteis, não sessões prontas para uso.

A janela entre revogar um acesso e ele parar de funcionar é a validade do access token — no
máximo 15 minutos. É a consequência de o JWT ser validado por assinatura e não por consulta ao
banco; fechar essa janela custaria uma ida ao banco por requisição.

### Ciclo de vida da conta

`/auth` cuida da **sessão**; `/conta` cuida da **conta**, e quase tudo ali acontece sem ninguém
logado — é justamente quando o usuário não consegue entrar.

```
registro ──> e-mail de confirmação enfileirado automaticamente

POST /conta/esqueci-senha        → 204 sempre; e-mail sai só se a conta existir
POST /conta/redefinir-senha      → consome o token, derruba todas as sessões, avisa por e-mail
POST /conta/confirmar-email      → consome o token
POST /conta/reenviar-confirmacao → 204 sempre
POST /conta/alterar-senha        → exige a senha atual, derruba todas as sessões, avisa
```

Cinco decisões que fazem esse fluxo ser seguro:

- **Resposta constante.** "Esqueci a senha" e "reenviar confirmação" respondem 204 exista a conta
  ou não. Distinguir os dois casos transformaria endpoints públicos e anônimos em verificadores
  de quais e-mails têm cadastro.
- **Troca de senha derruba tudo.** Se a redefinição aconteceu porque a conta foi comprometida,
  deixar os refresh tokens do atacante vivos anularia a troca — ele continuaria renovando o
  acesso indefinidamente.
- **Alterar senha exige a senha atual**, mesmo com o usuário já autenticado. Um access token
  esquecido numa máquina aberta não deve bastar para tomar a conta.
- **Aviso de senha alterada.** Não é cortesia: é o único sinal que o dono legítimo recebe quando
  alguém trocou a senha dele.
- **Token inválido e senha fraca são erros distintos.** São problemas com soluções opostas —
  "peça um link novo" versus "escolha outra senha"; a mesma mensagem para os dois deixa o usuário
  tentando a coisa errada.

Os tokens são os do Identity, atrelados ao `SecurityStamp` (uso único), com validade de 4 horas
em vez do padrão de um dia — link de e-mail é credencial parada numa caixa de entrada. Viajam em
Base64 URL-safe: o Base64 comum do Identity tem `+`, que vira espaço numa query string e produz o
clássico "o link de redefinição não funciona".

`Conta:ExigirEmailConfirmado` bloqueia o login de quem não confirmou. Fica **desligado por
padrão** — com `Smtp:Host` vazio os e-mails só vão para o log, e ligar isso sem SMTP trancaria
todo mundo para fora no primeiro cadastro. Ligue junto com o SMTP, não antes.

### Autorização

Políticas nomeadas em `Politicas.cs`, nunca `[Authorize(Roles = "texto")]` espalhado:

```csharp
[Authorize(Policy = Politicas.SomenteAdministrador)]
```

**O administrador é coringa.** O helper `ExigirPerfil()` aceita quem tem o perfil pedido *ou* o
de administrador, então toda política nova já nasce acessível ao admin. Para permissão granular
(módulo × ação), o ponto de extensão é trocar a asserção por um `IAuthorizationHandler` mantendo
esse atalho — nada nos controllers muda.

### Travas do administrador

A gestão de usuários recusa três operações, todas contra o mesmo desastre — um sistema sem
ninguém capaz de administrá-lo, cuja recuperação exige acesso direto ao banco:

- desativar o próprio acesso;
- remover o próprio perfil de administrador;
- desativar ou rebaixar o **último** administrador ativo.

---

## Persistência

Um único `DbContext`, com Identity e domínio na mesma cadeia de migrations.

- **Chave**: `Guid` versão 7. Tem prefixo de timestamp, então os inserts caem em páginas
  sequenciais do índice em vez de espalhados como no v4 — mantendo a vantagem de gerar o id na
  aplicação e de não ser enumerável.
- **Nomes**: `snake_case`, incluindo as tabelas do Identity, para não precisar aspear metade do
  banco no `psql`.
- **Mapeamento**: um `IEntityTypeConfiguration` por entidade em `Data/Mappings/`, carregado por
  varredura. O `AppDbContext` fica com poucas linhas em vez de um `OnModelCreating` gigante.
- **Auditoria**: `CriadoEm` e `AtualizadoEm` são carimbados pelo contexto no `SaveChangesAsync`.
- **Migration não roda no boot.** Duas réplicas subindo juntas tentariam migrar ao mesmo tempo, e
  uma migration destrutiva num container em loop de reinício aplica o estrago sozinha. É passo de
  deploy — ver [operacao.md](operacao.md).

---

## Envio de e-mail

E-mail **nunca sai durante a requisição HTTP**. O service enfileira; o worker envia.

```
Service ──> IEmailService.Enfileirar()  (mesma transação do domínio)
                     │
                     ▼
              tabela emails_fila
                     │
                     ▼
Worker ──> reserva um lote  (FOR UPDATE SKIP LOCKED, transação curta)
       ──> envia            (fora de transação)
       ──> registra o resultado  (enviado, ou reagendado com espera crescente)
```

Três propriedades que isso garante:

- **O endpoint não fica refém do SMTP.** O tempo de resposta não depende do humor do provedor.
- **O e-mail não se perde.** Provedor fora do ar significa retentativa, não mensagem sumida.
- **Não há envio duplicado com o worker escalado.** `FOR UPDATE SKIP LOCKED` faz cada réplica
  pular as linhas que outra já reservou.

A reserva acontece **antes** do envio. Se o processo morrer no meio, o e-mail fica preso em
`Enviando` — escolha consciente entre "pode ficar preso" e "pode ser enviado duas vezes", porque
receber a mesma cobrança duas vezes é pior que não receber.

Retentativa com espera exponencial (3, 9, 27, 81 minutos) e no máximo 5 tentativas. Esgotadas, o
registro fica em `Falhou` **com o erro preservado** — um e-mail que não chegou é justamente o que
alguém vai procurar depois.

### Por que SMTP e não o SDK de um provedor

Amazon SES, SendGrid, Mailgun, Resend, Postmark e Gmail **todos falam SMTP**. Uma implementação
cobre todos, e trocar de provedor é mudar host, porta e credencial no ambiente.

Um cliente específico só passa a valer a pena quando você precisa de algo que SMTP não oferece:
webhook de bounce, template hospedado no provedor, envio em lote personalizado. Nesse dia,
implemente `IEmailSender` e troque o registro na injeção de dependência.

Com `Smtp:Host` vazio, entra um remetente que apenas registra a mensagem no log — é o que faz o
projeto subir e o fluxo funcionar de ponta a ponta sem um servidor SMTP à mão.

---

## Armazenamento de arquivos

**Metadados no banco, bytes no provedor.**

```
POST /arquivos (multipart)
      │
      ├─ valida nome, extensão, tamanho e categoria
      ├─ grava os bytes no provedor  ──> IArmazenamentoDeArquivos
      └─ registra os metadados       ──> tabela arquivos
```

A linha no banco é a fonte da verdade sobre quais arquivos existem, quem os enviou e quem pode
baixá-los. Sem ela, listar e autorizar exigiria varrer o bucket, e não haveria como distinguir um
objeto em uso de um órfão.

### A chave é gerada pela aplicação

`categoria/ano/mês/identificador.extensão` — nunca derivada do nome enviado. Nome de arquivo vindo
de fora traz `../`, caractere de controle, unicode ambíguo e colisão, e qualquer um desses vira
leitura ou escrita fora do diretório pretendido. O nome original é guardado como metadado e
devolvido no download.

### Validação é fronteira de confiança

Nome, tipo e tamanho vêm do cliente; nenhum é aceito como verdade. Três regras, todas de
segurança:

- **lista de permissão** de extensões, nunca de bloqueio — lista de bloqueio sempre esquece uma
  extensão executável, e basta uma para transformar upload em execução remota de código;
- **tamanho máximo**, alinhado aos limites do Kestrel e do leitor de multipart, para o envio ser
  recusado com a mensagem certa em vez de cortado pelo servidor;
- **categoria** restrita a minúsculas, números e hífen, já que ela compõe a chave.

### Ordem das operações

| Operação | Ordem | Se falhar no meio |
|---|---|---|
| Envio | bytes → metadados | Objeto órfão no provedor: desperdício de espaço. |
| Remoção | metadados → bytes | Objeto órfão no provedor: desperdício de espaço. |

A ordem inversa, nos dois casos, produziria o erro caro — um arquivo listado que não abre. Entre
os dois erros possíveis, o template escolhe sempre o barato.

### Acesso

Cada usuário enxerga os próprios arquivos; o administrador enxerga todos. Arquivo de terceiro
responde **404, e não 403** — devolver 403 confirmaria que o identificador existe, transformando o
endpoint num verificador.

Regra além disso — anexo visível para toda a equipe do pedido, documento restrito por filial —
depende do domínio e entra no service do projeto.

### Provedores

| Provedor | Quando |
|---|---|
| `Local` | Desenvolvimento e testes. Disco de container é efêmero e não é compartilhado entre réplicas — **não serve para produção**. |
| `S3` | Produção. Funciona com AWS, MinIO, Cloudflare R2 e Backblaze B2 via `ServiceUrl`. |

A escolha acontece uma vez, no registro de dependências. Nenhum outro ponto do projeto sabe qual
provedor está ativo.

Credenciais da AWS vêm da cadeia padrão do SDK — perfil de instância, role do IRSA, variáveis de
ambiente. Não há campo de chave na configuração da aplicação.

### O download passa pela API

Em vez de devolver uma URL assinada do provedor. É mais tráfego, e mantém a autorização em um
lugar só: URL assinada, uma vez emitida, vale para quem a tiver em mãos, independentemente de o
usuário ter perdido o acesso no meio do caminho.

O teto disso é conhecido — arquivo grande ocupa a conexão da API pelo tempo da transferência. Se
virar problema, o ponto de mudança é acrescentar emissão de URL temporária ao
`IArmazenamentoDeArquivos`, aceitando a troca de autorização.

### O que não está aqui

Verificação antivírus e inspeção do conteúdo real (o tipo declarado pelo cliente não é
verificado contra os bytes). Para upload público e não confiável, os dois entram entre a
validação e a gravação.

---

## Captura de eventos

Eventos de negócio nomeados, gravados fora do caminho da requisição.

```
[RegistrarEvento("produto.criado")]   ──> só em resposta 2xx
              │
              ▼
   fila em memória (limitada, descarta quando cheia)
              │
              ▼
   serviço de descarga ──> grava em lote na tabela eventos
              │
              ▼
   worker de retenção ──> apaga o que passou do prazo
```

A propriedade central: **registrar um evento nunca bloqueia e nunca lança.** Fila cheia descarta
o evento e conta o descarte. Perder um ponto do gráfico é aceitável; atrasar ou derrubar a
operação que estava sendo medida, não.

A fila é limitada de propósito — fila ilimitada não é fila, é um vazamento de memória que derruba
o processo justamente quando o banco está lento.

`Dados` é `jsonb`, o que permite acrescentar um campo a um evento sem migration.

### O que registrar

Só **evento de negócio**: "produto criado", "relatório exportado", "perfil alterado". Tráfego HTTP
bruto — quem chamou o quê, quanto demorou, qual status — já está nos traces do OpenTelemetry e nos
logs estruturados. Duplicar isso numa tabela só produz custo de disco.

A tabela ganha quando a pergunta mistura evento com domínio: *"quais clientes do plano X usaram a
feature Y?"*. Para funil, retenção e coorte de produto, uma ferramenta dedicada (PostHog, Umami)
faz melhor.

**É memória de um processo.** O que estiver na fila num encerramento abrupto se perde, e cada
réplica tem a sua. Evento que não pode ser perdido — cobrança, auditoria legal — grava na
transação do domínio, não aqui.

---

## Observabilidade

Três sinais, nenhum agente proprietário:

| Sinal | Como |
|---|---|
| **Logs** | `ILogger` nativo. JSON de uma linha no stdout fora de desenvolvimento, console legível em dev. |
| **Traces** | OpenTelemetry: requisições de entrada, chamadas HTTP de saída, consultas do Npgsql. |
| **Métricas** | OpenTelemetry: ASP.NET Core, HttpClient e runtime (GC, thread pool, exceções). |

A exportação é OTLP, aceita por Grafana/Tempo/Loki, Datadog, Jaeger, Honeycomb e New Relic —
trocar de backend é variável de ambiente. Vazia, desliga a exportação.

Não há sink de agregador dentro da aplicação: quem coleta o stdout é o agente da plataforma.
Aplicação que escreve direto no agregador trava quando o agregador cai.

`GET /health` verifica a conexão com o Postgres e fica fora do rastreamento, para as sondas do
orquestrador não encherem a amostragem de traces.

---

## Limitação de taxa

Limitador nativo do runtime, com duas políticas: uma padrão e uma estreita para os endpoints de
autenticação — login sem limite é um oráculo de força bruta contra as senhas dos usuários.

A identificação é pelo usuário autenticado e, quando anônimo, pelo IP. Usuário antes de IP porque
um escritório inteiro sai pelo mesmo IP, e limitar só por IP faria um usuário barulhento bloquear
os colegas.

**O limite é por processo.** Com N réplicas, o limite efetivo é N vezes maior. É proteção contra
abuso acidental e força bruta, não contra DDoS distribuído — para isso o lugar é a borda.

---

## Versionamento da API

Versão no caminho (`/api/v1/...`), uma pasta por versão em `Controllers/`.

Uma V2 só nasce quando existe **quebra de contrato**. Campo novo opcional, correção de bug e campo
novo na resposta continuam na V1 — versionar por comodidade dobra a superfície de teste. Ao criar
a V2, só o controller afetado é copiado; os outros declaram as duas `[ApiVersion]`.

---

Onde colocar cada arquivo: [estrutura.md](estrutura.md).
Como usar cada padrão no código: [padroes.md](padroes.md).
