# Padrões de código

Os padrões aplicados no projeto, com o uso correto de cada um. É a referência de consulta
enquanto se escreve código — a visão geral está em [arquitetura.md](arquitetura.md).

---

## `Result<T>` — o retorno de todo service

Todo método público de service devolve `Result` ou `Result<T>`. A falha faz parte do tipo, então
ignorá-la exige escrever código que a ignora.

```csharp
// Sem valor de retorno
Task<Result> AlterarAtivacao(Guid id, bool ativo, CancellationToken ct = default);

// Com valor
Task<Result<UsuarioDetalhe>> ObterPorId(Guid id, CancellationToken ct = default);
```

### Produzindo

```csharp
return Result.Ok();                                  // sucesso sem valor
return Result.Ok(detalhe);                           // sucesso com valor
return detalhe;                                      // idem, por conversão implícita
return Erro.NaoEncontrado("usuario.nao_encontrado", "Usuário não encontrado.");
return Result.Falha<UsuarioDetalhe>(validacao.Erros);  // vários erros
```

As conversões implícitas existem para o caminho comum ficar curto. Em método que devolve
`Result<T>`, tanto um `T` quanto um `Erro` viram o resultado correspondente sozinhos.

### Consumindo

```csharp
var resultado = await usuarioService.ObterPorId(id, ct);

if (resultado.Falhou)
    return Result.Falha<OutraCoisa>(resultado.Erros);

var usuario = resultado.Valor;
```

`Valor` lança se o resultado for de falha. É intencional: acessá-lo sem checar é bug, e um erro
alto e imediato é melhor que um `null` circulando.

### Compondo

```csharp
resultado.Map(usuario => usuario.Adapt<UsuarioDetalheDTO>())   // transforma o sucesso
resultado.Bind(usuario => await OutroPasso(usuario))            // encadeia algo que também falha
resultado.Tap(usuario => await NotificarAsync(usuario))         // efeito colateral no caminho feliz
```

Nenhum deles executa a função quando o resultado já é falha — o erro atravessa intacto.

---

## `Erro` — falha de negócio nomeada

```csharp
Erro.Validacao("produto.preco_invalido", "O preço deve ser maior que zero.", campo: "preco");
Erro.NaoEncontrado("produto.nao_encontrado", "Produto não encontrado.");
Erro.Conflito("produto.nome_em_uso", "Já existe um produto com este nome.");
Erro.NaoAutenticado("auth.credenciais_invalidas", "E-mail ou senha incorretos.");
Erro.Proibido("produto.sem_acesso", "Você não pode alterar produtos de outra filial.");
Erro.Indisponivel("pagamento.fora_do_ar", "O provedor de pagamento não respondeu.");
```

O **tipo** determina o status HTTP; o service nunca vê status code.

| Tipo | Status |
|---|---|
| `Validacao` | 400 |
| `NaoAutenticado` | 401 |
| `Proibido` | 403 |
| `NaoEncontrado` | 404 |
| `Conflito` | 409 |
| `Indisponivel` | 503 |

O **código** é o identificador estável, no formato `recurso.motivo`. É por ele que o cliente
ramifica. A mensagem é texto para humano e pode mudar a qualquer momento; o código não.

---

## `IUnitOfWork` — quem decide o commit

Repositório monta consulta e marca mudança. **Quem persiste é o service.**

```csharp
await _pedidoRepository.Adicionar(pedido, ct);
await _estoqueRepository.Baixar(itens, ct);
await _unitOfWork.SalvarAsync(ct);        // uma transação, tudo ou nada
```

Com `SaveChanges` dentro de cada repositório, o segundo pode falhar depois de o primeiro gravar —
inconsistência silenciosa, sem exceção e sem log.

Para trabalho que precisa de transação explícita — mais de um `SalvarAsync`, ou escrita
combinada com algo fora do contexto:

```csharp
var lote = await _unitOfWork.EmTransacaoAsync(token => _repositorio.ReservarLote(20, agora, token), ct);
```

A operação roda sob a estratégia de execução do provider, que **reexecuta tudo** em falha
transitória — por isso ela precisa ser idempotente.

### As duas exceções

| Caso | Por quê |
|---|---|
| `UserManager` do Identity | As APIs dele persistem por conta própria. Para compor com outra escrita, envolva em `EmTransacaoAsync`. |
| Remoção em massa (`ExecuteDeleteAsync`) | Limpeza do worker, sem nada a compor. Carregar milhares de entidades só para descartá-las seria desperdício. |

