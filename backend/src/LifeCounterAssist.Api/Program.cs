using Microsoft.AspNetCore.SignalR;
using LifeCounterAssist.Api.Endpoints;
using LifeCounterAssist.Api.Hubs;
using LifeCounterAssist.Api.Infrastructure;
using LifeCounterAssist.Api.Services;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Notifier>();
builder.Services.AddSingleton<IGameNotifier>(sp => sp.GetRequiredService<Notifier>());
builder.Services.AddSingleton<GameService>();
builder.Services.AddHostedService<GameTicker>();

// CORS is only needed for the Vite dev server; in production the API serves the front itself.
var allowedOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var useCors = allowedOrigins.Length > 0;
if (useCors)
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
}

builder.Services.ConfigureHttpJsonOptions(options => JsonDefaults.Configure(options.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddSignalR(options => options.AddFilter<DomainExceptionHubFilter>())
    .AddJsonProtocol(options => JsonDefaults.Configure(options.PayloadSerializerOptions));
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
if (useCors) app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthChecks("/health");
app.MapApi();
app.MapHub<GameHub>("/hubs/game");
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Entry point, exposed for integration tests.</summary>
public partial class Program;
