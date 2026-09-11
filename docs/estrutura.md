# Estrutura de pastas

Regra geral: **camada por projeto, feature por pasta dentro da camada.**
Uma feature nova nunca cria projeto novo — cria a mesma pasta em Api, Business e Data.

```
backend/
├── Directory.Build.props        # TargetFramework, Nullable, analisadores — vale p/ toda a solução
├── Directory.Packages.props     # TODAS as versões de pacote (Central Package Management)
├── docker-compose.yml           # postgres + api + worker
├── .config/dotnet-tools.json    # csharpier, dotnet-ef (versionados junto com o repo)
├── docs/
├── src/
│   ├── Backend.Api/
│   ├── Backend.Business/
│   ├── Backend.Data/
│   └── Backend.Worker/
└── tests/
    ├── Backend.UnitTests/
    └── Backend.IntegrationTests/
```

## Grafo de dependências

```
Api ──────┐
          ├──> Business <── Data
Worker ───┘                  ▲
   │                         │
   └─────────────────────────┘
```

`Business` **não referencia ninguém**. Não conhece EF Core, não conhece ASP.NET.
As interfaces de repositório vivem em `Business`, a implementação em `Data` — é a inversão de
dependência que faz a regra de negócio ser testável sem banco.

`Api` e `Worker` são apenas *hosts*: recebem entrada (HTTP / agendamento), chamam um service e
devolvem saída. Regra de negócio dentro de controller ou de job é bug de arquitetura.

---

## src/Backend.Api — o host HTTP

```
Backend.Api/
├── Program.cs                 # curto: só encadeia os Add*/Use* de Configuration/
├── Configuration/             # um arquivo por preocupação, todos com Add*/Use*
│   ├── ApiConfig.cs           #   controllers, versionamento, CORS, ProblemDetails
│   ├── AuthConfig.cs          #   Identity + JWT Bearer + políticas de autorização
│   ├── ObservabilidadeConfig.cs #  logging JSON, OpenTelemetry, health checks
│   ├── RateLimitConfig.cs     #   políticas de rate limiting nativo
│   ├── ScalarConfig.cs        #   OpenAPI + UI Scalar
│   └── DependenciasConfig.cs  #   DI dos services de Business
├── Controllers/
│   ├── MainController.cs      # base: traduz Result -> IActionResult. Nada além disso.
│   └── V1/
│       ├── Auth/AuthController.cs
│       └── Usuarios/UsuarioController.cs
├── DTOs/                      # contrato HTTP. NUNCA vaza pra Business.
│   ├── Comum/                 #   PaginacaoRequestDTO, PaginaDTO<T>
│   ├── Auth/
│   └── Usuarios/
├── Mapping/                   # Mapster: um IRegister por feature (Model <-> DTO)
├── Extensions/                # helpers do host: IUsuarioAtual, atributos, claims
└── Middleware/                # GlobalExceptionHandler (IExceptionHandler)
```

### Versionamento

Uma pasta por versão dentro de `Controllers/`, e a versão aparece na rota:

```csharp
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/usuarios")]
public class UsuarioController : MainController
```

- Pasta = `Controllers/V1/<Feature>/`, namespace = `Backend.Api.Controllers.V1.<Feature>`.
- **V2 só nasce quando existe quebra de contrato.** Campo novo opcional, correção de bug e
  campo novo na resposta continuam na V1 — versionar por comodidade dobra a superfície de teste.
- Ao criar a V2 de um recurso, copie **só aquele controller** para `Controllers/V2/<Feature>/`.
  Os outros continuam servindo `1.0` e `2.0` ao mesmo tempo declarando as duas `[ApiVersion]`.
- A versão a ser removida ganha `Deprecated = true` no `[ApiVersion]`, o que o Scalar mostra
  na documentação antes de você apagar o código.

### Por que o controller é burro

O controller faz exatamente três coisas: mapear DTO -> Model, chamar o service, traduzir
`Result` -> HTTP. Sem `if` de regra, sem acesso a repositório, sem `try/catch`.

---

## src/Backend.Business — a regra de negócio

```
Backend.Business/
├── Abstractions/              # o vocabulário compartilhado por TODAS as features
│   ├── Entity.cs              #   Id (UUIDv7) + CriadoEm/AtualizadoEm
│   ├── Result.cs              #   Result / Result<T> — o retorno padrão de todo service
│   ├── Erro.cs                #   código + mensagem + tipo (Validacao/NaoEncontrado/Conflito/...)
│   ├── IUnitOfWork.cs         #   SalvarAsync / EmTransacaoAsync
│   └── Paginacao.cs           #   PaginacaoRequest + PaginaDe<T>
├── Common/                    # utilitários de verdade compartilhados. NÃO é lixeira.
│   ├── AplicacaoSettings.cs   #   nome e URL do front, usados nos e-mails
│   ├── Datas/                 #   DataUtils: fuso, início/fim de período
│   └── Texto/                 #   normalização e mascaramento de dado sensível
├── Auth/                      # <── uma pasta por feature
│   ├── Interfaces/            #   IAuthService, IContaService, ITokenService, IEmailsDeConta
│   ├── Models/                #   RefreshToken, ParDeTokens, Credenciais, RedefinirSenha
│   ├── Services/              #   AuthService (sessão), ContaService (conta), TokenService
│   ├── Settings/              #   JwtSettings, ContaSettings
│   └── Validators/
├── Usuarios/                  #   usuário, perfis e travas do administrador
├── Admin/                     #   resumo do painel administrativo
├── Emails/                    #   fila de envio, remetente SMTP, modelos
├── Arquivos/                  #   metadados, provedores Local e S3
└── Eventos/                   #   modelo e contrato de captura de uso
```

