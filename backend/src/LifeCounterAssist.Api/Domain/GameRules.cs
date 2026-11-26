namespace LifeCounterAssist.Api.Domain;

/// <summary>Pure rule functions mutating a <see cref="GameState"/>.</summary>
/// <remarks>Callers are responsible for synchronization and persistence.</remarks>
public static class GameRules
{
    /// <summary>Default Commander starting life.</summary>
    public const int CommanderStartingLife = 40;

    /// <summary>Commander damage from a single source that eliminates a player.</summary>
    public const int LethalCommanderDamage = 21;

    /// <summary>Poison counters that eliminate a player.</summary>
    public const int LethalPoison = 10;

    /// <summary>Minimum number of seats.</summary>
    public const int MinPlayers = 2;

    /// <summary>Maximum number of seats.</summary>
    public const int MaxPlayersLimit = 8;

    /// <summary>Number of log entries kept in the shared state.</summary>
    public const int MaxLogEntries = 100;

    /// <summary>Creates a new game in lobby with the host seated.</summary>
    /// <param name="id">Game identifier.</param>
    /// <param name="joinCode">Join code.</param>
    /// <param name="host">The host player.</param>
    /// <param name="maxPlayers">Number of seats.</param>
    /// <param name="startingLife">Starting life total.</param>
    /// <param name="invitedUserIds">Users invited to the game.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The new game.</returns>
    /// <exception cref="DomainException">Thrown when settings are out of range.</exception>
    public static GameState Create(
        string id,
        string joinCode,
        PlayerState host,
        int maxPlayers,
        int startingLife,
        IEnumerable<string> invitedUserIds,
        DateTimeOffset now)
    {
        if (maxPlayers is < MinPlayers or > MaxPlayersLimit)
            throw new DomainException("invalid_player_count");
        if (startingLife is < 1 or > 999)
            throw new DomainException("invalid_starting_life");

        host.Life = startingLife;
        var state = new GameState
        {
            Id = id,
            JoinCode = joinCode,
            HostUserId = host.UserId,
            MaxPlayers = maxPlayers,
            StartingLife = startingLife,
            CreatedAt = now,
            Players = [host],
            InvitedUserIds = invitedUserIds.Where(u => u != host.UserId).Distinct().ToList(),
        };
        AddLog(state, new GameLogEntry(now, host.UserId, "created"));
        return state;
    }

    /// <summary>Seats a player in a lobby. Joining twice is a no-op.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="player">Player to seat.</param>
    /// <param name="now">Current time.</param>
    /// <exception cref="DomainException">Thrown when the game is not in lobby or full.</exception>
    public static void Join(GameState state, PlayerState player, DateTimeOffset now)
    {
        if (state.FindPlayer(player.UserId) is not null) return;
        if (state.Status != GameStatus.Lobby) throw new DomainException("game_already_started");
        if (state.Players.Count >= state.MaxPlayers) throw new DomainException("game_full");

        player.Life = state.StartingLife;
        state.Players.Add(player);
        state.InvitedUserIds.Remove(player.UserId);
        AddLog(state, new GameLogEntry(now, player.UserId, "joined"));
    }

    /// <summary>Removes a player from a lobby. The host cannot leave.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="userId">Player leaving.</param>
    /// <param name="now">Current time.</param>
    /// <exception cref="DomainException">Thrown when the game already started or the host leaves.</exception>
    public static void Leave(GameState state, string userId, DateTimeOffset now)
    {
        if (state.Status != GameStatus.Lobby) throw new DomainException("game_already_started");
        if (userId == state.HostUserId) throw new DomainException("host_cannot_leave");
        if (state.Players.RemoveAll(p => p.UserId == userId) > 0)
            AddLog(state, new GameLogEntry(now, userId, "left"));
    }

    /// <summary>Sets the deck a player uses.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="userId">Player choosing the deck.</param>
    /// <param name="deck">Deck info, or <see langword="null"/> to clear.</param>
    /// <exception cref="DomainException">Thrown when the game is finished.</exception>
    public static void SelectDeck(GameState state, string userId, DeckInfo? deck)
    {
        EnsureNotFinished(state);
        var player = GetPlayer(state, userId);
        player.DeckId = deck?.Id;
        player.DeckName = deck?.Name;
        player.CommanderName = deck?.CommanderName;
        player.CommanderImageUrl = deck?.CommanderImageUrl;
        state.Version++;
    }

