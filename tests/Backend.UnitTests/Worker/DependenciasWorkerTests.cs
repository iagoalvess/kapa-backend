using Backend.Worker.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Backend.UnitTests.Worker;

/// <summary>
/// Garante que o grafo de dependências do worker é válido.
/// </summary>
/// <remarks>
/// Um <c>BackgroundService</c> é singleton e **não pode** consumir serviço <c>scoped</c>. O
/// runtime só reprova isso ao construir o provedor, e o worker não atende HTTP — então não há
/// requisição para um teste de integração exercitar. Sem este teste, o erro só aparece como
/// container em loop de reinício, com a mensagem enterrada no log.
/// <para>
/// Nada de banco aqui: <c>ValidateOnBuild</c> resolve os construtores sem executar nada.
/// </para>
/// </remarks>
public sealed class DependenciasWorkerTests
{
    private static readonly Dictionary<string, string?> Configuracao = new(StringComparer.Ordinal)
    {
        ["ConnectionStrings:Postgres"] = "Host=localhost;Database=nao-usado;Username=x;Password=y",
        ["Jwt:Emissor"] = "backend-testes",
        ["Jwt:Audiencia"] = "clientes-testes",
        ["Jwt:ChaveSecreta"] = "chave-de-teste-com-mais-de-32-caracteres-ok",
    };

    private static ServiceProvider Construir(Dictionary<string, string?> configuracao)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(configuracao);
        builder.AddWorker();

        return builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void O_grafo_de_dependencias_do_worker_e_valido()
    {
        using var provider = Should.NotThrow(() => Construir(Configuracao));

        provider.ShouldNotBeNull();
    }

    [Fact]
    public void Todos_os_jobs_estao_registrados()
    {
        using var provider = Construir(Configuracao);

        provider.GetServices<IHostedService>().Count().ShouldBe(4);
    }

    /// <summary>
    /// Com S3 configurado o grafo muda — outro remetente de arquivos e um cliente da AWS a mais.
    /// A validação precisa valer para as duas configurações.
    /// </summary>
    [Fact]
    public void O_grafo_continua_valido_com_o_provedor_S3()
    {
        var comS3 = new Dictionary<string, string?>(Configuracao, StringComparer.Ordinal)
        {
            ["Armazenamento:Provedor"] = "S3",
            ["Armazenamento:S3:Bucket"] = "meu-bucket",
            ["Armazenamento:S3:Regiao"] = "sa-east-1",
        };

        using var provider = Should.NotThrow(() => Construir(comS3));

        provider.ShouldNotBeNull();
    }

    [Fact]
    public void O_grafo_continua_valido_com_SMTP_configurado()
    {
        var comSmtp = new Dictionary<string, string?>(Configuracao, StringComparer.Ordinal)
        {
            ["Smtp:Host"] = "smtp.exemplo.com",
            ["Smtp:RemetenteEmail"] = "nao-responda@exemplo.com",
        };

        using var provider = Should.NotThrow(() => Construir(comSmtp));

        provider.ShouldNotBeNull();
    }
}
