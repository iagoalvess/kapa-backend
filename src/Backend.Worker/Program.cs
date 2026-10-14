using Backend.Worker.Comandos;
using Backend.Worker.Configuration;

var comandoDePrivacidade = args.FirstOrDefault() == "privacidade";
var builder = Host.CreateApplicationBuilder(comandoDePrivacidade ? [] : args);

if (!comandoDePrivacidade)
    builder.AddObservabilidade("backend-worker");
builder.AddWorker();

using var host = builder.Build();

if (comandoDePrivacidade)
{
    try
    {
        return await ComandoDePrivacidade.Executar(args, host.Services, Console.Out);
    }
    catch (Exception excecao)
    {
        // Não imprimir a exceção: erros de conexão podem carregar segredos da configuração.
        Console.Error.WriteLine($"Atendimento interrompido ({excecao.GetType().Name}). Confira a configuração e o armazenamento antes de repetir.");
        return 1;
    }
}

await host.RunAsync();
return 0;
