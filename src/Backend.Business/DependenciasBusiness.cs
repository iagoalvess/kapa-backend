using Amazon;
using Amazon.S3;
using Backend.Business.Admin.Interfaces;
using Backend.Business.Admin.Services;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Services;
using Backend.Business.Arquivos.Settings;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Services;
using Backend.Business.Auth.Settings;
using Backend.Business.Common;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Services;
using Backend.Business.Emails.Settings;
using Backend.Business.Usuarios.Interfaces;
using Backend.Business.Usuarios.Services;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Business;

/// <summary>
/// Registro de tudo o que a camada de negócio expõe.
/// </summary>
/// <remarks>
/// Cada camada registra as próprias dependências. É o que evita o arquivo central de
/// centenas de linhas que toda feature nova precisa editar — e que vira conflito de merge
/// sempre que duas pessoas criam uma feature na mesma semana.
/// <para>Ao criar uma feature, adicione o service dela em <c>AdicionarServices</c>.</para>
/// </remarks>
public static class DependenciasBusiness
{
    /// <summary>Registra services, validadores e configurações da camada de negócio.</summary>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configuration">Configuração da aplicação.</param>
    public static IServiceCollection AddBusiness(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtSettings>().Bind(configuration.GetSection(JwtSettings.Secao)).ValidateDataAnnotations().ValidateOnStart();

        services.AddOptions<SmtpSettings>().Bind(configuration.GetSection(SmtpSettings.Secao));
        services.AddOptions<ContaSettings>().Bind(configuration.GetSection(ContaSettings.Secao));
        services.AddOptions<AplicacaoSettings>().Bind(configuration.GetSection(AplicacaoSettings.Secao));
        services.AddOptions<ArmazenamentoSettings>().Bind(configuration.GetSection(ArmazenamentoSettings.Secao));

        services.AddValidatorsFromAssembly(typeof(DependenciasBusiness).Assembly, ServiceLifetime.Singleton);

        return services.AdicionarRemetenteDeEmail(configuration).AdicionarArmazenamento(configuration).AdicionarServices();
    }

    /// <summary>
    /// Registra o remetente de e-mail conforme a configuração.
    /// </summary>
    /// <remarks>
    /// Sem <c>Smtp:Host</c>, entra o remetente que apenas registra a mensagem no log. É o que
    /// permite o projeto subir e o fluxo de e-mail funcionar de ponta a ponta sem um servidor
    /// SMTP à mão — em desenvolvimento e nos testes.
    /// <para>
    /// Singleton, e não scoped: o remetente não guarda estado por requisição, e quem o consome é
    /// um <c>BackgroundService</c> — que é singleton e não pode depender de serviço scoped.
    /// </para>
    /// </remarks>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configuration">Configuração da aplicação.</param>
    private static IServiceCollection AdicionarRemetenteDeEmail(this IServiceCollection services, IConfiguration configuration)
    {
        var smtp = configuration.GetSection(SmtpSettings.Secao).Get<SmtpSettings>();

        if (smtp?.Configurado == true)
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        else
            services.AddSingleton<IEmailSender, EmailSenderDeLog>();

        return services;
    }

    /// <summary>
    /// Registra o provedor de armazenamento conforme a configuração.
    /// </summary>
    /// <remarks>
    /// A escolha acontece uma vez, aqui. Nenhum outro ponto do projeto sabe qual provedor está
    /// ativo — todos falam com <see cref="IArmazenamentoDeArquivos"/>.
    /// <para>
    /// O cliente do S3 é montado à mão em vez de usar <c>AWSSDK.Extensions.NETCore.Setup</c>: são
    /// poucas linhas, e evita mais um pacote só para ler duas chaves de configuração. As
    /// credenciais continuam vindo da cadeia padrão do SDK.
    /// </para>
    /// </remarks>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configuration">Configuração da aplicação.</param>
    private static IServiceCollection AdicionarArmazenamento(this IServiceCollection services, IConfiguration configuration)
    {
        var armazenamento = configuration.GetSection(ArmazenamentoSettings.Secao).Get<ArmazenamentoSettings>() ?? new ArmazenamentoSettings();

        if (armazenamento.Provedor is not EProvedorDeArmazenamento.S3)
        {
            services.AddSingleton<IArmazenamentoDeArquivos, ArmazenamentoLocal>();

            return services;
        }

        services.AddSingleton<IAmazonS3>(_ => CriarClienteS3(armazenamento.S3));
        services.AddSingleton<IArmazenamentoDeArquivos, ArmazenamentoS3>();

        return services;
    }

    private static AmazonS3Client CriarClienteS3(S3Settings s3)
    {
        var configuracao = new AmazonS3Config();

        if (!string.IsNullOrWhiteSpace(s3.Regiao))
            configuracao.RegionEndpoint = RegionEndpoint.GetBySystemName(s3.Regiao);

        if (!string.IsNullOrWhiteSpace(s3.ServiceUrl))
        {
            configuracao.ServiceURL = s3.ServiceUrl;
            configuracao.ForcePathStyle = true;
        }

        return new AmazonS3Client(configuracao);
    }

    private static IServiceCollection AdicionarServices(this IServiceCollection services)
    {
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IContaService, ContaService>();
        services.AddScoped<IEmailsDeConta, EmailsDeConta>();
        services.AddScoped<IUsuarioService, UsuarioService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IArquivoService, ArquivoService>();

        return services;
    }
}
