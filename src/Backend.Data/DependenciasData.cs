using Backend.Business.Abstractions;
using Backend.Business.Admin.Interfaces;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Usuarios.Interfaces;
using Backend.Data.Context;
using Backend.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        services.AddFormaturaAtualPadrao();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services.AdicionarRepositorios();
    }

    /// <summary>
    /// Registra o contexto de formatura padrão: nenhuma selecionada.
    /// </summary>
    /// <remarks>
    /// Serve onde não há requisição HTTP: o <c>Backend.Worker</c>, a CLI do EF Core e as
    /// migrações em tempo de projeto. Sem ele, os três falhariam ao resolver o
    /// <c>AppDbContext</c>. A Api troca este registro pela implementação que lê a claim do token
    /// (<c>Replace</c> em <c>ApiConfig</c>), então a ordem entre <c>AddData</c> e <c>AddApi</c>
    /// não importa.
    /// </remarks>
    /// <param name="services">Coleção de serviços.</param>
    private static IServiceCollection AddFormaturaAtualPadrao(this IServiceCollection services)
    {
        services.TryAddScoped<IFormaturaAtual, SemFormaturaSelecionada>();

        return services;
    }

    private static IServiceCollection AdicionarRepositorios(this IServiceCollection services)
    {
        services.AddScoped<IUsuarioRepository, UsuarioRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAdminRepository, AdminRepository>();
        services.AddScoped<IEmailFilaRepository, EmailFilaRepository>();
        services.AddScoped<IEventoRepository, EventoRepository>();
        services.AddScoped<IArquivoRepository, ArquivoRepository>();
        services.AddScoped<IVinculoRepository, VinculoRepository>();

        return services;
    }
}