---

## Repositórios

Um por agregado, com **métodos de intenção**. Não existe `Repository<TEntity>` genérico.

```csharp
public interface IProdutoRepository
{
    Task<PaginaDe<ProdutoResumo>> Listar(PaginacaoRequest paginacao, string? busca, CancellationToken ct = default);
    Task<ProdutoDetalhe?> ObterDetalhe(Guid id, CancellationToken ct = default);
    Task<bool> ExisteComNome(string nome, CancellationToken ct = default);
    Task<Produto?> ObterParaEdicao(Guid id, CancellationToken ct = default);
    Task Adicionar(Produto produto, CancellationToken ct = default);
}
```

`DbContext` já é Unit of Work e `DbSet<T>` já é repositório — uma camada genérica em cima não
abstrai nada e ainda vaza LINQ-to-Entities inteiro para o service. E `ObterTodos()` é a assinatura
mais perigosa que existe: funciona com 30 linhas em desenvolvimento e derruba produção com 3
milhões.

### Leitura e escrita são métodos diferentes

```csharp
// Leitura: sem rastreamento, projetada no SELECT
public async Task<ProdutoDetalhe?> ObterDetalhe(Guid id, CancellationToken ct = default) =>
    await db.Produtos.AsNoTracking()
        .Where(p => p.Id == id)
        .Select(p => new ProdutoDetalhe(p.Id, p.Nome, p.Preco))
        .FirstOrDefaultAsync(ct);

// Escrita: rastreada, porque vai ser alterada
public Task<Produto?> ObterParaEdicao(Guid id, CancellationToken ct = default) =>
    db.Produtos.FirstOrDefaultAsync(p => p.Id == id, ct);
```

Só quem vai escrever paga o custo do rastreamento, e a leitura traz do banco apenas as colunas que
a tela usa.

---

## Paginação

```csharp
public async Task<Result<PaginaDe<ProdutoResumo>>> Listar(PaginacaoRequest paginacao, string? busca, CancellationToken ct = default)
{
    var pagina = await produtoRepository.Listar(paginacao.Normalizar(), busca, ct);

    return Result.Ok(pagina);
}
```

`Normalizar()` aplica o teto do servidor (100 itens). A borda aceita qualquer inteiro, e
`tamanho=100000` vindo de fora não pode virar consulta.

**Ordene sempre com um critério de desempate único:**

```csharp
.OrderBy(p => p.Nome).ThenBy(p => p.Id)
```

Sem desempate, dois registros de mesmo nome trocam de posição entre páginas — um item some da
listagem e outro aparece duas vezes.

---

## Validação

Validator cuida de **forma**: obrigatório, tamanho, formato.

```csharp
public sealed class CriarProdutoValidator : AbstractValidator<CriarProduto>
{
    public CriarProdutoValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().WithMessage("O nome é obrigatório.").MaximumLength(200);
        RuleFor(x => x.Preco).GreaterThan(0).WithMessage("O preço deve ser maior que zero.");
    }
}
```

Uso no service, sempre como primeiro passo:

```csharp
var validacao = criarValidator.Validar(dados);
if (validacao.Falhou)
    return Result.Falha<ProdutoDetalhe>(validacao.Erros);
```

`Validar()` devolve **todos** os erros: um formulário com três campos inválidos acende os três de
uma vez, em vez de obrigar a três idas ao servidor.

Regra que depende do estado do sistema — "este nome já existe", "este é o último administrador" —
é do **service**, que tem o repositório. Validator que consulta banco vira consulta escondida.

Validador não precisa de registro: `AddValidatorsFromAssembly` varre o assembly de `Business`.

### Onde a regra mora — uma vez só

| Regra | Lugar único |
|---|---|
| Forma do payload | Validator em `Business/<Feature>/Validators/` |
| Política de senha | `IdentityOptions`, em `AuthConfig` |
| Limite de tamanho de página | `PaginacaoRequest.Normalizar()` |
| Tamanho de coluna | `IEntityTypeConfiguration` |

DTO de request **não leva atributo de validação**. Duplicar a regra em duas camadas garante que
um dia as duas discordem — e a que o teste cobre não é a que roda.

---

## Controllers

