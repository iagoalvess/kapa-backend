# Criando uma feature

Exemplo: um cadastro de **Produto**, com listagem paginada, detalhe e criação.

A ordem importa — de dentro para fora. Começar pelo controller leva a modelar a regra a partir
do formato do JSON, e o JSON é a parte que mais muda.

---

## 1. Entidade e modelos de leitura

`src/Backend.Business/Produtos/Models/Produto.cs`

```csharp
namespace Backend.Business.Produtos.Models;

/// <summary>Produto disponível para venda.</summary>
public class Produto : Entity
{
    public string Nome { get; set; } = string.Empty;
    public decimal Preco { get; set; }
    public bool Ativo { get; set; } = true;
}
```

`Models/ProdutoLeitura.cs` — o que a API devolve, projetado direto no `SELECT`:

```csharp
public sealed record ProdutoResumo(Guid Id, string Nome, decimal Preco, bool Ativo);

public sealed record ProdutoDetalhe(Guid Id, string Nome, decimal Preco, bool Ativo, DateTime CriadoEm);

public sealed record CriarProduto(string Nome, decimal Preco);
```

> Entidade herda de `Entity` (id UUIDv7 + datas de auditoria). Modelo de leitura é `record`.

> **A entidade pertence a uma formatura?** Então herda de `EntidadeDaFormatura`, não de `Entity`.
> É a única coisa a fazer: o `AppDbContext` aplica o filtro global, cria o índice de
> `FormaturaId` e carimba a coluna na gravação, sem nenhuma linha por entidade. Herdar de
> `Entity` por engano não quebra nada no build — só faz a turma A enxergar o dado da turma B.
>
> Ficam fora da convenção o que é global (`Usuario`, `Arquivo`, `Evento`, `EmailNaFila`) e o que
> precisa ser lido antes de haver formatura selecionada (`Formatura`, `VinculoDeFormatura`).

---

## 2. Interfaces

`Produtos/Interfaces/IProdutoRepository.cs`

```csharp
public interface IProdutoRepository
{
    Task<PaginaDe<ProdutoResumo>> Listar(PaginacaoRequest paginacao, string? busca, CancellationToken ct = default);
    Task<ProdutoDetalhe?> ObterDetalhe(Guid id, CancellationToken ct = default);
    Task<bool> ExisteComNome(string nome, CancellationToken ct = default);
    Task Adicionar(Produto produto, CancellationToken ct = default);
}
```

`Produtos/Interfaces/IProdutoService.cs`

```csharp
public interface IProdutoService
{
    Task<Result<PaginaDe<ProdutoResumo>>> Listar(PaginacaoRequest paginacao, string? busca, CancellationToken ct = default);
    Task<Result<ProdutoDetalhe>> ObterPorId(Guid id, CancellationToken ct = default);
    Task<Result<ProdutoDetalhe>> Criar(CriarProduto dados, CancellationToken ct = default);
}
```

Duas regras que o template não abre mão:

- **O repositório é declarado aqui, em `Business`.** A implementação fica em `Data`. É isso
  que deixa a regra testável sem banco.
- **Nenhum método do repositório persiste.** Quem chama `SalvarAsync` é o service.

---

## 3. Validator

`Produtos/Validators/CriarProdutoValidator.cs`

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

Validator cuida de **forma**. "Já existe produto com este nome" depende do banco e é do service.

Registro: nenhum. `AddValidatorsFromAssembly` varre o assembly de `Business`.

---

## 4. Service

`Produtos/Services/ProdutoService.cs`

```csharp
public sealed class ProdutoService(
    IProdutoRepository produtoRepository,
    IValidator<CriarProduto> criarValidator,
    IUnitOfWork unitOfWork
) : IProdutoService
{
    public async Task<Result<ProdutoDetalhe>> Criar(CriarProduto dados, CancellationToken ct = default)
    {
        var validacao = criarValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ProdutoDetalhe>(validacao.Erros);

        if (await produtoRepository.ExisteComNome(dados.Nome, ct))
            return Erro.Conflito("produto.nome_em_uso", "Já existe um produto com este nome.");

        var produto = new Produto { Nome = dados.Nome.Trim(), Preco = dados.Preco };

        await produtoRepository.Adicionar(produto, ct);
        await unitOfWork.SalvarAsync(ct);

        return await ObterPorId(produto.Id, ct);
    }
}
```

O padrão do corpo é sempre o mesmo: **validar → checar estado → alterar → salvar → devolver**.

Registre em `DependenciasBusiness.AdicionarServices`:

