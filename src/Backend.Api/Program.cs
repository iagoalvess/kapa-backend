using Backend.Api.Configuration;
using Backend.Business;
using Backend.Data;
using Backend.Data.Seed;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservabilidade("backend-api");

builder.Services.AddData(builder.Configuration);
builder.Services.AddBusiness(builder.Configuration);
builder.Services.AddAuth(builder.Configuration);
builder.Services.AddRateLimit(builder.Configuration);
builder.Services.AddApi(builder.Configuration);
builder.Services.AddDocumentacao();

var app = builder.Build();

// O bootstrap não sobe o servidor: cria o primeiro administrador e sai. Fica antes das recusas de
// produção de propósito — é o caminho que a recusa do seed em produção deixa em aberto (ver
// BootstrapDoAdministrador).
if (BootstrapDoAdministrador.EhPedido(args))
{
    using var escopo = app.Services.CreateScope();
    Environment.ExitCode = await BootstrapDoAdministrador.ExecutarAsync(escopo.ServiceProvider);
    return;
}

app.AvisarConfiguracaoDeProducao();
app.UseApi();
app.UseDocumentacao();

app.MapHealthChecks("/health").AllowAnonymous().RequireRateLimiting(RateLimitConfig.Saude);

app.RecusarConfiguracaoInseguraDeProducao();

if (app.Configuration.GetValue<bool>("Seed:AoIniciar"))
{
    using var escopo = app.Services.CreateScope();
    await SeedInicial.AplicarAsync(escopo.ServiceProvider);
    await SeedDePlanos.AplicarAsync(escopo.ServiceProvider);
}

await app.RunAsync();

/// <summary>
/// Exposta para que os testes de integração possam construir o host com
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
/// <remarks>
/// Migration **não** roda aqui. Com duas réplicas subindo juntas, as duas tentariam migrar e uma
/// quebraria; e uma migration destrutiva num container em loop de reinício aplica o estrago
/// sozinha. O passo é <c>dotnet ef database update</c> no deploy — ver <c>docs/operacao.md</c>.
/// </remarks>
public partial class Program;
