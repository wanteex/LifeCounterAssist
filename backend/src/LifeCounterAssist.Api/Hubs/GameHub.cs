using LifeCounterAssist.Api.Auth;
using LifeCounterAssist.Api.Domain;
using LifeCounterAssist.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace LifeCounterAssist.Api.Hubs;

/// <summary>
/// Real-time game hub. Every seated player can change any player's counters,
/// which mirrors a shared physical life pad.
/// </summary>
[Authorize]
public sealed class GameHub(GameService games, TimeProvider clock) : Hub
{
    private const int MaxDelta = 100;

    private CurrentUser Caller => Context.User!.ToCurrentUser();

    /// <inheritdoc/>
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, Notifier.UserGroup(Caller.Id));
        await base.OnConnectedAsync();
    }

    /// <summary>Subscribes to a game's updates and returns its current state.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <returns>The game state.</returns>
    public async Task<GameState> Watch(string gameId)
    {
        var state = await games.GetForUserAsync(Caller, gameId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, Notifier.GameGroup(gameId));
        return state;
    }

    /// <summary>Stops receiving a game's updates.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <returns>A task.</returns>
    public Task Unwatch(string gameId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Notifier.GameGroup(gameId));

    /// <summary>Changes a player's life.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="targetUserId">Player.</param>
    /// <param name="delta">Signed delta.</param>
    /// <returns>A task.</returns>
    public Task AdjustLife(string gameId, string targetUserId, int delta) =>
        Mutate(gameId, (s, me) => GameRules.AdjustLife(s, me, targetUserId, Clamp(delta), clock.GetUtcNow()));

    /// <summary>Changes commander damage received by a player from another player's commander.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="targetUserId">Player receiving damage.</param>
    /// <param name="sourceUserId">Commander owner.</param>
    /// <param name="delta">Signed delta.</param>
    /// <returns>A task.</returns>
    public Task AdjustCommanderDamage(string gameId, string targetUserId, string sourceUserId, int delta) =>
        Mutate(gameId, (s, me) => GameRules.AdjustCommanderDamage(s, me, targetUserId, sourceUserId, Clamp(delta), clock.GetUtcNow()));

    /// <summary>Changes a player counter.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="targetUserId">Player.</param>
    /// <param name="counter">Counter type.</param>
    /// <param name="delta">Signed delta.</param>
    /// <returns>A task.</returns>
    public Task AdjustCounter(string gameId, string targetUserId, CounterType counter, int delta) =>
        Mutate(gameId, (s, me) => GameRules.AdjustCounter(s, me, targetUserId, counter, Clamp(delta), clock.GetUtcNow()));

    /// <summary>Sets or clears the monarch.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="targetUserId">New monarch or <see langword="null"/>.</param>
    /// <returns>A task.</returns>
    public Task SetMonarch(string gameId, string? targetUserId) =>
        Mutate(gameId, (s, me) => GameRules.SetMonarch(s, me, targetUserId, clock.GetUtcNow()));

    /// <summary>Sets or clears the initiative.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="targetUserId">Player or <see langword="null"/>.</param>
    /// <returns>A task.</returns>
    public Task SetInitiative(string gameId, string? targetUserId) =>
        Mutate(gameId, (s, me) => GameRules.SetInitiative(s, me, targetUserId, clock.GetUtcNow()));

    /// <summary>Toggles a player's concession.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="targetUserId">Player.</param>
    /// <returns>A task.</returns>
    public Task ToggleConcede(string gameId, string targetUserId) =>
        Mutate(gameId, (s, me) => GameRules.ToggleConcede(s, me, targetUserId, clock.GetUtcNow()));

    /// <summary>Starts the game (host only).</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <returns>A task.</returns>
    public Task StartGame(string gameId) =>
        Mutate(gameId, (s, me) => GameRules.Start(s, me, clock.GetUtcNow()));

    /// <summary>Ends the game.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="winnerUserId">Winner, or <see langword="null"/> for a draw.</param>
    /// <returns>A task.</returns>
    public Task FinishGame(string gameId, string? winnerUserId) =>
        Mutate(gameId, (s, me) => GameRules.Finish(s, me, winnerUserId, clock.GetUtcNow()));

    /// <summary>Selects the deck the caller plays.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="deckId">Deck identifier, or <see langword="null"/> to clear.</param>
    /// <returns>A task.</returns>
    public Task SelectDeck(string gameId, string? deckId) =>
        games.SelectDeckAsync(Caller, gameId, deckId, Context.ConnectionAborted);

    private Task Mutate(string gameId, Action<GameState, string> action)
    {
        var me = Caller;
        return games.MutateAsync(me, gameId, s => action(s, me.Id), Context.ConnectionAborted);
    }

    private static int Clamp(int delta) => Math.Clamp(delta, -MaxDelta, MaxDelta);
}