```csharp
services.AddScoped<IProdutoService, ProdutoService>();
```

---

## 5. Mapeamento e repositório

`src/Backend.Data/Mappings/ProdutoMapping.cs`

```csharp
public sealed class ProdutoMapping : IEntityTypeConfiguration<Produto>
{
    public void Configure(EntityTypeBuilder<Produto> builder)
    {
        builder.ToTable("produtos");
        builder.Property(p => p.Nome).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Preco).HasPrecision(18, 2);
        builder.HasIndex(p => p.Nome).IsUnique();
    }
}
```

> `HasPrecision` em dinheiro não é detalhe: sem ele o EF escolhe um tipo e o centavo some
> silenciosamente no arredondamento.

`src/Backend.Data/Repositories/ProdutoRepository.cs` — consultas com `AsNoTracking` e projeção,
`Adicionar` só marcando (sem `SaveChanges`). Ordene sempre com um critério de desempate único,
senão a paginação repete e some itens entre páginas.

Registre em `DependenciasData.AdicionarRepositorios`:

```csharp
services.AddScoped<IProdutoRepository, ProdutoRepository>();
```

Migration:

```bash
dotnet ef migrations add AdicionaProdutos --project src/Backend.Data --startup-project src/Backend.Api
```

---

## 6. DTOs e controller

`src/Backend.Api/DTOs/Produtos/ProdutoDTOs.cs` — sem atributos de validação: a regra já vive
no validator.

`src/Backend.Api/Controllers/V1/Produtos/ProdutoController.cs`

```csharp
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/produtos")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class ProdutoController(IProdutoService produtoService) : MainController
{
    public const string RotaDeDetalhe = "ProdutoPorId";

    [HttpGet("{id:guid}", Name = RotaDeDetalhe)]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken ct)
    {
        var resultado = await produtoService.ObterPorId(id, ct);

        return Responder(resultado.Map(produto => produto.Adapt<ProdutoDetalheDTO>()));
    }

    [HttpPost]
    [Authorize(Policy = Politicas.SomenteAdministrador)]
    public async Task<IActionResult> Criar([FromBody] CriarProdutoRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await produtoService.Criar(new CriarProduto(requisicao.Nome, requisicao.Preco), ct);

        return Criado(resultado.Map(p => p.Adapt<ProdutoDetalheDTO>()), RotaDeDetalhe, new { id = resultado.Sucesso ? resultado.Valor.Id : Guid.Empty });
    }
}
```

O controller mapeia, chama e responde. Sem `if` de regra, sem `try/catch`.

Se algum campo do DTO não casar por nome com o modelo, acrescente um `IRegister` em
`Api/Mapping/`. Só isso — o que casa por nome não precisa de configuração.

---

## 7. Testes

**Unitário** (`tests/Backend.UnitTests/Produtos/ProdutoServiceTests.cs`) — repositório
substituído, cobrindo a regra: nome duplicado devolve conflito, preço zero devolve validação,
e o caminho feliz chama `SalvarAsync` uma vez.

**Integração** (`tests/Backend.IntegrationTests/Produtos/`) — API e banco reais, cobrindo o que
teste unitário não vê: **quem pode chamar cada endpoint**. Política mal declarada não quebra o
build; só aparece como endpoint aberto em produção.

---

## Checklist

- [ ] Entidade herda de `Entity` — ou de `EntidadeDaFormatura`, se pertencer a uma formatura
- [ ] Nenhum service atribui `FormaturaId`; quem carimba é o `AppDbContext`
- [ ] `IgnoreQueryFilters()` só em método com sufixo `DeTodasAsFormaturas`
- [ ] Endpoint de domínio com `[Authorize(Policy = Politicas.FormaturaSelecionada)]`
- [ ] Modelos de leitura são `record`
- [ ] Repositório declarado em `Business/`, implementado em `Data/`
- [ ] Nenhum `SaveChanges` no repositório — só `IUnitOfWork` no service
- [ ] Todo método público do service devolve `Result` / `Result<T>`
- [ ] Erros com código estável (`produto.nome_em_uso`), não só mensagem
- [ ] Service registrado em `DependenciasBusiness`, repositório em `DependenciasData`
- [ ] Controller com `[ApiVersion]`, rota versionada e política de autorização explícita
- [ ] DTO sem atributo de validação
- [ ] Migration gerada e revisada antes do commit
- [ ] Consulta paginada ordenada com desempate único
- [ ] Teste de integração cobrindo **quem pode** chamar cada endpoint
