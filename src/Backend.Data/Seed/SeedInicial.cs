using Backend.Business.Usuarios.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Data.Seed;

/// <summary>
/// Dados mínimos para a aplicação funcionar: perfis e o primeiro administrador.
/// </summary>
/// <remarks>
/// Idempotente — roda a cada subida sem duplicar nada.
/// <para>
/// <b>Não existe usuário padrão embutido.</b> O administrador só é criado se
/// <c>Seed:AdminEmail</c> e <c>Seed:AdminSenha</c> estiverem configurados. Um template que
/// nasce com <c>admin@admin.com</c> / <c>Admin@123</c> chega em produção com essa conta viva,
/// e a credencial está publicada no repositório de quem copiou o template.
/// </para>
/// </remarks>
public static class SeedInicial
{
    /// <summary>Nome da seção de configuração do seed.</summary>
    public const string Secao = "Seed";

    /// <summary>Cria os perfis padrão e, se configurado, o administrador inicial.</summary>
    /// <param name="provider">Provedor de serviços com escopo já aberto.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public static async Task AplicarAsync(IServiceProvider provider, CancellationToken ct = default)
    {
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(SeedInicial));
        var roleManager = provider.GetRequiredService<RoleManager<Perfil>>();
        var userManager = provider.GetRequiredService<UserManager<Usuario>>();
        var configuration = provider.GetRequiredService<IConfiguration>();

        foreach (var nome in PerfisPadrao.Todos)
        {
            if (await roleManager.RoleExistsAsync(nome))
                continue;

            await roleManager.CreateAsync(new Perfil(nome));
            logger.LogInformation("Perfil {Perfil} criado.", nome);
        }

        ct.ThrowIfCancellationRequested();

        var email = configuration[$"{Secao}:AdminEmail"];
        var senha = configuration[$"{Secao}:AdminSenha"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(senha))
        {
            logger.LogInformation("Seed do administrador ignorado: {Secao}:AdminEmail/AdminSenha não configurados.", Secao);
            return;
        }

        if (await userManager.FindByEmailAsync(email) is not null)
            return;

        var admin = new Usuario
        {
            Nome = "Administrador",
            Email = email,
            UserName = email,
            EmailConfirmed = true,
        };

        var criacao = await userManager.CreateAsync(admin, senha);

        if (!criacao.Succeeded)
        {
            logger.LogError("Falha ao criar o administrador inicial: {Erros}", string.Join("; ", criacao.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(admin, PerfisPadrao.Administrador);
        logger.LogInformation("Administrador inicial criado.");
    }
}
