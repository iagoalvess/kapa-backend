using Backend.Business;
using Backend.Business.Usuarios.Models;
using Backend.Data;
using Backend.Data.Context;
using Backend.Worker.Jobs;
using Microsoft.AspNetCore.Identity;

namespace Backend.Worker.Configuration;

/// <summary>
/// Registro de tudo o que o worker precisa.
/// </summary>
/// <remarks>
/// Extraído do <c>Program.cs</c> para poder ser exercitado por teste. Um
/// <c>BackgroundService</c> é singleton e não pode consumir serviço <c>scoped</c>; a validação de
/// escopo só reprova isso ao construir o provedor, e o worker não tem requisição HTTP para um
/// teste de integração exercitar. Sem este ponto de entrada, o erro só apareceria como container
/// em loop de reinício.
/// </remarks>
public static class DependenciasWorker
{
    /// <summary>Registra acesso a dados, regras de negócio, Identity e os jobs.</summary>
    /// <param name="builder">Builder do host.</param>
    public static IHostApplicationBuilder AddWorker(this IHostApplicationBuilder builder)
    {
        builder.Services.AddData(builder.Configuration);
        builder.Services.AddBusiness(builder.Configuration);

        builder.Services.AddIdentityCore<Usuario>().AddRoles<Perfil>().AddEntityFrameworkStores<AppDbContext>();

        return builder.AdicionarJobs();
    }

    /// <summary>Registra os serviços hospedados. Ao criar um job, acrescente-o aqui.</summary>
    /// <param name="builder">Builder do host.</param>
    private static IHostApplicationBuilder AdicionarJobs(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHostedService<EnvioDeEmailJob>();
        builder.Services.AddHostedService<LimpezaRefreshTokensJob>();
        builder.Services.AddHostedService<RetencaoDeEventosJob>();
        builder.Services.AddHostedService<ConciliacaoDeAssinaturasJob>();

        return builder;
    }
}
