using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Backend.IntegrationTests.Infra;

/// <summary>
/// Fixa o IP remoto antes do pipeline da aplicação, como o proxy reverso faria.
/// </summary>
/// <remarks>
/// O <c>TestServer</c> não preenche o endereço remoto, e IP é parte da prova de todo aceite
/// (consentimento, adesão). Use com <c>WithWebHostBuilder</c> + <c>ConfigureTestServices</c>.
/// </remarks>
/// <param name="ip">IP a atribuir a toda requisição.</param>
public sealed class IpFixo(string ip) : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use(
                (contexto, seguir) =>
                {
                    contexto.Connection.RemoteIpAddress = IPAddress.Parse(ip);
                    return seguir(contexto);
                }
            );
            next(app);
        };
}
