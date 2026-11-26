using LifeCounterAssist.Api.Auth;
using LifeCounterAssist.Api.Endpoints;
using LifeCounterAssist.Api.Hubs;
using LifeCounterAssist.Api.Infrastructure;
using LifeCounterAssist.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

/* Storage */
var storageOptions = config.GetSection(StorageOptions.Section).Get<StorageOptions>() ?? new StorageOptions();
builder.Services.AddSingleton(storageOptions);
builder.Services.AddSingleton<TableStore>();
builder.Services.AddSingleton(TimeProvider.System);

/* Services (singletons: GameService owns the in-memory game cache) */
builder.Services.AddHttpClient(nameof(DeckImporter), http =>
{
    http.Timeout = TimeSpan.FromSeconds(10);
    http.DefaultRequestHeaders.UserAgent.ParseAdd("LifeCounterAssist/1.0");
    http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddSingleton(sp => new DeckImporter(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(DeckImporter)),
    sp.GetRequiredService<ILogger<DeckImporter>>()));
builder.Services.AddSingleton<Notifier>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<FriendService>();
builder.Services.AddSingleton<DeckService>();
builder.Services.AddSingleton<GameService>();

/* Authentication */
var authMode = config["Auth:Mode"] ?? "Entra";
if (authMode == DevAuthenticationHandler.SchemeName)
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Auth:Mode=Dev is only allowed in the Development environment.");
    builder.Services.AddAuthentication(DevAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevAuthenticationHandler>(DevAuthenticationHandler.SchemeName, null);
}
else
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = config["Auth:Authority"]
                ?? throw new InvalidOperationException("Auth:Authority is required.");
            options.MapInboundClaims = false;
            options.TokenValidationParameters.ValidAudiences =
                (config["Auth:Audience"] ?? throw new InvalidOperationException("Auth:Audience is required."))
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            options.TokenValidationParameters.NameClaimType = ClaimNames.Name;
            options.Events = new JwtBearerEvents
            {
                // Browsers cannot set headers on WebSocket requests; SignalR sends the token in the query string.
                OnMessageReceived = context =>
                {
                    var token = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        context.Token = token;
                    return Task.CompletedTask;
                },
            };
        });
}

builder.Services.AddAuthorization();

/* Web */
var allowedOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.ConfigureHttpJsonOptions(options => JsonDefaults.Configure(options.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddSignalR(options => options.AddFilter<DomainExceptionHubFilter>())
    .AddJsonProtocol(options => JsonDefaults.Configure(options.PayloadSerializerOptions));
builder.Services.AddHealthChecks();

var app = builder.Build();

await EnsureStorageAsync(app);

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapApi();
app.MapHub<GameHub>("/hubs/game");

app.Run();

static async Task EnsureStorageAsync(WebApplication app)
{
    var store = app.Services.GetRequiredService<TableStore>();
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await store.EnsureTablesAsync();
            return;
        }
        catch (Exception ex) when (attempt < 10)
        {
            // The local emulator or a fresh role assignment may not be ready at startup.
            app.Logger.LogWarning("Storage not ready (attempt {Attempt}): {Error}. Retrying", attempt, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}

/// <summary>Entry point, exposed for integration tests.</summary>
public partial class Program;
