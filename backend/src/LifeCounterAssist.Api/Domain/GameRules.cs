namespace LifeCounterAssist.Api.Domain;

/// <summary>Pure rule functions mutating a <see cref="GameState"/>.</summary>
/// <remarks>Callers are responsible for synchronization.</remarks>
public static class GameRules
{
    /// <summary>Commander damage from a single source that eliminates a player.</summary>
    public const int LethalCommanderDamage = 21;

    /// <summary>Poison counters that eliminate a player.</summary>
    public const int LethalPoison = 10;

    /// <summary>Minimum number of seats.</summary>
    public const int MinPlayers = 2;

    /// <summary>Maximum number of seats.</summary>
    public const int MaxPlayers = 8;

    /// <summary>Maximum display name length.</summary>
    public const int MaxNameLength = 20;

    /// <summary>Number of log entries kept in the shared state.</summary>
    public const int MaxLogEntries = 100;

    /// <summary>Delay between the last opponent falling and the game ending.</summary>
    public static readonly TimeSpan FinishCountdown = TimeSpan.FromSeconds(10);

    /// <summary>Only Scryfall art crops may be shown on the room screen.</summary>
    public const string CommanderArtPrefix = "https://cards.scryfall.io/art_crop/";

    private const int MaxCommanderNameLength = 150;
    private const int MaxCommanderArtistLength = 100;
    private const int MaxCommanderArtUrlLength = 300;

    private static readonly int[] AllowedStartingLife = [20, 25, 30, 40];

    /// <summary>Trims and validates a commander choice.</summary>
    /// <param name="commander">Raw commander, or <see langword="null"/> for none.</param>
    /// <returns>The trimmed commander, or <see langword="null"/>.</returns>
    /// <exception cref="DomainException">Thrown when the name, artist or art URL is invalid.</exception>
    public static CommanderInfo? NormalizeCommander(CommanderInfo? commander)
    {
        if (commander is null) return null;
        var name = commander.Name?.Trim() ?? "";
        var artist = commander.Artist?.Trim() ?? "";
        var url = commander.ArtUrl ?? "";
        if (name.Length is 0 or > MaxCommanderNameLength
            || artist.Length > MaxCommanderArtistLength
            || url.Length > MaxCommanderArtUrlLength
            || !url.StartsWith(CommanderArtPrefix, StringComparison.Ordinal))
            throw new DomainException("invalid_commander");
        return new CommanderInfo(name, url, artist);
    }

