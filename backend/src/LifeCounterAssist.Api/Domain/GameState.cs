namespace LifeCounterAssist.Api.Domain;

/// <summary>Lifecycle status of a game.</summary>
public enum GameStatus
{
    /// <summary>Players are joining; counters are not active yet.</summary>
    Lobby,

    /// <summary>The game is being played.</summary>
    InProgress,

    /// <summary>The game is over and recorded in players' history.</summary>
    Finished,
}

/// <summary>Player counters that can be adjusted with a delta.</summary>
public enum CounterType
{
    /// <summary>Poison counters (10 eliminates).</summary>
    Poison,

    /// <summary>Energy counters.</summary>
    Energy,

    /// <summary>Experience counters.</summary>
    Experience,

    /// <summary>Number of times the commander was cast from the command zone.</summary>
    CommanderTax,
}

/// <summary>Full shared state of a game, synchronized to every participant.</summary>
public sealed class GameState
{
    /// <summary>Gets or sets the game identifier.</summary>
    public required string Id { get; set; }

    /// <summary>Gets or sets the short code other players can use to join.</summary>
    public required string JoinCode { get; set; }

    /// <summary>Gets or sets the identifier of the user who created the game.</summary>
    public required string HostUserId { get; set; }

    /// <summary>Gets or sets the game status.</summary>
    public GameStatus Status { get; set; } = GameStatus.Lobby;

    /// <summary>Gets or sets the maximum number of seats.</summary>
    public int MaxPlayers { get; set; } = 4;

    /// <summary>Gets or sets the life total each player starts with.</summary>
    public int StartingLife { get; set; } = GameRules.CommanderStartingLife;

    /// <summary>Gets or sets the creation date.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the date the game started.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Gets or sets the date the game finished.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Gets or sets the winner, if any.</summary>
    public string? WinnerUserId { get; set; }

    /// <summary>Gets or sets the player holding the monarch designation.</summary>
    public string? MonarchUserId { get; set; }

    /// <summary>Gets or sets the player holding the initiative.</summary>
    public string? InitiativeUserId { get; set; }

    /// <summary>Gets or sets the seated players, in seat order.</summary>
    public List<PlayerState> Players { get; set; } = [];

    /// <summary>Gets or sets the users invited to the game.</summary>
    public List<string> InvitedUserIds { get; set; } = [];

    /// <summary>Gets or sets the most recent actions, newest last.</summary>
    public List<GameLogEntry> Log { get; set; } = [];

    /// <summary>Gets or sets a counter incremented on every mutation.</summary>
    public long Version { get; set; }

    /// <summary>Finds a seated player.</summary>
    /// <param name="userId">The user identifier.</param>
    /// <returns>The player or <see langword="null"/>.</returns>
    public PlayerState? FindPlayer(string userId) => Players.Find(p => p.UserId == userId);
}

/// <summary>State of a single seated player.</summary>
public sealed class PlayerState
{
    /// <summary>Gets or sets the user identifier.</summary>
    public required string UserId { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Gets or sets the selected deck identifier.</summary>
    public string? DeckId { get; set; }

    /// <summary>Gets or sets the selected deck name.</summary>
    public string? DeckName { get; set; }

    /// <summary>Gets or sets the commander name.</summary>
    public string? CommanderName { get; set; }

    /// <summary>Gets or sets the commander art URL.</summary>
    public string? CommanderImageUrl { get; set; }

    /// <summary>Gets or sets the life total.</summary>
    public int Life { get; set; }

    /// <summary>Gets or sets the poison counters.</summary>
    public int Poison { get; set; }

    /// <summary>Gets or sets the energy counters.</summary>
    public int Energy { get; set; }

    /// <summary>Gets or sets the experience counters.</summary>
    public int Experience { get; set; }

    /// <summary>Gets or sets the commander tax count.</summary>
    public int CommanderTax { get; set; }

    /// <summary>Gets or sets commander damage received, keyed by source player identifier.</summary>
    public Dictionary<string, int> CommanderDamage { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether the player conceded.</summary>
    public bool Conceded { get; set; }

    /// <summary>Gets a value indicating whether the player has lost according to the rules.</summary>
    public bool IsEliminated =>
        Conceded
        || Life <= 0
        || Poison >= GameRules.LethalPoison
        || CommanderDamage.Values.Any(d => d >= GameRules.LethalCommanderDamage);
}

/// <summary>A single entry of the game action log.</summary>
/// <param name="At">When the action happened.</param>
/// <param name="ActorUserId">Who performed the action.</param>
/// <param name="Message">Machine-friendly action key.</param>
/// <param name="TargetUserId">Affected player, if any.</param>
/// <param name="Value">Numeric delta or value, if any.</param>
/// <param name="Detail">Extra information such as a counter type or source player.</param>
public sealed record GameLogEntry(
    DateTimeOffset At,
    string ActorUserId,
    string Message,
    string? TargetUserId = null,
    int? Value = null,
    string? Detail = null);
