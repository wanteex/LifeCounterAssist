using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using LifeCounterAssist.Api.Domain;
using LifeCounterAssist.Api.Infrastructure;

namespace LifeCounterAssist.Api.Services;

/// <summary>Request to create a game.</summary>
/// <param name="Name">Host display name.</param>
/// <param name="StartingLife">Starting life total.</param>
/// <param name="Commander">Host commander, if any.</param>
/// <param name="Mode">How the game is played.</param>
/// <param name="Blitz">Blitz options, if any.</param>
public sealed record CreateGameRequest(
    string Name, int StartingLife = 40, CommanderInfo? Commander = null, GameMode Mode = GameMode.Normal, BlitzSettings? Blitz = null);

/// <summary>Request to join a game.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Commander">Commander; on a reclaim, <see langword="null"/> keeps the current one.</param>
public sealed record JoinGameRequest(string Name, CommanderInfo? Commander = null);

/// <summary>A seat handed to a participant; the token is a secret and never part of the game state.</summary>
/// <param name="Game">Game state.</param>
/// <param name="PlayerId">Seat owner.</param>
/// <param name="SeatToken">Secret proving ownership of the seat.</param>
/// <param name="Reclaimed">Whether an existing seat was taken back rather than a new one created.</param>
public sealed record SeatDto(GameState Game, string PlayerId, string SeatToken, bool Reclaimed = false);

/// <summary>Lightweight game description for the lobby list.</summary>
/// <param name="Id">Game identifier.</param>
/// <param name="HostName">Host display name.</param>
/// <param name="PlayerCount">Seated players.</param>
/// <param name="StartingLife">Starting life total.</param>
/// <param name="Status">Game status.</param>
/// <param name="CreatedAt">Creation date.</param>
public sealed record GameSummaryDto(
    string Id, string HostName, int PlayerCount, int StartingLife, GameStatus Status, DateTimeOffset CreatedAt);

/// <summary>Owns the in-memory games, seat tokens and room-screen selection. Single instance only.</summary>
public sealed class GameService(IGameNotifier notifier, TimeProvider clock, ILogger<GameService> logger)
{
    private static readonly TimeSpan FinishedDisplay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FinishedRetention = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan IdleRetention = TimeSpan.FromHours(6);
    private const int MaxActiveGames = 20;

    private readonly ConcurrentDictionary<string, GameState> _games = new();
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _seats = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    // Serializes list/screen computation and publication so concurrent actions cannot publish out of order.
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private string? _screenGameId;

    /// <summary>Creates a game with the caller seated as host.</summary>
    /// <param name="r">Creation request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The host seat.</returns>
    /// <exception cref="DomainException">Thrown when too many games are active.</exception>
    public async Task<SeatDto> CreateAsync(CreateGameRequest r, CancellationToken ct = default)
    {
        if (_games.Values.Count(g => g.Status is GameStatus.Lobby or GameStatus.InProgress) >= MaxActiveGames)
            throw new DomainException("too_many_games");

        var gameId = Guid.NewGuid().ToString("N");
        var playerId = NewId();
        var state = GameRules.Create(gameId, playerId, r.Name, r.StartingLife, clock.GetUtcNow(), r.Commander, r.Mode, r.Blitz);
        var token = NewToken();
        _seats[gameId] = new Dictionary<string, string> { [token] = playerId };
        _locks[gameId] = new SemaphoreSlim(1, 1);
        _games[gameId] = state;
        await BroadcastAsync(state, ct);
        return new SeatDto(Clone(state), playerId, token);
    }

    /// <summary>Seats a player, or reclaims the seat of the same name with a fresh token.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="r">Join request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The seat.</returns>
    public async Task<SeatDto> JoinAsync(string gameId, JoinGameRequest r, CancellationToken ct = default)
    {
        var gate = GateFor(gameId);
        await gate.WaitAsync(ct);
        try
        {
            var current = GetOrThrow(gameId);
            var copy = Clone(current);
            var (player, reclaimed) = GameRules.Join(copy, NewId(), r.Name, clock.GetUtcNow(), r.Commander);
            var commanderChanged = current.FindPlayer(player.PlayerId)?.Commander != player.Commander;
            var token = NewToken();
            var seats = _seats[gameId];
            lock (seats)
            {
                foreach (var old in seats.Where(kv => kv.Value == player.PlayerId).Select(kv => kv.Key).ToList())
                    seats.Remove(old);
                seats[token] = player.PlayerId;
            }

            _games[gameId] = copy;
            // A plain reclaim does not change the state; a reclaim with a different commander does.
            if (!reclaimed || commanderChanged) await BroadcastAsync(copy, ct);
            return new SeatDto(Clone(copy), player.PlayerId, token, reclaimed);
        }
        finally { gate.Release(); }
    }

    /// <summary>Gets a game.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <returns>A copy of the game state.</returns>
    /// <exception cref="NotFoundException">Thrown when the game does not exist.</exception>
    public GameState Get(string gameId) => Clone(GetOrThrow(gameId));

    /// <summary>Lists lobby and in-progress games, most recent first.</summary>
    /// <returns>Game summaries.</returns>
    public IReadOnlyList<GameSummaryDto> List() =>
        _games.Values
            .Where(g => g.Status is GameStatus.Lobby or GameStatus.InProgress)
            .OrderByDescending(g => g.CreatedAt)
            .Select(g => new GameSummaryDto(
                g.Id,
                g.FindPlayer(g.HostPlayerId)?.DisplayName ?? "",
                g.Players.Count,
                g.StartingLife,
                g.Status,
                g.CreatedAt))
            .ToList();

