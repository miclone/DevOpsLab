using System.Reflection;
using TodoApi.Todos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITodoRepository, InMemoryTodoRepository>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();   // API description at /openapi/v1.json, Development only
}

// For load balancers, Docker and Kubernetes: "is this instance alive?"
app.MapHealthChecks("/health");

// "Which build is running, and where?" The version includes the git commit ID,
// which becomes very useful once the same code runs in several environments.
app.MapGet("/version", (IHostEnvironment env) => new
{
    Version = Assembly.GetEntryAssembly()?
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion ?? "unknown",
    Environment = env.EnvironmentName,
    Machine = System.Environment.MachineName,
});

app.MapTodoEndpoints();

app.Run();

// Makes Program visible to the test project, so WebApplicationFactory<Program>
// can start the whole API in memory for integration tests.
public partial class Program { }