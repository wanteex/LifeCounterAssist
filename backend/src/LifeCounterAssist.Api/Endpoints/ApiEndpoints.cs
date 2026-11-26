using LifeCounterAssist.Api.Services;

namespace LifeCounterAssist.Api.Endpoints;

/// <summary>HTTP API surface. Anonymous: no authentication.</summary>
public static class ApiEndpoints
{
    /// <summary>Maps the HTTP API.</summary>
    /// <param name="app">Route builder.</param>
    /// <returns>The same builder.</returns>
    public static IEndpointRouteBuilder MapApi(this IEndpointRouteBuilder app)
    {
        var games = app.MapGroup("/api/games");

        games.MapGet("/", (GameService service) => service.List());
        games.MapPost("/", (CreateGameRequest request, GameService service, CancellationToken ct) =>
            service.CreateAsync(request, ct));
        games.MapPost("/{id}/join", (string id, JoinGameRequest request, GameService service, CancellationToken ct) =>
            service.JoinAsync(id, request, ct));
        games.MapGet("/{id}", (string id, GameService service) => service.Get(id));

        return app;
    }
}