    /// <summary>Picks the game to show on the room screen.</summary>
    /// <returns>A copy of the featured game, or <see langword="null"/>.</returns>
    public GameState? GetFeatured()
    {
        var now = clock.GetUtcNow();
        var games = _games.Values.ToList();

        var game =
            games.Where(g => g.Status == GameStatus.InProgress).MaxBy(g => g.StartedAt ?? g.CreatedAt)
            ?? games.Where(g => g.Status == GameStatus.Finished && g.FinishedAt is { } f && now - f < FinishedDisplay)
                .MaxBy(g => g.FinishedAt)
            ?? games.Where(g => g.Status == GameStatus.Lobby).MaxBy(g => g.CreatedAt);
        return game is null ? null : Clone(game);
    }

    /// <summary>Applies an action on behalf of a seat owner.</summary>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="seatToken">Caller's seat token.</param>
    /// <param name="action">Action receiving the state and the caller's player identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new state.</returns>
    /// <exception cref="NotFoundException">Thrown when the game does not exist.</exception>
    /// <exception cref="ForbiddenException">Thrown when the token is not a seat of this game.</exception>
    public async Task<GameState> ActAsync(
        string gameId, string seatToken, Action<GameState, string> action, CancellationToken ct = default)
    {
        var gate = GateFor(gameId);
        await gate.WaitAsync(ct);
        try
        {
            var current = GetOrThrow(gameId);
            string? playerId = null;
            var seats = _seats[gameId];
            lock (seats)
            {
                if (seatToken is not null) seats.TryGetValue(seatToken, out playerId);
            }

            // A seat whose player left the game is no longer valid.
            if (playerId is null || current.FindPlayer(playerId) is null) throw new ForbiddenException("invalid_seat");

            var copy = Clone(current);
            action(copy, playerId);
            if (copy.FindPlayer(playerId) is null)
            {
                lock (seats)
                {
                    foreach (var old in seats.Where(kv => kv.Value == playerId).Select(kv => kv.Key).ToList())
                        seats.Remove(old);
                }
            }

            _games[gameId] = copy;
            await BroadcastAsync(copy, ct);
            return Clone(copy);
        }
        finally { gate.Release(); }
    }

    /// <summary>Completes elapsed finish countdowns and removes expired games.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task TickAsync(CancellationToken ct = default)
    {
        var removed = false;
        var now = clock.GetUtcNow();

        foreach (var id in _games.Keys.ToList())
        {
            if (!_locks.TryGetValue(id, out var gate)) continue;
            await gate.WaitAsync(ct);
            try
            {
                if (!_games.TryGetValue(id, out var current)) continue;
                var copy = Clone(current);
                if (GameRules.CompletePendingFinish(copy, now))
                {
                    _games[id] = copy;
                    await BroadcastAsync(copy, ct);
                }
                else if (IsExpired(current, now))
                {
                    _games.TryRemove(id, out _);
                    _seats.TryRemove(id, out _);
                    _locks.TryRemove(id, out _);
                    removed = true;
                }
            }
            finally { gate.Release(); }
        }

        // Completed games already broadcast list and screen; only removals and the 30 s display expiry remain.
        await _publishGate.WaitAsync(ct);
        try
        {
            var screen = GetFeatured();
            if (removed) await NotifySafelyAsync(() => notifier.ListChangedAsync(List(), ct), "list");
            if (removed || screen?.Id != _screenGameId) await PublishScreenAsync(screen, ct);
        }
        finally { _publishGate.Release(); }
    }

    private static bool IsExpired(GameState g, DateTimeOffset now) =>
        now - g.LastActivityAt > IdleRetention
        || (g.Status is GameStatus.Finished or GameStatus.Cancelled
            && now - (g.FinishedAt ?? g.LastActivityAt) > FinishedRetention);

    private async Task BroadcastAsync(GameState state, CancellationToken ct)
    {
        await NotifySafelyAsync(() => notifier.GameUpdatedAsync(Clone(state), ct), "game");
        await _publishGate.WaitAsync(ct);
        try
        {
            await NotifySafelyAsync(() => notifier.ListChangedAsync(List(), ct), "list");
            await PublishScreenAsync(GetFeatured(), ct);
        }
        finally { _publishGate.Release(); }
    }

    // Must be called while holding _publishGate.
    private async Task PublishScreenAsync(GameState? featured, CancellationToken ct)
    {
        _screenGameId = featured?.Id;
        await NotifySafelyAsync(() => notifier.ScreenChangedAsync(featured, ct), "screen");
    }

    // The state change is already committed: a notification failure must never surface as an action error.
    private async Task NotifySafelyAsync(Func<Task> notify, string what)
    {
        try { await notify(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to publish {Notification} notification", what);
        }
    }

    private GameState GetOrThrow(string gameId) =>
        _games.TryGetValue(gameId, out var state) ? state : throw new NotFoundException("game_not_found");

    private SemaphoreSlim GateFor(string gameId) =>
        _locks.TryGetValue(gameId, out var gate) ? gate : throw new NotFoundException("game_not_found");

    private static GameState Clone(GameState state) =>
        JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state, JsonDefaults.Options), JsonDefaults.Options)!;

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    private static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}
