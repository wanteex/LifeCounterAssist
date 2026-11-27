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
}