    /// <summary>Trims and validates a display name.</summary>
    /// <param name="name">Raw name.</param>
    /// <returns>The trimmed name.</returns>
    /// <exception cref="DomainException">Thrown when the name is empty or too long.</exception>
    public static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length is 0 or > MaxNameLength) throw new DomainException("invalid_name");
        return trimmed;
    }

    /// <summary>Creates a new game in lobby with the host seated.</summary>
    /// <param name="id">Game identifier.</param>
    /// <param name="hostPlayerId">Host player identifier.</param>
    /// <param name="hostName">Host display name.</param>
    /// <param name="startingLife">Starting life total.</param>
    /// <param name="now">Current time.</param>
    /// <param name="commander">Host commander, if any.</param>
    /// <returns>The new game.</returns>
    /// <exception cref="DomainException">Thrown when settings are invalid.</exception>
    public static GameState Create(string id, string hostPlayerId, string hostName, int startingLife, DateTimeOffset now, CommanderInfo? commander = null)
    {
        if (!AllowedStartingLife.Contains(startingLife)) throw new DomainException("invalid_starting_life");
        var name = NormalizeName(hostName);
        var hostCommander = NormalizeCommander(commander);

        var state = new GameState
        {
            Id = id,
            HostPlayerId = hostPlayerId,
            StartingLife = startingLife,
            CreatedAt = now,
            LastActivityAt = now,
            Players = [new PlayerState { PlayerId = hostPlayerId, DisplayName = name, Life = startingLife, Commander = hostCommander }],
        };
        AddLog(state, new GameLogEntry(now, hostPlayerId, "created"), now);
        return state;
    }

    /// <summary>Seats a player, or hands back the existing seat when the name is already taken.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="newPlayerId">Identifier for a newly created seat.</param>
    /// <param name="name">Display name.</param>
    /// <param name="now">Current time.</param>
    /// <param name="commander">Commander to set; on a reclaim, <see langword="null"/> keeps the current one.</param>
    /// <returns>The seat and whether it was reclaimed.</returns>
    /// <exception cref="DomainException">Thrown when the game is over, started, or full, or the commander is invalid.</exception>
    public static (PlayerState Player, bool Reclaimed) Join(GameState state, string newPlayerId, string name, DateTimeOffset now, CommanderInfo? commander = null)
    {
        var normalized = NormalizeName(name);
        var chosen = NormalizeCommander(commander);
        if (state.Status is GameStatus.Finished or GameStatus.Cancelled) throw new DomainException("game_over");

        var existing = state.FindPlayerByName(normalized);
        if (existing is not null)
        {
            if (chosen is not null) existing.Commander = chosen;
            return (existing, true);
        }

        if (state.Status != GameStatus.Lobby) throw new DomainException("game_already_started");
        if (state.Players.Count >= MaxPlayers) throw new DomainException("game_full");

        var player = new PlayerState { PlayerId = newPlayerId, DisplayName = normalized, Life = state.StartingLife, Commander = chosen };
        state.Players.Add(player);
        AddLog(state, new GameLogEntry(now, newPlayerId, "joined"), now);
        return (player, false);
    }

    /// <summary>Removes a player from a lobby. The host cannot leave.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="playerId">Player leaving.</param>
    /// <param name="now">Current time.</param>
    /// <exception cref="DomainException">Thrown when the game already started or the host leaves.</exception>
    public static void Leave(GameState state, string playerId, DateTimeOffset now)
    {
        if (state.Status != GameStatus.Lobby) throw new DomainException("game_already_started");
        if (playerId == state.HostPlayerId) throw new DomainException("host_cannot_leave");
        if (state.Players.RemoveAll(p => p.PlayerId == playerId) > 0)
            AddLog(state, new GameLogEntry(now, playerId, "left"), now);
    }

    /// <summary>Cancels a lobby game (host only).</summary>
    /// <param name="state">Game state.</param>
    /// <param name="actorPlayerId">Must be the host.</param>
    /// <param name="now">Current time.</param>
    /// <exception cref="ForbiddenException">Thrown when the actor is not the host.</exception>
    /// <exception cref="DomainException">Thrown when the game is not in lobby.</exception>
    public static void Cancel(GameState state, string actorPlayerId, DateTimeOffset now)
    {
        if (actorPlayerId != state.HostPlayerId) throw new ForbiddenException("host_only");
        if (state.Status != GameStatus.Lobby) throw new DomainException("game_already_started");

        state.Status = GameStatus.Cancelled;
        state.FinishedAt = now;
        AddLog(state, new GameLogEntry(now, actorPlayerId, "cancelled"), now);
    }

    /// <summary>Moves a lobby game to in-progress.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="actorPlayerId">Must be the host.</param>
    /// <param name="now">Current time.</param>
    /// <exception cref="ForbiddenException">Thrown when the actor is not the host.</exception>
    /// <exception cref="DomainException">Thrown when not enough players are seated.</exception>
    public static void Start(GameState state, string actorPlayerId, DateTimeOffset now)
    {
        if (actorPlayerId != state.HostPlayerId) throw new ForbiddenException("host_only");
        if (state.Status != GameStatus.Lobby) throw new DomainException("game_already_started");
        if (state.Players.Count < MinPlayers) throw new DomainException("not_enough_players");

        state.Status = GameStatus.InProgress;
        state.StartedAt = now;
        AddLog(state, new GameLogEntry(now, actorPlayerId, "started"), now);
    }

    /// <summary>Adds a delta to a player's life.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="actor">Player performing the change.</param>
    /// <param name="target">Player whose life changes.</param>
    /// <param name="delta">Signed delta.</param>
    /// <param name="now">Current time.</param>
    public static void AdjustLife(GameState state, string actor, string target, int delta, DateTimeOffset now)
    {
        EnsureInProgress(state);
        var player = GetPlayer(state, target);
        if (delta == 0) return;
        player.Life += delta;
        AddLog(state, new GameLogEntry(now, actor, "life", target, delta), now);
        RefreshPendingFinish(state, now);
    }

    /// <summary>
    /// Adds commander damage dealt by the commander of <paramref name="source"/>.
    /// Commander damage is also life loss, so life is adjusted by the opposite delta.
    /// </summary>
    /// <param name="state">Game state.</param>
    /// <param name="actor">Player performing the change.</param>
    /// <param name="target">Player receiving damage.</param>
    /// <param name="source">Owner of the commander dealing damage.</param>
    /// <param name="delta">Signed delta of damage.</param>
    /// <param name="now">Current time.</param>
    /// <exception cref="DomainException">Thrown when target and source are the same player.</exception>
    public static void AdjustCommanderDamage(
        GameState state, string actor, string target, string source, int delta, DateTimeOffset now)
    {
        EnsureInProgress(state);
        if (delta == 0) return;
        if (target == source) throw new DomainException("invalid_commander_source");
        var player = GetPlayer(state, target);
        GetPlayer(state, source);

        var current = player.CommanderDamage.GetValueOrDefault(source);
        var next = Math.Max(0, current + delta);
        var applied = next - current;
        if (applied == 0) return;

        player.CommanderDamage[source] = next;
        player.Life -= applied;
        AddLog(state, new GameLogEntry(now, actor, "commander_damage", target, applied, source), now);
        RefreshPendingFinish(state, now);
    }

    /// <summary>Adds a delta to a player's poison counters (never below zero).</summary>
    /// <param name="state">Game state.</param>
    /// <param name="actor">Player performing the change.</param>
    /// <param name="target">Player whose counter changes.</param>
    /// <param name="delta">Signed delta.</param>
    /// <param name="now">Current time.</param>
    public static void AdjustPoison(GameState state, string actor, string target, int delta, DateTimeOffset now)
    {
        EnsureInProgress(state);
        var player = GetPlayer(state, target);
        var after = Math.Max(0, player.Poison + delta);
        var applied = after - player.Poison;
        if (applied == 0) return;

        player.Poison = after;
        AddLog(state, new GameLogEntry(now, actor, "poison", target, applied), now);
        RefreshPendingFinish(state, now);
    }

    /// <summary>Toggles whether a player conceded.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="actor">Player performing the change.</param>
    /// <param name="target">Player conceding or un-conceding.</param>
    /// <param name="now">Current time.</param>
    public static void ToggleConcede(GameState state, string actor, string target, DateTimeOffset now)
    {
        EnsureInProgress(state);
        var player = GetPlayer(state, target);
        player.Conceded = !player.Conceded;
        AddLog(state, new GameLogEntry(now, actor, player.Conceded ? "conceded" : "unconceded", target), now);
        RefreshPendingFinish(state, now);
    }

    /// <summary>Cancels the running finish countdown; it will not restart for the same survivor.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="actor">Player cancelling.</param>
    /// <param name="now">Current time.</param>
    public static void CancelPendingFinish(GameState state, string actor, DateTimeOffset now)
    {
        EnsureInProgress(state);
        if (state.PendingWinnerPlayerId is null) return;

        state.SuppressedWinnerPlayerId = state.PendingWinnerPlayerId;
        ClearPending(state);
        AddLog(state, new GameLogEntry(now, actor, "finish_cancelled"), now);
    }

    /// <summary>Ends the game manually, without a winner.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="actor">Player ending the game.</param>
    /// <param name="now">Current time.</param>
    public static void Finish(GameState state, string actor, DateTimeOffset now)
    {
        EnsureInProgress(state);
        state.Status = GameStatus.Finished;
        state.FinishedAt = now;
        state.WinnerPlayerId = null;
        ClearPending(state);
        AddLog(state, new GameLogEntry(now, actor, "finished"), now);
    }

    /// <summary>Ends the game with the pending winner once the countdown has elapsed.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="now">Current time.</param>
    /// <returns><see langword="true"/> when the game has just finished.</returns>
    public static bool CompletePendingFinish(GameState state, DateTimeOffset now)
    {
        if (state.Status != GameStatus.InProgress || state.PendingFinishAt is null || state.PendingFinishAt > now)
            return false;

        var winner = state.PendingWinnerPlayerId;
        state.Status = GameStatus.Finished;
        state.WinnerPlayerId = winner;
        state.FinishedAt = now;
        ClearPending(state);
        AddLog(state, new GameLogEntry(now, winner ?? "", "finished"), now);
        return true;
    }

    private static void RefreshPendingFinish(GameState state, DateTimeOffset now)
    {
        var alive = state.Players.Where(p => !p.IsEliminated).ToList();
        if (alive.Count == 1)
        {
            var winner = alive[0].PlayerId;
            if (winner == state.SuppressedWinnerPlayerId || winner == state.PendingWinnerPlayerId) return;
            state.PendingWinnerPlayerId = winner;
            state.PendingFinishAt = now + FinishCountdown;
        }
        else
        {
            ClearPending(state);
            state.SuppressedWinnerPlayerId = null;
        }
    }

    private static void ClearPending(GameState state)
    {
        state.PendingWinnerPlayerId = null;
        state.PendingFinishAt = null;
    }

    private static PlayerState GetPlayer(GameState state, string playerId) =>
        state.FindPlayer(playerId) ?? throw new NotFoundException("player_not_found");

    private static void EnsureInProgress(GameState state)
    {
        if (state.Status != GameStatus.InProgress) throw new DomainException("game_not_in_progress");
    }

    private static void AddLog(GameState state, GameLogEntry entry, DateTimeOffset now)
    {
        state.Log.Add(entry);
        if (state.Log.Count > MaxLogEntries)
            state.Log.RemoveRange(0, state.Log.Count - MaxLogEntries);
        state.LastActivityAt = now;
        state.Version++;
    }
}