    /// <summary>Moves a lobby game to in-progress.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="actorUserId">Must be the host.</param>
    /// <param name="now">Current time.</param>
    /// <exception cref="ForbiddenException">Thrown when the actor is not the host.</exception>
    /// <exception cref="DomainException">Thrown when not enough players are seated.</exception>
    public static void Start(GameState state, string actorUserId, DateTimeOffset now)
    {
        if (actorUserId != state.HostUserId) throw new ForbiddenException("host_only");
        if (state.Status != GameStatus.Lobby) throw new DomainException("game_already_started");
        if (state.Players.Count < MinPlayers) throw new DomainException("not_enough_players");

        state.Status = GameStatus.InProgress;
        state.StartedAt = now;
        state.InvitedUserIds.Clear();
        AddLog(state, new GameLogEntry(now, actorUserId, "started"));
    }

    /// <summary>Adds a delta to a player's life.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="actorUserId">Player performing the change.</param>
    /// <param name="targetUserId">Player whose life changes.</param>
    /// <param name="delta">Signed delta.</param>
    /// <param name="now">Current time.</param>
    public static void AdjustLife(GameState state, string actorUserId, string targetUserId, int delta, DateTimeOffset now)
    {
        EnsureInProgress(state);
        if (delta == 0) return;
        var target = GetPlayer(state, targetUserId);
        target.Life += delta;
        AddLog(state, new GameLogEntry(now, actorUserId, "life", targetUserId, delta));
    }

    /// <summary>
    /// Adds commander damage dealt by <paramref name="sourceUserId"/>'s commander.
    /// Commander damage is also life loss, so life is adjusted by the opposite delta.
    /// </summary>
    /// <param name="state">Game state.</param>
    /// <param name="actorUserId">Player performing the change.</param>
    /// <param name="targetUserId">Player receiving damage.</param>
    /// <param name="sourceUserId">Owner of the commander dealing damage.</param>
    /// <param name="delta">Signed delta of damage.</param>
    /// <param name="now">Current time.</param>
    /// <exception cref="DomainException">Thrown when target and source are the same player.</exception>
    public static void AdjustCommanderDamage(
        GameState state, string actorUserId, string targetUserId, string sourceUserId, int delta, DateTimeOffset now)
    {
        EnsureInProgress(state);
        if (delta == 0) return;
        if (targetUserId == sourceUserId) throw new DomainException("invalid_commander_source");
        var target = GetPlayer(state, targetUserId);
        GetPlayer(state, sourceUserId);

        var current = target.CommanderDamage.GetValueOrDefault(sourceUserId);
        var next = Math.Max(0, current + delta);
        var applied = next - current;
        if (applied == 0) return;

        target.CommanderDamage[sourceUserId] = next;
        target.Life -= applied;
        AddLog(state, new GameLogEntry(now, actorUserId, "commander_damage", targetUserId, applied, sourceUserId));
    }

    /// <summary>Adds a delta to a non-negative player counter.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="actorUserId">Player performing the change.</param>
    /// <param name="targetUserId">Player whose counter changes.</param>
    /// <param name="counter">Counter type.</param>
    /// <param name="delta">Signed delta.</param>
    /// <param name="now">Current time.</param>
    public static void AdjustCounter(
        GameState state, string actorUserId, string targetUserId, CounterType counter, int delta, DateTimeOffset now)
    {
        EnsureInProgress(state);
        var target = GetPlayer(state, targetUserId);
        int Apply(int value) => Math.Max(0, value + delta);

        var before = counter switch
        {
            CounterType.Poison => target.Poison,
            CounterType.Energy => target.Energy,
            CounterType.Experience => target.Experience,
            CounterType.CommanderTax => target.CommanderTax,
            _ => throw new DomainException("invalid_counter"),
        };
        var after = Apply(before);
        if (after == before) return;

        switch (counter)
        {
            case CounterType.Poison: target.Poison = after; break;
            case CounterType.Energy: target.Energy = after; break;
            case CounterType.Experience: target.Experience = after; break;
            case CounterType.CommanderTax: target.CommanderTax = after; break;
        }

        AddLog(state, new GameLogEntry(now, actorUserId, "counter", targetUserId, after - before, counter.ToString()));
    }

