using Backend.Api.Configuration;
using Backend.Business;
using Backend.Business.Assinaturas.Settings;
using Backend.Data;
using Backend.Data.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservabilidade("backend-api");

builder.Services.AddData(builder.Configuration);
builder.Services.AddBusiness(builder.Configuration);
builder.Services.AddAuth(builder.Configuration);
builder.Services.AddRateLimit(builder.Configuration);
builder.Services.AddApi(builder.Configuration);
builder.Services.AddDocumentacao();

var app = builder.Build();

app.AvisarConfiguracaoDeProducao();
app.UseApi();
app.UseDocumentacao();

app.MapHealthChecks("/health").AllowAnonymous();

// Seed em produção é a conta de administrador com senha de arquivo de configuração — e é assim
// que um produto estreia com credencial conhecida. A conta inicial de produção é criada uma vez,
// à mão (checklist da Sprint 16). Recusar na subida, e não ignorar em silêncio, é de propósito:
// quem deixou a chave ligada precisa descobrir no deploy, não seis meses depois.
if (app.Environment.IsProduction() && app.Configuration.GetValue<bool>("Seed:AoIniciar"))
{
    throw new InvalidOperationException(
        "'Seed:AoIniciar' não pode ficar ligado em produção: a conta inicial é criada manualmente. Ver docs/operacao.md."
    );
}

// O provedor fake ativa qualquer turma sem cobrar nada: em produção ele é um buraco, não um modo de teste (Sprint 37).
if (app.Environment.IsProduction() && app.Configuration.GetValue("Assinaturas:Provedor", EProvedorDeAssinatura.Fake) == EProvedorDeAssinatura.Fake)
{
    throw new InvalidOperationException(
        "'Assinaturas:Provedor' não pode ser Fake em produção: configure MercadoPago e o token da conta do Kapa. Ver docs/deploy.md."
    );
}

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