As quatro últimas seguem exatamente o mesmo contrato de subpastas de `Auth/` e `Usuarios/` —
foram omitidas acima só para a árvore caber.

### O contrato de uma feature

Toda feature repete as mesmas subpastas:

| Subpasta | Contém | Regra |
|---|---|---|
| `Interfaces/` | `I<Feature>Service`, `I<Feature>Repository` | O repositório é declarado aqui e implementado em `Data`. Sem repositório genérico. |
| `Models/` | entidade + read models | Entidade herda de `Entity`. Read model é `record`, não classe. |
| `Services/` | a regra | Todo método público devolve `Result` ou `Result<T>`. |
| `Validators/` | `AbstractValidator<T>` | Validação de forma. Regra de negócio é do service, não do validator. |
| `Settings/` | classes de `IOptions` | Só quando a feature tem configuração própria. |
| `Enums/` | enums da feature | Crie quando precisar; não deixe pasta vazia. |

`Common/` é para o que **duas ou mais features** usam de fato. Utilitário de uma feature só
mora dentro da feature. A pasta `Common` de qualquer projeto vira depósito de entulho se essa
regra não for seguida.

---

## src/Backend.Data — persistência

```
Backend.Data/
├── Context/
│   └── AppDbContext.cs        # único DbContext (Identity + domínio na mesma migration)
├── Mappings/                  # um IEntityTypeConfiguration<T> por entidade
├── Repositories/              # implementação das interfaces declaradas em Business
├── Migrations/                # geradas pelo dotnet ef
├── Seed/                      # dados iniciais idempotentes (perfis, admin)
├── UnitOfWork.cs
└── DependenciasData.cs        # AddData(config): DbContext + repositórios, num lugar só
```

`Mappings/` usa `IEntityTypeConfiguration` em vez de configurar tudo dentro do
`OnModelCreating` — o `AppDbContext` fica com 20 linhas em vez de 800.

---

## src/Backend.Worker — trabalho de fundo

```
Backend.Worker/
├── Program.cs                 # curto: AddObservabilidade + AddWorker
├── Configuration/
│   ├── DependenciasWorker.cs  #   registra dados, negócio, Identity e os jobs
│   └── ObservabilidadeConfig.cs
└── Jobs/                      # um BackgroundService por job
    ├── EnvioDeEmailJob.cs
    ├── LimpezaRefreshTokensJob.cs
    └── RetencaoDeEventosJob.cs
```

O worker chama os **mesmos services de Business** que a API. Um job nunca reimplementa regra.

O registro fica em `DependenciasWorker`, e não solto no `Program.cs`, para poder ser exercitado
por teste: um `BackgroundService` é singleton e não pode consumir serviço `scoped`, o runtime só
reprova isso ao construir o provedor, e o worker não tem requisição HTTP para um teste de
integração exercitar. Sem esse ponto de entrada, o erro só apareceria como container em loop de
reinício.

---

## tests/

```
tests/
├── Backend.UnitTests/         # sem I/O. Substitutos no lugar dos repositórios.
│   ├── Abstractions/          #   Result, paginação
│   ├── Auth/ Usuarios/ Emails/ Arquivos/ Eventos/ Common/
│   └── Worker/                #   validação do grafo de dependências dos jobs
└── Backend.IntegrationTests/  # sobe a API de verdade + Postgres em container
    ├── Infra/                 #   ApiFactory (Testcontainers) + atalhos de autenticação
    └── Auth/ Conta/ Usuarios/ Admin/ Arquivos/ Eventos/
```

O espelho é intencional: `Business/Auth/Services/AuthService.cs` é testado em
`Backend.UnitTests/Auth/AuthServiceTests.cs`. Achar o teste de um arquivo nunca deve exigir busca.

---

## Onde colocar cada coisa (referência rápida)

| Vou escrever... | Vai em |
|---|---|
| endpoint HTTP novo | `Api/Controllers/V1/<Feature>/` |
| formato do JSON de entrada/saída | `Api/DTOs/<Feature>/` |
| regra de negócio | `Business/<Feature>/Services/` |
| consulta ao banco | interface em `Business/<Feature>/Interfaces/`, SQL/LINQ em `Data/Repositories/` |
| entidade nova | `Business/<Feature>/Models/` + mapeamento em `Data/Mappings/` |
| validação de payload | `Business/<Feature>/Validators/` |
| configuração de host (DI, auth, CORS) | `Api/Configuration/` |
| helper usado por 1 feature | dentro da própria feature |
| helper usado por 2+ features | `Business/Common/` |
| job agendado | `Worker/Jobs/` |
