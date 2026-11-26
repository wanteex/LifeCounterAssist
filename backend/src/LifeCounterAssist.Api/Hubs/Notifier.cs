using LifeCounterAssist.Api.Domain;
using LifeCounterAssist.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace LifeCounterAssist.Api.Hubs;

/// <summary>Pushes real-time events to connected clients.</summary>
public sealed class Notifier(IHubContext<GameHub> hub) : IGameNotifier
{
    /// <summary>Group name for a game's watchers.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <returns>The group name.</returns>
    public static string GameGroup(string gameId) => "game:" + gameId;

    /// <inheritdoc/>
    public Task GameUpdatedAsync(GameState state, CancellationToken cancellationToken) =>
        hub.Clients.Group(GameGroup(state.Id)).SendAsync("GameUpdated", state, cancellationToken);

    /// <inheritdoc/>
    public Task ListChangedAsync(IReadOnlyList<GameSummaryDto> games, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task ScreenChangedAsync(GameState? featured, CancellationToken ct) => Task.CompletedTask;
}