```csharp
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/produtos")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class ProdutoController(IProdutoService produtoService) : MainController
{
    [HttpGet("{id:guid}", Name = RotaDeDetalhe)]
    [Authorize(Policy = Politicas.SomenteAdministrador)]
    [ProducesResponseType(typeof(ProdutoDetalheDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken ct)
    {
        var resultado = await produtoService.ObterPorId(id, ct);

        return Responder(resultado.Map(produto => produto.Adapt<ProdutoDetalheDTO>()));
    }
}
```

Os três helpers do `MainController`:

| Método | Sucesso |
|---|---|
| `Responder(Result)` | 204 sem corpo |
| `Responder(Result<T>)` | 200 com o corpo |
| `Criado(Result<T>, rota, valores)` | 201 com `Location` |

Falha, em qualquer um deles, vira o `ProblemDetails` correspondente ao tipo do erro.

**Autenticação é o padrão.** `MainController` já traz `[Authorize]`; endpoint público declara
`[AllowAnonymous]` explicitamente. O contrário — proteger só o que alguém lembrou de marcar — é
como endpoint vaza.

---

## Autorização

```csharp
[Authorize(Policy = Politicas.SomenteAdministrador)]
```

Nunca `[Authorize(Roles = "Administrador")]` espalhado: erro de digitação em string de papel não é
erro de compilação, vira 403 em produção — ou, pior, um endpoint que deveria ser restrito e não é.

Política nova:

```csharp
services.AddAuthorizationBuilder()
    .AddPolicy(Politicas.Financeiro, politica => politica.RequireAuthenticatedUser().ExigirPerfil(PerfisPadrao.Financeiro));
```

`ExigirPerfil()` sempre deixa o administrador passar, então toda política nova já nasce acessível
a ele.

---

## Entidades e modelos de leitura

```csharp
// Entidade: muda ao longo do tempo, herda de Entity (id UUIDv7 + auditoria)
public class Produto : Entity
{
    public string Nome { get; set; } = string.Empty;
    public decimal Preco { get; set; }
}

// Modelo de leitura: record, projetado direto no SELECT
public sealed record ProdutoResumo(Guid Id, string Nome, decimal Preco);
```

A entidade é dona das próprias transições de estado quando elas têm regra:

```csharp
public void RegistrarFalha(string erro, DateTime agoraUtc, int maximoDeTentativas) { … }
```

É o que permite testar a política sem banco, sem rede e sem worker — como acontece com
`EmailNaFila`.

Entidade imutável e append-only (evento, log de auditoria) **não** herda de `Entity`: um campo
`AtualizadoEm` ali seria uma promessa falsa, e são as tabelas que mais crescem.

---

## Jobs do worker

```csharp
public sealed class MeuJob(IServiceScopeFactory scopeFactory, ILogger<MeuJob> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var relogio = new PeriodicTimer(Intervalo);

        do
        {
            await ExecutarUmaVez(stoppingToken);
        } while (await EsperarProximaExecucao(relogio, stoppingToken));
    }
}
```

Três detalhes que todo job repete:

1. **Escopo por execução.** Repositórios são `scoped` e o `BackgroundService` é singleton —
   injetar o repositório no construtor prenderia um `DbContext` vivo pelo tempo do processo.
2. **Roda uma vez ao subir**, depois no intervalo. Um container que reinicia não deve esperar seis
   horas pela primeira execução.
3. **Engole a exceção e segue.** Falha numa execução não pode derrubar o host, o que mataria
   também os outros jobs. `OperationCanceledException` de desligamento é reerguida.

`PeriodicTimer` do runtime, sem Hangfire nem Quartz. O teto disso é conhecido: sem persistência de
agendamento, sem retentativa automática e sem painel. Quando o projeto precisar de **uma** dessas
três coisas, o job vira um método com `RecurringJob.AddOrUpdate` — sem mudar nada em `Business`.

---

## Enviando e-mail

```csharp
await emailService.Enfileirar(
    new NovoEmail(usuario.Email, "Bem-vindo", corpoHtml, EEmailPrioridade.Alta),
    ct
);

await unitOfWork.SalvarAsync(ct);
```

O service de e-mail **não chama `SalvarAsync`** — o e-mail entra na mesma transação de quem o
originou. Se o cadastro falhar depois de enfileirar as boas-vindas, o e-mail desfaz junto, em vez
de o usuário receber boas-vindas de uma conta que não existe.

