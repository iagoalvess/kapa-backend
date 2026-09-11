using Backend.Worker.Configuration;

var builder = Host.CreateApplicationBuilder(args);

builder.AddObservabilidade("backend-worker");
builder.AddWorker();

var host = builder.Build();

await host.RunAsync();
