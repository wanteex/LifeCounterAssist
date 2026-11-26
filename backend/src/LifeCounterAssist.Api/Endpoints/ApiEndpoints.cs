using System.Security.Claims;
using LifeCounterAssist.Api.Auth;
using LifeCounterAssist.Api.Services;

namespace LifeCounterAssist.Api.Endpoints;

/// <summary>Maps the REST API.</summary>
public static class ApiEndpoints
{
    /// <summary>Registers every <c>/api</c> endpoint.</summary>
    /// <param name="app">Route builder.</param>
    /// <returns>The route builder.</returns>
    public static IEndpointRouteBuilder MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        var me = api.MapGroup("/me");
        me.MapGet("/", async (ClaimsPrincipal p, UserService users, CancellationToken ct) =>
            UserService.ToDto(await users.EnsureProfileAsync(p.ToCurrentUser(), ct)));
        me.MapPut("/", async (UpdateProfileRequest body, ClaimsPrincipal p, UserService users, CancellationToken ct) =>
            UserService.ToDto(await users.UpdateAsync(p.ToCurrentUser(), body, ct)));

        var friends = api.MapGroup("/friends");
        friends.MapGet("/", (ClaimsPrincipal p, FriendService s, CancellationToken ct) => s.ListAsync(p.ToCurrentUser(), ct));
        friends.MapPost("/", (AddFriendRequest body, ClaimsPrincipal p, FriendService s, CancellationToken ct) =>
            s.AddAsync(p.ToCurrentUser(), body, ct));
        friends.MapPost("/{userId}/accept", (string userId, ClaimsPrincipal p, FriendService s, CancellationToken ct) =>
            s.AcceptAsync(p.ToCurrentUser(), userId, ct));
        friends.MapDelete("/{userId}", async (string userId, ClaimsPrincipal p, FriendService s, CancellationToken ct) =>
        {
            await s.RemoveAsync(p.ToCurrentUser(), userId, ct);
            return Results.NoContent();
        });

        var decks = api.MapGroup("/decks");
        decks.MapGet("/", (ClaimsPrincipal p, DeckService s, CancellationToken ct) => s.ListAsync(p.ToCurrentUser(), ct));
        decks.MapPost("/", (AddDeckRequest body, ClaimsPrincipal p, DeckService s, CancellationToken ct) =>
            s.AddAsync(p.ToCurrentUser(), body, ct));
        decks.MapDelete("/{deckId}", async (string deckId, ClaimsPrincipal p, DeckService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(p.ToCurrentUser(), deckId, ct);
            return Results.NoContent();
        });

        var games = api.MapGroup("/games");
        games.MapPost("/", (CreateGameRequest body, ClaimsPrincipal p, GameService s, CancellationToken ct) =>
            s.CreateAsync(p.ToCurrentUser(), body, ct));
        games.MapGet("/history", (ClaimsPrincipal p, GameService s, CancellationToken ct) => s.HistoryAsync(p.ToCurrentUser(), ct));
        games.MapPost("/join", (JoinByCodeRequest body, ClaimsPrincipal p, GameService s, CancellationToken ct) =>
            s.JoinByCodeAsync(p.ToCurrentUser(), body, ct));
        games.MapGet("/{gameId}", (string gameId, ClaimsPrincipal p, GameService s, CancellationToken ct) =>
            s.GetForUserAsync(p.ToCurrentUser(), gameId, ct));
        games.MapPost("/{gameId}/join", (string gameId, JoinGameRequest body, ClaimsPrincipal p, GameService s, CancellationToken ct) =>
            s.JoinAsync(p.ToCurrentUser(), gameId, body.DeckId, ct));
        games.MapPost("/{gameId}/leave", async (string gameId, ClaimsPrincipal p, GameService s, CancellationToken ct) =>
        {
            await s.LeaveAsync(p.ToCurrentUser(), gameId, ct);
            return Results.NoContent();
        });
        games.MapPost("/{gameId}/invite", (string gameId, InviteRequest body, ClaimsPrincipal p, GameService s, CancellationToken ct) =>
            s.InviteAsync(p.ToCurrentUser(), gameId, body, ct));

        var invitations = api.MapGroup("/invitations");
        invitations.MapGet("/", (ClaimsPrincipal p, GameService s, CancellationToken ct) => s.ListInvitationsAsync(p.ToCurrentUser(), ct));
        invitations.MapDelete("/{gameId}", async (string gameId, ClaimsPrincipal p, GameService s, CancellationToken ct) =>
        {
            await s.DeclineInvitationAsync(p.ToCurrentUser(), gameId, ct);
            return Results.NoContent();
        });

        return app;
    }
}
