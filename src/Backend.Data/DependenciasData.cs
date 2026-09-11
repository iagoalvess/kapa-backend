using Backend.Business.Abstractions;
using Backend.Business.Admin.Interfaces;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Usuarios.Interfaces;
using Backend.Data.Context;
using Backend.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Data;

/// <summary>
/// Registro do contexto, da unidade de trabalho e dos repositórios.
/// </summary>
/// <remarks>Ao criar um repositório, registre-o em <c>AdicionarRepositorios</c>.</remarks>
public static class DependenciasData
{
    /// <summary>Nome da string de conexão esperada na configuração.</summary>
    public const string NomeDaConexao = "Postgres";

    /// <summary>Registra o acesso a dados.</summary>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configuration">Configuração da aplicação.</param>
    /// <exception cref="InvalidOperationException">Se a string de conexão não estiver configurada.</exception>
    public static IServiceCollection AddData(this IServiceCollection services, IConfiguration configuration)
    {
        var conexao =
            configuration.GetConnectionString(NomeDaConexao)
            ?? throw new InvalidOperationException($"A string de conexão '{NomeDaConexao}' não está configurada.");

        services.AddDbContext<AppDbContext>(opcoes =>
            opcoes.UseNpgsql(conexao, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3)).UseSnakeCaseNamingConvention()
        );

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services.AdicionarRepositorios();
    }

    private static IServiceCollection AdicionarRepositorios(this IServiceCollection services)
    {
        services.AddScoped<IUsuarioRepository, UsuarioRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAdminRepository, AdminRepository>();
        services.AddScoped<IEmailFilaRepository, EmailFilaRepository>();
        services.AddScoped<IEventoRepository, EventoRepository>();
        services.AddScoped<IArquivoRepository, ArquivoRepository>();

        return services;
    }
}
