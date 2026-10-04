using System.Globalization;
using Backend.Business;
using Backend.Business.Abstractions;
using Backend.Data;
using Backend.Data.Context;
using Backend.Data.Criptografia;
using Backend.Data.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    /// <summary>Segredo do HMAC do webhook de assinatura nos testes.</summary>
    public const string SegredoDoWebhook = "segredo-do-webhook-de-teste";

    /// <summary>Segredo do HMAC do convite da festa — fixo, como o de produção: o convite sobrevive ao reinício.</summary>
    public const string SegredoDoConvite = "ZmVzdGEtZGUtdGVzdGUtY29tLTMyLWJ5dGVzLW91LW1haXM=";

    /// <summary>Chave AES dos testes, para conferir a cifra decifrando direto da coluna.</summary>
    public const string ChaveDeDados = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    /// <summary>Quantidade de arquivos por usuário nos testes, baixa para a cota ser alcançável.</summary>
    public const int LimiteDeArquivos = 3;

    /// <summary>Contador de comandos SQL, para a guarda de N+1 medir uma requisição.</summary>
    public ContadorDeComandos Contador { get; } = new();

    private readonly string _diretorioDeArquivos = Path.Combine(Path.GetTempPath(), $"backend-arquivos-{Guid.CreateVersion7():N}");

    private DbContextOptions<AppDbContext> _opcoes = null!;

    private ServiceProvider _worker = null!;

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
        await SeedDePlanos.AplicarAsync(escopo.ServiceProvider);

        _worker = ConstruirWorker();
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
    public AppDbContext ContextoDe(Guid? formaturaId) => new(_opcoes, new FormaturaFixa(formaturaId), Services.GetRequiredService<CifraDeCampo>());

    /// <summary>
    /// O assunto e o corpo do e-mail mais recente da fila, concatenados — a caixa de entrada do teste.
    /// </summary>
    /// <remarks>
    /// O worker não roda nos testes: o e-mail fica na fila, que é onde o teste o lê. Serve a quem
    /// precisa de um valor que só existe dentro da mensagem, como o código de seis dígitos da adesão.
    /// <para>
    /// Sem filtro por destinatário porque os testes desta coleção rodam em série: o último da fila é
    /// o que o teste corrente acabou de provocar.
    /// </para>
    /// </remarks>
    public async Task<string?> UltimoEmailDaFila(CancellationToken ct)
    {
        await using var contexto = ContextoDe(null);

        return await contexto.EmailsFila.OrderByDescending(e => e.CriadoEm).Select(e => $"{e.Assunto}\n{e.CorpoHtml}").FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A rajada da entrada vai por <c>UseSetting</c>, e não pelo dicionário de <see cref="CreateHost"/>:
    /// lá ela ganharia do <c>UseSetting</c> de um host apertado, e o teste do limite não conseguiria
    /// baixá-la. Aqui, o <c>WithWebHostBuilder</c> do teste roda depois e sobrescreve.
    /// <para>
    /// O <see cref="Contador"/> é preso às opções do <c>AppDbContext</c>: o EF Core não resolve
    /// <c>IInterceptor</c> do provedor com a configuração do projeto, então o host de teste troca o
    /// registro do contexto por um idêntico <b>com</b> o interceptor. A connection string é a mesma.
    /// </para>
    /// </remarks>
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder
            .UseEnvironment("Testing")
            .UseSetting("RateLimit:EntradaRajada", "100000")
            .UseSetting("RateLimit:IngressoRajada", "100000")
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<AppDbContext>();
                services.RemoveAll<DbContextOptions<AppDbContext>>();

                services.AddDbContext<AppDbContext>(opcoes =>
                    opcoes
                        .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3))
                        .UseSnakeCaseNamingConvention()
                        .AddInterceptors(Contador)
                );
            });

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
                    ["Criptografia:ChaveDeDados"] = ChaveDeDados,
                    ["Festa:SegredoDoConvite"] = SegredoDoConvite,
                    ["RateLimit:PadraoPorMinuto"] = "100000",
                    ["RateLimit:AutenticacaoPorMinuto"] = "100000",
                    ["RateLimit:CodigoPorMinuto"] = "100000",
                    ["Armazenamento:Provedor"] = "Local",
                    ["Armazenamento:CaminhoLocal"] = _diretorioDeArquivos,
                    // Baixo de propósito: a cota precisa ser alcançável em teste sem subir
                    // duzentos arquivos. Cada teste registra um usuário novo, então o limite
                    // por usuário não atrapalha quem só envia um.
                    ["Armazenamento:MaximoDeArquivosPorUsuario"] = LimiteDeArquivos.ToString(CultureInfo.InvariantCulture),
                    ["Assinaturas:SegredoDoWebhook"] = SegredoDoWebhook,
                    ["Seed:AoIniciar"] = "false",
                    ["Seed:AdminEmail"] = AdminEmail,
                    ["Seed:AdminSenha"] = AdminSenha,
                }
            )
        );

        return base.CreateHost(builder);
    }

    /// <summary>
    /// Um escopo igual ao do worker, apontado para uma formatura.
    /// </summary>
    /// <remarks>
    /// O <c>IFormaturaAtual</c> da API lê a claim do token, e um job não tem requisição HTTP: sem
    /// este container, um teste de régua resolveria o service com a formatura nula e o filtro global
    /// não casaria com linha nenhuma. A composição é a mesma de <c>DependenciasWorker</c> —
    /// <c>AddData</c> + <c>AddBusiness</c> com <c>FormaturaDoProcessamento</c> no lugar da claim.
    /// </remarks>
    /// <param name="formaturaId">Formatura a processar.</param>
    public IServiceScope EscopoDoWorker(Guid formaturaId)
    {
        var escopo = _worker.CreateScope();

        escopo.ServiceProvider.GetRequiredService<FormaturaDoProcessamento>().Apontar(formaturaId);

        return escopo;
    }

    private ServiceProvider ConstruirWorker()
    {
        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                    ["Criptografia:ChaveDeDados"] = ChaveDeDados,
                    ["Festa:SegredoDoConvite"] = SegredoDoConvite,
                    ["Jwt:ChaveSecreta"] = "chave-de-teste-com-mais-de-32-caracteres-ok",
                    ["Aplicacao:Nome"] = "Kapa",
                    ["Aplicacao:UrlDoFrontend"] = "https://kapa.testes",
                    ["Armazenamento:Provedor"] = "Local",
                    ["Armazenamento:CaminhoLocal"] = _diretorioDeArquivos,
                    ["ComunicacaoDoKapa:EnvioLigado"] = "true",
                }
            )
            .Build();

        var servicos = new ServiceCollection();
        servicos.AddLogging();
        servicos.AddData(configuracao).AddBusiness(configuracao);
        servicos.AddScoped<FormaturaDoProcessamento>();
        servicos.Replace(ServiceDescriptor.Scoped<IFormaturaAtual>(sp => sp.GetRequiredService<FormaturaDoProcessamento>()));

        return servicos.BuildServiceProvider();
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _worker.DisposeAsync();
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
