using LifeCounterAssist.Api.Domain;
using LifeCounterAssist.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace LifeCounterAssist.Api.Hubs;

/// <summary>Pushes real-time events to connected clients.</summary>
public sealed class Notifier(IHubContext<GameHub> hub) : IGameNotifier
{
    /// <summary>Group of connections watching the lobby list.</summary>
    public const string ListGroup = "list";

    /// <summary>Group of connections watching the room screen.</summary>
    public const string ScreenGroup = "screen";

    /// <summary>Group name for a game's watchers.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <returns>The group name.</returns>
    public static string GameGroup(string gameId) => "game:" + gameId;

    /// <inheritdoc/>
    public Task GameUpdatedAsync(GameState state, CancellationToken ct) =>
        hub.Clients.Group(GameGroup(state.Id)).SendAsync("GameUpdated", state, ct);

    /// <inheritdoc/>
    public Task ListChangedAsync(IReadOnlyList<GameSummaryDto> games, CancellationToken ct) =>
        hub.Clients.Group(ListGroup).SendAsync("ListChanged", games, ct);

    /// <inheritdoc/>
    public Task ScreenChangedAsync(GameState? featured, CancellationToken ct) =>
        hub.Clients.Group(ScreenGroup).SendAsync("ScreenChanged", featured, ct);
}
