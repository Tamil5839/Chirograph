using Chirograph.Application;
using Chirograph.Infrastructure;
using Chirograph.Web;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddChirographApplication();
builder.Services.AddChirographInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddChirographWeb(builder.Configuration, builder.Environment);

var app = builder.Build();

if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ApplyMigrationsOnStartup)
    await app.Services.ApplyMigrationsAsync();

app.UseChirographWeb();
await app.RunAsync();

// Exposed so integration tests can host the app with WebApplicationFactory<Program>.
public partial class Program;
