using Backend.Business.Usuarios.Interfaces;
using Backend.Data.Seed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Api.Configuration;

/// <summary>
/// Cria o primeiro administrador em produção, uma vez, fora do caminho normal da API.
/// </summary>
/// <remarks>
/// A API se recusa a subir em produção com <c>Seed:AoIniciar</c> ligado (ver
/// <see cref="RecusasDeProducao"/>), e é de propósito: seed automático em produção é a conta de
/// administrador com senha de arquivo de configuração. Mas alguém precisa criar a primeira conta, e
/// não há como fazê-lo pela API — promover pela API exige um administrador existente.
/// <para>
/// Este comando fecha o ovo e a galinha sem reabrir o seed: roda o mesmo <see cref="SeedInicial"/>
/// (idempotente) num processo que <b>não sobe o servidor</b> e sai, disparado por
/// <c>dotnet Backend.Api.dll bootstrap-admin</c> — ou, no deploy, por um <c>docker run</c> da mesma
/// imagem com esse argumento. Lê <c>Seed:AdminEmail</c> e <c>Seed:AdminSenha</c>; <c>Seed:AoIniciar</c>
/// não é usado e não precisa ser ligado.
/// </para>
/// <para>
/// Recusa quando já existe administrador ativo: um segundo administrador se promove pela API
/// (<c>PUT /api/v1/usuarios/{id}/perfis</c>), com a trilha registrada.
/// </para>
/// </remarks>
public static class BootstrapDoAdministrador
{
    /// <summary>Argumento que dispara o comando em vez de subir a API.</summary>
    public const string Comando = "bootstrap-admin";

    /// <summary>Diz se a linha de comando pediu o bootstrap (aceita <c>bootstrap-admin</c> e <c>--bootstrap-admin</c>).</summary>
    /// <param name="args">Argumentos do processo.</param>
    public static bool EhPedido(string[] args) => args.Any(arg => string.Equals(arg.TrimStart('-'), Comando, StringComparison.OrdinalIgnoreCase));

    /// <summary>Cria o administrador inicial, se ainda não houver nenhum.</summary>
    /// <param name="provider">Provedor de serviços com escopo já aberto.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>0</c> em sucesso (inclusive quando já havia administrador); <c>1</c> quando falta credencial.</returns>
    public static async Task<int> ExecutarAsync(IServiceProvider provider, CancellationToken ct = default)
    {
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(BootstrapDoAdministrador));
        var configuracao = provider.GetRequiredService<IConfiguration>();

        var email = configuracao[$"{SeedInicial.Secao}:AdminEmail"];
        var senha = configuracao[$"{SeedInicial.Secao}:AdminSenha"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(senha))
        {
            logger.LogError(
                "Bootstrap recusado: configure {Secao}:AdminEmail e {Secao}:AdminSenha antes de rodar o comando.",
                SeedInicial.Secao,
                SeedInicial.Secao
            );
            return 1;
        }

        var usuarios = provider.GetRequiredService<IUsuarioRepository>();
        if (await usuarios.ContarAdministradoresAtivos(ct) > 0)
        {
            logger.LogInformation("Bootstrap ignorado: já existe administrador ativo. Promova outro pela API (PUT /api/v1/usuarios/{{id}}/perfis).");
            return 0;
        }

        await SeedInicial.AplicarAsync(provider, ct);
        return 0;
    }
}
