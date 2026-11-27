namespace LifeCounterAssist.Api.Domain;

/// <summary>Turn tracking and the Blitz clock.</summary>
public static partial class GameRules
{
    /// <summary>Turn limit multiplier applied each round in Blitz.</summary>
    public const double BlitzGrowth = 1.5;

    /// <summary>Longest Blitz turn limit, in seconds.</summary>
    public const int BlitzMaxSeconds = 300;

    /// <summary>Delay before the turn of an eliminated active player passes.</summary>
    public static readonly TimeSpan TurnSkipDelay = TimeSpan.FromSeconds(5);

    private static readonly int[] AllowedBlitzBaseSeconds = [15, 30, 60];

    /// <summary>Gets the Blitz turn limit of a round.</summary>
    /// <param name="baseSeconds">Limit of the first round.</param>
    /// <param name="round">Round, starting at 1.</param>
    /// <returns>The limit in seconds, capped at <see cref="BlitzMaxSeconds"/>.</returns>
    public static int TurnLimitSeconds(int baseSeconds, int round) =>
        (int)Math.Min(BlitzMaxSeconds, Math.Round(baseSeconds * Math.Pow(BlitzGrowth, round - 1), MidpointRounding.AwayFromZero));

    private static BlitzSettings? NormalizeBlitz(GameMode mode, BlitzSettings? blitz)
    {
        if (mode != GameMode.Blitz) return null;
        var settings = blitz ?? new BlitzSettings(30, false);
        if (!AllowedBlitzBaseSeconds.Contains(settings.BaseSeconds)) throw new DomainException("invalid_blitz");
        return settings;
    }

    private static void StartTurns(GameState state, DateTimeOffset now)
    {
        var first = state.Players.FirstOrDefault(p => !p.IsEliminated) ?? state.Players[0];
        state.Turn = new TurnState
        {
            ActivePlayerId = first.PlayerId,
            RunningSince = now,
            LimitSeconds = state.Blitz is { } b ? TurnLimitSeconds(b.BaseSeconds, 1) : null,
        };
    }

    /// <summary>Ends the current turn; play passes to the next player still in the game.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="actor">Must be the active player or the host.</param>
    /// <param name="now">Current time.</param>
    /// <param name="expectedActivePlayerId">Player whose turn the caller is ending; when the turn has already
    /// passed (another tap, auto-skip), nothing happens so a stale tap never skips the next player.</param>
    /// <exception cref="ForbiddenException">Thrown when the actor is neither the active player nor the host.</exception>
    /// <exception cref="DomainException">Thrown when the game is not in progress.</exception>
    public static void EndTurn(GameState state, string actor, DateTimeOffset now, string? expectedActivePlayerId = null)
    {
        var turn = EnsureTurnActor(state, actor);
        if (expectedActivePlayerId is not null && expectedActivePlayerId != turn.ActivePlayerId) return;
        AdvanceTurn(state, turn, actor, now, "turn_ended");
    }

    private static TurnState EnsureTurnActor(GameState state, string actor)
    {
        EnsureInProgress(state);
        var turn = state.Turn ?? throw new DomainException("game_not_in_progress");
        if (actor != turn.ActivePlayerId && actor != state.HostPlayerId) throw new ForbiddenException("not_your_turn");
        return turn;
    }

    /// <summary>Gives the turn to the next seat still in the game, clockwise; going past the last seat starts a new round.</summary>
    private static void AdvanceTurn(GameState state, TurnState turn, string actor, DateTimeOffset now, string logMessage)
    {
        var n = state.Players.Count;
        var i = state.Players.FindIndex(p => p.PlayerId == turn.ActivePlayerId);
        var next = i;
        var wrapped = true;
        for (var k = 1; k < n; k++)
        {
            var candidate = state.Players[(i + k) % n];
            if (candidate.IsEliminated) continue;
            next = (i + k) % n;
            wrapped = i + k >= n;
            break;
        }

        if (wrapped) turn.Round++;
        turn.ActivePlayerId = state.Players[next].PlayerId;
        turn.ElapsedMs = 0;
        turn.PenaltySeconds = 0;
        turn.Combat = false;
        turn.SkipAt = null;
        turn.RunningSince = turn.Paused ? null : now;
        turn.LimitSeconds = state.Blitz is { } b ? TurnLimitSeconds(b.BaseSeconds, turn.Round) : null;
        AddLog(state, new GameLogEntry(now, actor, logMessage, turn.ActivePlayerId), now);
        SyncTurn(state, now);
    }

