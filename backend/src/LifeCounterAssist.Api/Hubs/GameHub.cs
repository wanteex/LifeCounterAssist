using LifeCounterAssist.Api.Domain;
using LifeCounterAssist.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace LifeCounterAssist.Api.Hubs;

/// <summary>Real-time game hub. Anonymous: seat ownership is proven by the seat token passed to each action.</summary>
public sealed class GameHub(GameService games, TimeProvider clock) : Hub
{
    private const int MaxDelta = 100;

    /// <summary>Starts receiving updates of a game.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <returns>The current game state.</returns>
    public async Task<GameState> Watch(string gameId)
    {
        // Validate first so unknown games never create a group membership.
        games.Get(gameId);
        await Groups.AddToGroupAsync(Context.ConnectionId, Notifier.GameGroup(gameId));
        // Read after joining the group so an update between the two steps is not lost.
        return games.Get(gameId);
    }

    /// <summary>Stops receiving updates of a game.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <returns>A task.</returns>
    public Task Unwatch(string gameId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Notifier.GameGroup(gameId));

    /// <summary>Starts receiving lobby list updates.</summary>
    /// <returns>The current list.</returns>
    public async Task<IReadOnlyList<GameSummaryDto>> WatchList()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, Notifier.ListGroup);
        return games.List();
    }

    /// <summary>Starts receiving room-screen updates.</summary>
    /// <returns>The currently featured game, or <see langword="null"/>.</returns>
    public async Task<GameState?> WatchScreen()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, Notifier.ScreenGroup);
        return games.GetFeatured();
    }

    /// <summary>Starts the game.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="seatToken">Caller's seat token.</param>
    /// <returns>A task.</returns>
    public Task StartGame(string gameId, string seatToken) =>
        games.ActAsync(gameId, seatToken, (s, p) => GameRules.Start(s, p, clock.GetUtcNow()));

    /// <summary>Moves a player one seat around the table (host only, lobby only).</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="seatToken">Caller's seat token.</param>
    /// <param name="playerId">Player to move.</param>
    /// <param name="offset">-1 for one seat earlier, +1 for one seat later.</param>
    /// <returns>A task.</returns>
    public Task MovePlayer(string gameId, string seatToken, string playerId, int offset) =>
        games.ActAsync(gameId, seatToken, (s, p) => GameRules.MovePlayer(s, p, playerId, offset, clock.GetUtcNow()));

    /// <summary>Cancels the game.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="seatToken">Caller's seat token.</param>
    /// <returns>A task.</returns>
    public Task CancelGame(string gameId, string seatToken) =>
        games.ActAsync(gameId, seatToken, (s, p) => GameRules.Cancel(s, p, clock.GetUtcNow()));

    /// <summary>Leaves the game.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="seatToken">Caller's seat token.</param>
    /// <returns>A task.</returns>
    public Task LeaveGame(string gameId, string seatToken) =>
        games.ActAsync(gameId, seatToken, (s, p) => GameRules.Leave(s, p, clock.GetUtcNow()));

    /// <summary>Adjusts a player's life.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="seatToken">Caller's seat token.</param>
    /// <param name="targetPlayerId">Player whose life changes.</param>
    /// <param name="delta">Signed delta, clamped to plus or minus 100.</param>
    /// <param name="seq">Client sequence number of this action, acknowledged in the state.</param>
    /// <returns>A task.</returns>
    public Task AdjustLife(string gameId, string seatToken, string targetPlayerId, int delta, long seq) =>
        games.ActAsync(gameId, seatToken, (s, p) =>
        {
            GameRules.AdjustLife(s, p, targetPlayerId, Clamp(delta), clock.GetUtcNow());
            GameRules.Acknowledge(s, p, seq);
        });

    /// <summary>Adjusts commander damage.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="seatToken">Caller's seat token.</param>
    /// <param name="targetPlayerId">Player receiving damage.</param>
    /// <param name="sourcePlayerId">Owner of the commander dealing damage.</param>
    /// <param name="delta">Signed delta, clamped to plus or minus 100.</param>
    /// <param name="seq">Client sequence number of this action, acknowledged in the state.</param>
    /// <returns>A task.</returns>
    public Task AdjustCommanderDamage(
        string gameId, string seatToken, string targetPlayerId, string sourcePlayerId, int delta, long seq) =>
        games.ActAsync(gameId, seatToken, (s, p) =>
        {
            GameRules.AdjustCommanderDamage(s, p, targetPlayerId, sourcePlayerId, Clamp(delta), clock.GetUtcNow());
            GameRules.Acknowledge(s, p, seq);
        });

    /// <summary>Adjusts poison counters.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="seatToken">Caller's seat token.</param>
    /// <param name="targetPlayerId">Player whose counter changes.</param>
    /// <param name="delta">Signed delta, clamped to plus or minus 100.</param>
    /// <param name="seq">Client sequence number of this action, acknowledged in the state.</param>
    /// <returns>A task.</returns>
    public Task AdjustPoison(string gameId, string seatToken, string targetPlayerId, int delta, long seq) =>
        games.ActAsync(gameId, seatToken, (s, p) =>
        {
            GameRules.AdjustPoison(s, p, targetPlayerId, Clamp(delta), clock.GetUtcNow());
            GameRules.Acknowledge(s, p, seq);
        });

    /// <summary>Toggles a player's concession.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="seatToken">Caller's seat token.</param>
    /// <param name="targetPlayerId">Player conceding or un-conceding.</param>
    /// <returns>A task.</returns>
    public Task ToggleConcede(string gameId, string seatToken, string targetPlayerId) =>
        games.ActAsync(gameId, seatToken, (s, p) =>
            GameRules.ToggleConcede(s, p, targetPlayerId, clock.GetUtcNow()));

    /// <summary>Cancels the pending automatic finish.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="seatToken">Caller's seat token.</param>
    /// <returns>A task.</returns>
    public Task CancelPendingFinish(string gameId, string seatToken) =>
        games.ActAsync(gameId, seatToken, (s, p) => GameRules.CancelPendingFinish(s, p, clock.GetUtcNow()));

    /// <summary>Ends the current turn (active player or host).</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="seatToken">Caller's seat token.</param>
    /// <returns>A task.</returns>
    public Task EndTurn(string gameId, string seatToken) =>
        games.ActAsync(gameId, seatToken, (s, p) => GameRules.EndTurn(s, p, clock.GetUtcNow()));

    /// <summary>Starts or ends the combat phase (Blitz; active player or host).</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="seatToken">Caller's seat token.</param>
    /// <returns>A task.</returns>
    public Task ToggleCombat(string gameId, string seatToken) =>
        games.ActAsync(gameId, seatToken, (s, p) => GameRules.ToggleCombat(s, p, clock.GetUtcNow()));

    /// <summary>Pauses or resumes the Blitz clock (host only).</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="seatToken">Caller's seat token.</param>
    /// <returns>A task.</returns>
    public Task TogglePause(string gameId, string seatToken) =>
        games.ActAsync(gameId, seatToken, (s, p) => GameRules.TogglePause(s, p, clock.GetUtcNow()));

    /// <summary>Ends the game manually.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="seatToken">Caller's seat token.</param>
    /// <returns>A task.</returns>
    public Task FinishGame(string gameId, string seatToken) =>
        games.ActAsync(gameId, seatToken, (s, p) => GameRules.Finish(s, p, clock.GetUtcNow()));

    private static int Clamp(int delta) => Math.Clamp(delta, -MaxDelta, MaxDelta);
}
