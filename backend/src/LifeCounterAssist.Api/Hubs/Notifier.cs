using LifeCounterAssist.Api.Domain;
using Microsoft.AspNetCore.SignalR;

namespace LifeCounterAssist.Api.Hubs;

/// <summary>Pushes real-time events to connected clients.</summary>
public sealed class Notifier(IHubContext<GameHub> hub)
{
    /// <summary>Group name for a user's connections.</summary>
    /// <param name="userId">User identifier.</param>
    /// <returns>The group name.</returns>
    public static string UserGroup(string userId) => "user:" + userId;

    /// <summary>Group name for a game's watchers.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <returns>The group name.</returns>
    public static string GameGroup(string gameId) => "game:" + gameId;

    /// <summary>Broadcasts a new game state to its watchers.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task GameUpdatedAsync(GameState state, CancellationToken cancellationToken = default) =>
        hub.Clients.Group(GameGroup(state.Id)).SendAsync("GameUpdated", state, cancellationToken);

    /// <summary>Tells a user to refresh invitations.</summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task InvitationsChangedAsync(string userId, CancellationToken cancellationToken = default) =>
        hub.Clients.Group(UserGroup(userId)).SendAsync("InvitationsChanged", cancellationToken);

    /// <summary>Tells a user to refresh friends.</summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task FriendsChangedAsync(string userId, CancellationToken cancellationToken = default) =>
        hub.Clients.Group(UserGroup(userId)).SendAsync("FriendsChanged", cancellationToken);
}
