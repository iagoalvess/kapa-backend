namespace Backend.Api.Configuration;

/// <summary>
/// Configurações que sobem sem erro em produção mas deixam a plataforma fora do ar.
/// </summary>
/// <remarks>
/// Aviso, e não recusa na subida: sem proxy na frente, a lista vazia é o correto, e travar o deploy
/// seria decidir pelo operador. Mas fica no log de subida, em nível de erro, para ninguém descobrir
/// pelo incidente.
/// <para>
/// E-mail, armazenamento e pagamento ficam de fora de propósito: os fornecedores ainda não foram
/// contratados, e os provedores locais são o estado conhecido do desenvolvimento.
/// </para>
/// </remarks>
public static class AvisosDeProducao
{
    /// <summary>Registra no log o que está configurado de um jeito perigoso para produção.</summary>
    /// <param name="app">Aplicação web.</param>
    public static WebApplication AvisarConfiguracaoDeProducao(this WebApplication app)
    {
        if (!app.Environment.IsProduction())
            return app;

        if ((app.Configuration.GetSection(RedeConfig.Secao).Get<string[]>() ?? []).Length == 0)
            app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger(nameof(AvisosDeProducao))
                .LogError(
                    "'{Secao}' está vazio: atrás de um proxy, toda requisição parece vir do IP dele, e o limite de login vira 10 por minuto para a plataforma inteira.",
                    RedeConfig.Secao
                );

        return app;
    }
}
