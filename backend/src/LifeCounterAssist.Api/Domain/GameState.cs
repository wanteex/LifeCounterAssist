namespace LifeCounterAssist.Api.Domain;

/// <summary>Lifecycle status of a game.</summary>
public enum GameStatus
{
    /// <summary>Players are joining; counters are not active yet.</summary>
    Lobby,

    /// <summary>The game is being played.</summary>
    InProgress,

    /// <summary>The game is over.</summary>
    Finished,

    /// <summary>The host cancelled the game while it was still in the lobby.</summary>
    Cancelled,
}

/// <summary>Full shared state of a game, synchronized to every participant.</summary>
public sealed class GameState
{
    /// <summary>Gets or sets the game identifier.</summary>
    public required string Id { get; set; }

    /// <summary>Gets or sets the identifier of the player who created the game.</summary>
    public required string HostPlayerId { get; set; }

    /// <summary>Gets or sets the game status.</summary>
    public GameStatus Status { get; set; } = GameStatus.Lobby;

    /// <summary>Gets or sets the life total each player starts with.</summary>
    public int StartingLife { get; set; } = 40;

    /// <summary>Gets or sets the creation date.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the date the game started.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Gets or sets the date the game finished or was cancelled.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Gets or sets the date of the last mutation.</summary>
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>Gets or sets the winner, if any.</summary>
    public string? WinnerPlayerId { get; set; }

    /// <summary>Gets or sets the last player standing while the finish countdown runs.</summary>
    public string? PendingWinnerPlayerId { get; set; }

    /// <summary>Gets or sets when the game ends if the countdown is not cancelled.</summary>
    public DateTimeOffset? PendingFinishAt { get; set; }

    /// <summary>Gets or sets the survivor whose countdown was cancelled; it does not restart for them.</summary>
    public string? SuppressedWinnerPlayerId { get; set; }

    /// <summary>Gets or sets the seated players, in seat order.</summary>
    public List<PlayerState> Players { get; set; } = [];

    /// <summary>Gets or sets the most recent actions, newest last.</summary>
    public List<GameLogEntry> Log { get; set; } = [];

    /// <summary>Gets or sets a counter incremented on every mutation.</summary>
    public long Version { get; set; }

    /// <summary>Finds a seated player.</summary>
    /// <param name="playerId">The player identifier.</param>
    /// <returns>The player or <see langword="null"/>.</returns>
    public PlayerState? FindPlayer(string playerId) => Players.Find(p => p.PlayerId == playerId);

    /// <summary>Finds a seated player by display name, ignoring case.</summary>
    /// <param name="name">The display name.</param>
    /// <returns>The player or <see langword="null"/>.</returns>
    public PlayerState? FindPlayerByName(string name) =>
        Players.Find(p => string.Equals(p.DisplayName, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>State of a single seated player.</summary>
public sealed class PlayerState
{
    /// <summary>Gets or sets the player identifier.</summary>
    public required string PlayerId { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Gets or sets the life total.</summary>
    public int Life { get; set; }

    /// <summary>Gets or sets the poison counters.</summary>
    public int Poison { get; set; }

    /// <summary>Gets or sets commander damage received, keyed by source player identifier.</summary>
    public Dictionary<string, int> CommanderDamage { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether the player conceded.</summary>
    public bool Conceded { get; set; }

    /// <summary>Gets or sets the commander the player chose, if any.</summary>
    public CommanderInfo? Commander { get; set; }

    /// <summary>Gets a value indicating whether the player has lost according to the rules.</summary>
    public bool IsEliminated =>
        Conceded
        || Life <= 0
        || Poison >= GameRules.LethalPoison
        || CommanderDamage.Values.Any(d => d >= GameRules.LethalCommanderDamage);
}

/// <summary>Commander card chosen by a player, from Scryfall.</summary>
/// <param name="Name">Card name.</param>
/// <param name="ArtUrl">Scryfall <c>art_crop</c> image URL.</param>
/// <param name="Artist">Illustrator, credited next to the art.</param>
public sealed record CommanderInfo(string Name, string ArtUrl, string Artist);

/// <summary>A single entry of the game action log.</summary>
/// <param name="At">When the action happened.</param>
/// <param name="ActorPlayerId">Who performed the action.</param>
/// <param name="Message">Machine-friendly action key.</param>
/// <param name="TargetPlayerId">Affected player, if any.</param>
/// <param name="Value">Numeric delta or value, if any.</param>
/// <param name="Detail">Extra information such as a source player.</param>
public sealed record GameLogEntry(
    DateTimeOffset At,
    string ActorPlayerId,
    string Message,
    string? TargetPlayerId = null,
    int? Value = null,
    string? Detail = null);
