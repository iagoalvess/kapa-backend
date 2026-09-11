using System.Globalization;
using Backend.Business.Abstractions;
using Backend.Data.Context;
using Backend.Data.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace Backend.IntegrationTests.Infra;

/// <summary>
/// Sobe a API de verdade contra um PostgreSQL de verdade, em container descartável.
/// </summary>
/// <remarks>
/// <b>Requer Docker em execução.</b> É o preço de o teste dizer a verdade: o provider
/// <c>EF Core InMemory</c> — a alternativa sem Docker — não aplica <c>UNIQUE</c>, não aplica
/// chave estrangeira e não executa SQL. Teste passa e produção quebra, que é o pior resultado
/// possível para uma suíte de testes.
/// <para>
/// O container morre com a suíte, e cada execução parte de um banco recém-migrado — sem estado
/// de execução anterior e sem "roda na minha máquina".
/// </para>
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Credenciais do administrador criado pelo seed, usadas para autenticar nos testes.</summary>
    public const string AdminEmail = "admin@testes.local";

    /// <summary>Senha do administrador de teste.</summary>
    public const string AdminSenha = "Admin@Testes123";

    /// <summary>Quantidade de arquivos por usuário nos testes, baixa para a cota ser alcançável.</summary>
    public const int LimiteDeArquivos = 3;

    private readonly string _diretorioDeArquivos = Path.Combine(Path.GetTempPath(), $"backend-arquivos-{Guid.CreateVersion7():N}");

    private DbContextOptions<AppDbContext> _opcoes = null!;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("backend_testes")
        .WithUsername("testes")
        .WithPassword("testes")
        .Build();

    /// <summary>
    /// Sobe o banco, aplica as migrations e cria os dados iniciais.
    /// </summary>
    /// <remarks>
    /// O cliente fala <c>https</c> porque o cookie de sessão é <c>Secure</c>. O
    /// <c>CookieContainer</c> do <c>HttpClient</c> não tem a exceção que os navegadores fazem
    /// para <c>localhost</c>: em <c>http</c> ele descarta o cookie silenciosamente, e todo teste
    /// de renovação falharia sem nenhuma pista do motivo. O <c>TestServer</c> não faz TLS de
    /// verdade — só passa a enxergar o esquema como seguro.
    /// </remarks>
    public async ValueTask InitializeAsync()
    {
        ClientOptions.BaseAddress = new Uri("https://localhost");

        await _postgres.StartAsync();

        using var escopo = Services.CreateScope();

        _opcoes = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;

        await escopo.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        await SeedInicial.AplicarAsync(escopo.ServiceProvider);
    }

    /// <summary>
    /// Abre um contexto de dados enxergando a formatura informada.
    /// </summary>
    /// <remarks>
    /// A formatura é passada no construtor, e não obtida de um token: a semeadura do teste
    /// precisa gravar e consultar linhas isoladas <b>sem</b> requisição HTTP em curso. Passar
    /// <c>null</c> devolve um contexto sem formatura selecionada — o estado do worker e da CLI
    /// do EF Core, em que o filtro global não casa com linha nenhuma.
    /// <para>
    /// As opções são montadas aqui, e não tomadas emprestadas do container da aplicação: as do
    /// container carregam uma referência ao provedor do escopo que as resolveu, e usá-las depois
    /// que aquele escopo fecha estoura <c>ObjectDisposedException</c>.
    /// </para>
    /// </remarks>
    /// <param name="formaturaId">Formatura que o contexto vai enxergar, ou nulo para nenhuma.</param>
    public AppDbContext ContextoDe(Guid? formaturaId) => new(_opcoes, new FormaturaFixa(formaturaId));

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");

    /// <summary>
    /// Injeta a configuração do teste **antes** de o host da aplicação ser construído.
    /// </summary>
    /// <remarks>
    /// Tem que ser <c>ConfigureHostConfiguration</c>, e não <c>ConfigureAppConfiguration</c>:
    /// no modelo de hospedagem mínima, o <c>Program.cs</c> lê <c>builder.Configuration</c>
    /// ainda durante a construção — antes de a configuração de aplicação do teste ser aplicada.
    /// Com <c>ConfigureAppConfiguration</c>, a string de conexão chega tarde demais e o host
    /// morre com <c>ArgumentNullException</c> no primeiro serviço que a exige.
    /// </remarks>
    /// <param name="builder">Builder do host.</param>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuracao =>
            configuracao.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                    ["Jwt:Emissor"] = "backend-testes",
                    ["Jwt:Audiencia"] = "clientes-testes",
                    ["Jwt:ChaveSecreta"] = "chave-de-teste-com-mais-de-32-caracteres-ok",
                    ["Jwt:MinutosDeValidadeDoAccessToken"] = "15",
                    ["Jwt:DiasDeValidadeDoRefreshToken"] = "7",
                    ["RateLimit:PadraoPorMinuto"] = "100000",
                    ["RateLimit:AutenticacaoPorMinuto"] = "100000",
                    ["Armazenamento:Provedor"] = "Local",
                    ["Armazenamento:CaminhoLocal"] = _diretorioDeArquivos,
                    // Baixo de propósito: a cota precisa ser alcançável em teste sem subir
                    // duzentos arquivos. Cada teste registra um usuário novo, então o limite
                    // por usuário não atrapalha quem só envia um.
                    ["Armazenamento:MaximoDeArquivosPorUsuario"] = LimiteDeArquivos.ToString(CultureInfo.InvariantCulture),
                    ["Seed:AoIniciar"] = "false",
                    ["Seed:AdminEmail"] = AdminEmail,
                    ["Seed:AdminSenha"] = AdminSenha,
                }
            )
        );

        return base.CreateHost(builder);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();

        if (Directory.Exists(_diretorioDeArquivos))
            Directory.Delete(_diretorioDeArquivos, recursive: true);
    }
}

/// <summary>
/// Formatura selecionada por decisão do teste, em vez de vir de uma claim.
/// </summary>
/// <param name="id">Formatura a enxergar, ou nulo para nenhuma.</param>
public sealed class FormaturaFixa(Guid? id) : IFormaturaAtual
{
    /// <inheritdoc />
    public Guid? Id => id;
}

/// <summary>
/// Compartilha uma única API e um único container entre todas as classes de teste.
/// </summary>
/// <remarks>
/// Sem isto, cada classe subiria o próprio Postgres — a suíte passaria de segundos a minutos.
/// </remarks>
[CollectionDefinition(Nome)]
public sealed class ColecaoDeApi : ICollectionFixture<ApiFactory>
{
    /// <summary>Nome da coleção, usado em <c>[Collection]</c>.</summary>
    public const string Nome = "api";
}