Nenhum service de domínio usa `IEmailSender` diretamente. Prioridade `Alta` para o que o usuário
está esperando na tela (redefinição de senha); `Normal` para o resto.

---

## Guardando um arquivo

No controller, o arquivo chega como `IFormFile` e o fluxo é repassado sem passar por memória:

```csharp
await using var conteudo = arquivo.OpenReadStream();

var resultado = await arquivoService.Enviar(
    new NovoArquivo(arquivo.FileName, arquivo.ContentType, arquivo.Length, conteudo, categoria),
    usuarioAtual.Id,
    ct
);
```

De dentro de um service — um relatório gerado, um anexo montado pela aplicação:

```csharp
await using var conteudo = new MemoryStream(bytes);

var resultado = await arquivoService.Enviar(
    new NovoArquivo("relatorio.pdf", "application/pdf", bytes.Length, conteudo, "relatorios"),
    usuarioId,
    ct
);
```

Quem abre o fluxo é quem o descarta — o service lê e devolve, não assume a posse.

**Nunca monte a chave do objeto.** Ela é gerada pelo service a partir do identificador da
entidade; o nome enviado pelo usuário só sobrevive como metadado.

Para um provedor novo (Azure Blob, GCS), implemente `IArmazenamentoDeArquivos` e troque o registro
em `DependenciasBusiness.AdicionarArmazenamento`. Traduza o "não encontrado" do provedor para
`FileNotFoundException` — é o que o service espera, sem saber qual provedor está ativo.

### Categorias

Minúsculas, números e hífen: `avatares`, `anexos-pedido`, `importacoes`, `relatorios`. Elas
compõem a chave e viram diretório no provedor, então trate como vocabulário estável — renomear
não move os arquivos já gravados.

---

## Registrando um evento

```csharp
[RegistrarEvento("produto.criado")]
[HttpPost]
public async Task<IActionResult> Criar(...)

[RegistrarEvento("produto.excluido", CamposDaRota = ["id"])]
[HttpDelete("{id:guid}")]
public async Task<IActionResult> Excluir(Guid id, ...)
```

Só grava em resposta 2xx. Nome no formato `recurso.acao`, e **estável**: renomear parte a série
histórica em duas.

De dentro de um service, quando o evento não corresponde a um endpoint:

```csharp
registradorDeEventos.Registrar(new Evento { Nome = "assinatura.renovada", UsuarioId = id });
```

---

## Injeção de dependência

Construtor primário, sempre:

```csharp
public sealed class ProdutoService(IProdutoRepository repositorio, IUnitOfWork unitOfWork) : IProdutoService
```

Onde ele não cabe — quando é preciso derivar algo no construtor — campo `readonly` com `_prefixo`.

Cada camada registra o que expõe:

| O quê | Onde |
|---|---|
| Service de negócio | `DependenciasBusiness.AdicionarServices` |
| Repositório | `DependenciasData.AdicionarRepositorios` |
| Configuração (`IOptions`) | `DependenciasBusiness.AddBusiness` |
| Serviço da borda HTTP | `ApiConfig` |
| Job | `Program.cs` do worker |
| Validator, `IRegister` do Mapster | nada — varredura de assembly |

Não existe arquivo central de centenas de linhas que toda feature nova precise editar — e que
vira conflito de merge sempre que duas pessoas criam uma feature na mesma semana.

---

## Datas

Armazene em UTC. Converta para exibição **só na borda**:

```csharp
DataUtils.ParaExibicao(usuario.CriadoEm);
DataUtils.ParaUtc(dataInformadaPeloUsuario);
DataUtils.InicioDoDiaEmUtc(dia);   // limite inferior de um filtro por dia
DataUtils.FimDoDiaEmUtc(dia);      // limite superior, exclusivo
```

Nunca `DateTime.Now`: em container o fuso do sistema é UTC e o valor sai errado sem avisar.

---

## Comentários

Documentação em bloco `/// <summary>` acima do membro, com `<remarks>` para o "por quê",
`<param>` e `<returns>`. **Sem `//` dentro de corpo de método** — se um trecho precisa de
explicação, ela vai no `<remarks>` do membro ou o trecho vira um método com nome.

Exceção: `// Arrange` / `// Act` / `// Assert` em teste.
