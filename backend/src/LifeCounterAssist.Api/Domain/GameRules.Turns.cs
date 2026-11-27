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
    /// <exception cref="ForbiddenException">Thrown when the actor is neither the active player nor the host.</exception>
    /// <exception cref="DomainException">Thrown when the game is not in progress.</exception>
    public static void EndTurn(GameState state, string actor, DateTimeOffset now)
    {
        var turn = EnsureTurnActor(state, actor);
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
    }
}