    /// <summary>Starts or ends the combat phase, which freezes the Blitz clock.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="actor">Must be the active player or the host.</param>
    /// <param name="now">Current time.</param>
    /// <exception cref="ForbiddenException">Thrown when the actor is neither the active player nor the host.</exception>
    /// <exception cref="DomainException">Thrown when the game is not a Blitz game in progress.</exception>
    public static void ToggleCombat(GameState state, string actor, DateTimeOffset now)
    {
        var turn = EnsureTurnActor(state, actor);
        if (state.Mode != GameMode.Blitz) throw new DomainException("blitz_only");
        turn.Combat = !turn.Combat;
        AddLog(state, new GameLogEntry(now, actor, turn.Combat ? "combat_started" : "combat_ended", turn.ActivePlayerId), now);
        SyncTurn(state, now);
    }

    /// <summary>Pauses or resumes the Blitz clock (host only).</summary>
    /// <param name="state">Game state.</param>
    /// <param name="actor">Must be the host.</param>
    /// <param name="now">Current time.</param>
    /// <exception cref="ForbiddenException">Thrown when the actor is not the host.</exception>
    /// <exception cref="DomainException">Thrown when the game is not a Blitz game in progress.</exception>
    public static void TogglePause(GameState state, string actor, DateTimeOffset now)
    {
        EnsureInProgress(state);
        if (actor != state.HostPlayerId) throw new ForbiddenException("host_only");
        if (state.Mode != GameMode.Blitz || state.Turn is not { } turn) throw new DomainException("blitz_only");
        turn.Paused = !turn.Paused;
        AddLog(state, new GameLogEntry(now, actor, turn.Paused ? "paused" : "resumed"), now);
        SyncTurn(state, now);
    }

    /// <summary>Starts or freezes the turn clock to match the game: it runs only while the game is in progress,
    /// outside combat and pause, and while the active player and at least one opponent are still in the game.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="now">Current time.</param>
    public static void SyncTurn(GameState state, DateTimeOffset now)
    {
        if (state.Turn is not { } turn) return;
        var active = state.FindPlayer(turn.ActivePlayerId);
        var alive = state.Players.Count(p => !p.IsEliminated);
        if (state.Status == GameStatus.InProgress && active is { IsEliminated: true } && alive >= 1)
            turn.SkipAt ??= now + TurnSkipDelay;
        else
            turn.SkipAt = null;

        var shouldRun = state.Status == GameStatus.InProgress
            && !turn.Combat
            && !turn.Paused
            && turn.SkipAt is null
            && active is { IsEliminated: false }
            && alive >= 2;

        if (shouldRun && turn.RunningSince is null) turn.RunningSince = now;
        else if (!shouldRun && turn.RunningSince is { } since)
        {
            turn.ElapsedMs += (long)(now - since).TotalMilliseconds;
            turn.RunningSince = null;
        }
    }

    private static long UsedMs(TurnState turn, DateTimeOffset now) =>
        turn.ElapsedMs + (turn.RunningSince is { } since ? (long)(now - since).TotalMilliseconds : 0);

    /// <summary>Passes the turn of an eliminated active player once <see cref="TurnSkipDelay"/> has elapsed.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="now">Current time.</param>
    /// <returns><see langword="true"/> when the turn has just passed.</returns>
    public static bool CompletePendingSkip(GameState state, DateTimeOffset now)
    {
        if (state.Status != GameStatus.InProgress || state.Turn is not { SkipAt: { } at } turn || at > now) return false;
        AdvanceTurn(state, turn, turn.ActivePlayerId, now, "turn_skipped");
        return true;
    }

    /// <summary>Charges the active player one life per full second over the Blitz limit, when the penalty is on.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="now">Current time.</param>
    /// <returns><see langword="true"/> when life was taken.</returns>
    public static bool ApplyTimePenalty(GameState state, DateTimeOffset now)
    {
        if (state.Status != GameStatus.InProgress
            || state.Blitz is not { Penalty: true }
            || state.Turn is not { RunningSince: not null, LimitSeconds: { } limit } turn)
            return false;

        var over = (int)((UsedMs(turn, now) - limit * 1000L) / 1000);
        if (over <= turn.PenaltySeconds) return false;

        var lost = over - turn.PenaltySeconds;
        turn.PenaltySeconds = over;
        var player = GetPlayer(state, turn.ActivePlayerId);
        player.Life -= lost;
        AddLog(state, new GameLogEntry(now, turn.ActivePlayerId, "time_penalty", turn.ActivePlayerId, -lost), now);
        RefreshPendingFinish(state, now);
        return true;
    }
}
