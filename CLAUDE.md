# CLAUDE.md

Orientações para o Claude Code neste repositório.

## O que é este projeto

Template de backend .NET 10 — camadas (Api / Business / Data / Worker), `Result<T>`,
Unit of Work, autenticação JWT com refresh rotativo e painel administrativo.

**Antes de mexer na arquitetura, leia `docs/arquitetura.md`**, e mantenha `docs/padroes.md` à mão
enquanto escreve — ele traz o uso correto de cada padrão do projeto, com exemplos.

## Comandos

```bash
dotnet tool restore
dotnet csharpier format .
dotnet build --warnaserror
dotnet test tests/Backend.UnitTests
dotnet test tests/Backend.IntegrationTests     # exige Docker

dotnet ef migrations add Nome --project src/Backend.Data --startup-project src/Backend.Api
dotnet ef database update    --project src/Backend.Data --startup-project src/Backend.Api
```

## Regras fixas

Estas são convenções estabelecidas. Não desvie sem discutir antes.

### Sem comentário inline

Documente com bloco `/// <summary>` acima do membro, com `<remarks>`, `<param>` e `<returns>`
quando ajudar. Nada de `//` dentro de corpo de método. Exceção: `// Arrange` / `// Act` /
`// Assert` em teste.

### `Result<T>`, nunca notificação nem exceção para regra

Todo método público de service devolve `Result` ou `Result<T>`. Não existe `INotificador`.

Falha **prevista** (e-mail duplicado, registro não encontrado, sem permissão) é `Result` com
`Erro`. Exceção fica para o **imprevisto** (banco fora, bug) e é tratada pelo
`GlobalExceptionHandler`. Regra: se você previu, é `Result`; se te surpreendeu, é exceção.

Nenhum controller tem `try/catch`. Nenhum service lança exceção para sinalizar regra.

### Repositório não persiste

`SaveChangesAsync` só através de `IUnitOfWork.SalvarAsync()`, chamado pelo **service**.
Repositório monta consulta e marca mudança.

Exceção documentada: `RemoverInativosAnterioresA` usa `ExecuteDeleteAsync` (limpeza em massa do
worker, sem nada a compor) e as APIs do `UserManager`, que persistem por conta própria.

### Sem repositório genérico

Cada agregado tem repositório próprio com métodos de intenção. Não crie `Repository<TEntity>`,
não crie `ObterTodos()`.

### Sem exceção customizada

Use as exceções da BCL. O `GlobalExceptionHandler` mapeia para status HTTP. Não defina
`StatusCode` na mão no controller.

### Sem caminho qualificado inline

`using` no topo e nome curto no código. Nunca `Backend.Business.Auth.Models.Credenciais` no meio
de um método.

### Endpoint anônimo não confirma existência de conta

`esqueci-senha` e `reenviar-confirmacao` respondem 204 exista a conta ou não; login e acesso a
arquivo de terceiro respondem a mesma coisa para "não existe" e "não é seu". Distinguir os casos
transforma endpoint público em verificador. A distinção real vai só para o log.

Troca de senha (redefinição ou alteração) **sempre** derruba todas as sessões e dispara o aviso
por e-mail.

### Refresh token não aparece no corpo da resposta

Ele viaja em cookie `HttpOnly` (`CookieDeSessao`). Devolvê-lo no corpo anularia o cookie: um XSS
chamaria `/auth/refresh` e leria o token novo. Pelo mesmo motivo, no modo cookie o corpo da
requisição é **ignorado**, nunca usado como alternativa.

Endpoint novo que emita sessão usa `ResponderComSessao` do `AuthController`. Não monte
`TokenResponseDTO` com refresh token na mão.

### E-mail nunca sai na requisição

Service chama `IEmailService.Enfileirar(...)` e **não** chama `SalvarAsync` — o e-mail entra na
mesma transação de quem o originou. Quem envia é o `EnvioDeEmailJob` no worker. Nenhum service de
domínio usa `IEmailSender` diretamente.

### Arquivo: metadados no banco, bytes no provedor

Nunca monte a chave do objeto — ela é gerada pelo service a partir do id da entidade, nunca do
nome enviado pelo usuário. Provedor novo = implementar `IArmazenamentoDeArquivos` e traduzir o
"não encontrado" dele para `FileNotFoundException`.

Extensões são **lista de permissão**, em `Armazenamento:ExtensoesPermitidas`. Arquivo de terceiro
responde 404, nunca 403.

### Evento é só evento de negócio

`[RegistrarEvento("recurso.acao")]` marca ação de negócio. Tráfego HTTP bruto já está nos traces e
nos logs — não duplique numa tabela. Registrar evento nunca bloqueia e nunca lança; fila cheia
descarta.

### Datas em UTC

Armazene sempre em UTC. Converta para exibição só na borda, com
`Business/Common/Datas/DataUtils`. Nunca `DateTime.Now`.

### Validação vive uma vez

Regra de forma no validator do `Business`. Política de senha só em `IdentityOptions`. DTO de
request **não** leva atributo de validação.

## Estrutura

`Business` não referencia ninguém. Interface de repositório em `Business`, implementação em
`Data`. `Api` e `Worker` são só hosts.

Feature = mesma pasta em Api, Business e Data:
`Business/<Feature>/{Interfaces,Models,Services,Validators}`.

Pasta de feature no plural (`Usuarios/`, `Produtos/`), entidade no singular (`Usuario`) — pasta
singular colidiria com o nome do tipo.

Detalhes em `docs/estrutura.md`; passo a passo em `docs/nova-feature.md`.

## Ao criar uma feature

Ordem: entidade → interfaces → validator → service → mapping/repositório → migration →
DTO/controller → testes.

Registro de DI: service em `DependenciasBusiness`, repositório em `DependenciasData`. Validator
não precisa de registro (varredura de assembly). `IRegister` do Mapster também não.

## Testes

xUnit v3 + Shouldly + NSubstitute. Sem Moq, sem FluentAssertions (licença paga a partir da v8),
sem EF Core InMemory.

Unitário: sem I/O, repositórios substituídos.
Integração: API e Postgres reais via Testcontainers — e é onde as **fronteiras de autorização**
são cobertas, porque política mal declarada não quebra o build.

Passe `TestContext.Current.CancellationToken` nas chamadas que aceitam token.