    /// <summary>Gives or clears the monarch designation.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="actorUserId">Player performing the change.</param>
    /// <param name="targetUserId">New monarch, or <see langword="null"/> to clear.</param>
    /// <param name="now">Current time.</param>
    public static void SetMonarch(GameState state, string actorUserId, string? targetUserId, DateTimeOffset now)
    {
        EnsureInProgress(state);
        if (targetUserId is not null) GetPlayer(state, targetUserId);
        state.MonarchUserId = targetUserId;
        AddLog(state, new GameLogEntry(now, actorUserId, "monarch", targetUserId));
    }

    /// <summary>Gives or clears the initiative.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="actorUserId">Player performing the change.</param>
    /// <param name="targetUserId">Player taking the initiative, or <see langword="null"/> to clear.</param>
    /// <param name="now">Current time.</param>
    public static void SetInitiative(GameState state, string actorUserId, string? targetUserId, DateTimeOffset now)
    {
        EnsureInProgress(state);
        if (targetUserId is not null) GetPlayer(state, targetUserId);
        state.InitiativeUserId = targetUserId;
        AddLog(state, new GameLogEntry(now, actorUserId, "initiative", targetUserId));
    }

    /// <summary>Toggles whether a player conceded.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="actorUserId">Player performing the change.</param>
    /// <param name="targetUserId">Player conceding or un-conceding.</param>
    /// <param name="now">Current time.</param>
    public static void ToggleConcede(GameState state, string actorUserId, string targetUserId, DateTimeOffset now)
    {
        EnsureInProgress(state);
        var target = GetPlayer(state, targetUserId);
        target.Conceded = !target.Conceded;
        AddLog(state, new GameLogEntry(now, actorUserId, target.Conceded ? "conceded" : "unconceded", targetUserId));
    }

    /// <summary>Ends the game with an optional winner (draw when <see langword="null"/>).</summary>
    /// <param name="state">Game state.</param>
    /// <param name="actorUserId">Player ending the game.</param>
    /// <param name="winnerUserId">Winner, or <see langword="null"/> for a draw.</param>
    /// <param name="now">Current time.</param>
    public static void Finish(GameState state, string actorUserId, string? winnerUserId, DateTimeOffset now)
    {
        EnsureInProgress(state);
        if (winnerUserId is not null) GetPlayer(state, winnerUserId);
        state.Status = GameStatus.Finished;
        state.FinishedAt = now;
        state.WinnerUserId = winnerUserId;
        AddLog(state, new GameLogEntry(now, actorUserId, "finished", winnerUserId));
    }

    /// <summary>Returns the only remaining player when everyone else is eliminated.</summary>
    /// <param name="state">Game state.</param>
    /// <returns>The last player standing, or <see langword="null"/>.</returns>
    public static string? SuggestWinner(GameState state)
    {
        var alive = state.Players.Where(p => !p.IsEliminated).ToList();
        return alive.Count == 1 ? alive[0].UserId : null;
    }

    private static PlayerState GetPlayer(GameState state, string userId) =>
        state.FindPlayer(userId) ?? throw new NotFoundException("player_not_found");

    private static void EnsureInProgress(GameState state)
    {
        if (state.Status != GameStatus.InProgress) throw new DomainException("game_not_in_progress");
    }

    private static void EnsureNotFinished(GameState state)
    {
        if (state.Status == GameStatus.Finished) throw new DomainException("game_finished");
    }

    private static void AddLog(GameState state, GameLogEntry entry)
    {
        state.Log.Add(entry);
        if (state.Log.Count > MaxLogEntries)
            state.Log.RemoveRange(0, state.Log.Count - MaxLogEntries);
        state.Version++;
    }
}

/// <summary>Deck information copied into a player's seat.</summary>
/// <param name="Id">Deck identifier.</param>
/// <param name="Name">Deck name.</param>
/// <param name="CommanderName">Commander name.</param>
/// <param name="CommanderImageUrl">Commander art URL.</param>
public sealed record DeckInfo(string Id, string Name, string? CommanderName, string? CommanderImageUrl);
