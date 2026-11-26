using System.Collections.Concurrent;
using System.Text.Json;
using LifeCounterAssist.Api.Auth;
using LifeCounterAssist.Api.Domain;
using LifeCounterAssist.Api.Hubs;
using LifeCounterAssist.Api.Infrastructure;

namespace LifeCounterAssist.Api.Services;

/// <summary>Game creation payload.</summary>
/// <param name="MaxPlayers">Number of seats (2-8).</param>
/// <param name="StartingLife">Starting life, 40 by default.</param>
/// <param name="InvitedUserIds">Friends to invite.</param>
/// <param name="DeckId">Deck the host plays.</param>
public sealed record CreateGameRequest(int MaxPlayers = 4, int? StartingLife = null, List<string>? InvitedUserIds = null, string? DeckId = null);

/// <summary>Join payload.</summary>
/// <param name="DeckId">Deck the player plays.</param>
public sealed record JoinGameRequest(string? DeckId);

/// <summary>Join-by-code payload.</summary>
/// <param name="Code">Game join code.</param>
/// <param name="DeckId">Deck the player plays.</param>
public sealed record JoinByCodeRequest(string Code, string? DeckId);

/// <summary>Invite payload.</summary>
/// <param name="UserIds">Friends to invite.</param>
public sealed record InviteRequest(List<string> UserIds);

/// <summary>Invitation returned by the API.</summary>
/// <param name="GameId">Game identifier.</param>
/// <param name="HostUserId">Host identifier.</param>
/// <param name="HostName">Host name.</param>
/// <param name="CreatedAt">Invitation date.</param>
public sealed record InvitationDto(string GameId, string HostUserId, string HostName, DateTimeOffset CreatedAt);

/// <summary>History entry returned by the API.</summary>
/// <param name="GameId">Game identifier.</param>
/// <param name="Status">Game status.</param>
/// <param name="CreatedAt">Creation date.</param>
/// <param name="FinishedAt">Finish date.</param>
/// <param name="Won">Whether the user won.</param>
/// <param name="WinnerName">Winner name.</param>
/// <param name="PlayerCount">Number of players.</param>
/// <param name="PlayerNames">Player names.</param>
/// <param name="DeckName">Deck played.</param>
/// <param name="CommanderName">Commander played.</param>
/// <param name="FinalLife">Final life total.</param>
public sealed record HistoryEntryDto(
    string GameId, string Status, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt, bool Won, string? WinnerName,
    int PlayerCount, string[] PlayerNames, string? DeckName, string? CommanderName, int? FinalLife);

/// <summary>
/// Owns game state: serializes mutations per game, persists to Table Storage and broadcasts changes.
/// </summary>
/// <remarks>
/// State is cached in memory, so the API must run as a single replica (or use a SignalR backplane
/// and distributed locking before scaling out).
/// </remarks>
public sealed class GameService(
    TableStore store,
    UserService users,
    DeckService decks,
    FriendService friends,
    Notifier notifier,
    TimeProvider clock)
{
    private const int JoinCodeLength = 5;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, GameState> _cache = new();

    /// <summary>Creates a game with the caller as host and sends invitations.</summary>
    /// <param name="user">Current user.</param>
    /// <param name="request">Settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new game.</returns>
    public async Task<GameState> CreateAsync(CurrentUser user, CreateGameRequest request, CancellationToken cancellationToken = default)
    {
        var host = await BuildPlayerAsync(user, request.DeckId, cancellationToken);
        var invited = await FilterFriendsAsync(user.Id, request.InvitedUserIds, cancellationToken);
        var id = Guid.NewGuid().ToString("N");
        var joinCode = await ReserveJoinCodeAsync(id, cancellationToken);

        var state = GameRules.Create(
            id, joinCode, host, request.MaxPlayers, request.StartingLife ?? GameRules.CommanderStartingLife, invited, clock.GetUtcNow());

        await SaveAsync(state, cancellationToken);
        _cache[id] = state;
        await SyncPlayerGamesAsync(state, cancellationToken);
        await SendInvitationsAsync(state, host.DisplayName, state.InvitedUserIds, cancellationToken);
        return state;
    }

    /// <summary>Returns a game visible to the caller (player or invited).</summary>
    /// <param name="user">Current user.</param>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The game.</returns>
    /// <exception cref="ForbiddenException">Thrown when the caller cannot see the game.</exception>
    public async Task<GameState> GetForUserAsync(CurrentUser user, string gameId, CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(gameId, cancellationToken);
        if (state.FindPlayer(user.Id) is null && !state.InvitedUserIds.Contains(user.Id))
            throw new ForbiddenException("not_a_participant");
        return state;
    }

    /// <summary>Seats the caller in a lobby.</summary>
    /// <param name="user">Current user.</param>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="deckId">Deck to play.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated game.</returns>
    public async Task<GameState> JoinAsync(CurrentUser user, string gameId, string? deckId, CancellationToken cancellationToken = default)
    {
        var player = await BuildPlayerAsync(user, deckId, cancellationToken);
        var state = await MutateCoreAsync(gameId, s => GameRules.Join(s, player, clock.GetUtcNow()), cancellationToken);
        await store.DeleteAsync(Tables.Invitations, user.Id, gameId, cancellationToken);
        await SyncPlayerGamesAsync(state, cancellationToken);
        return state;
    }

    /// <summary>Seats the caller using a join code.</summary>
    /// <param name="user">Current user.</param>
    /// <param name="request">Code and deck.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated game.</returns>
    /// <exception cref="NotFoundException">Thrown when the code is unknown.</exception>
    public async Task<GameState> JoinByCodeAsync(CurrentUser user, JoinByCodeRequest request, CancellationToken cancellationToken = default)
    {
        var index = await store.GetAsync<JoinCodeEntity>(Tables.Games, JoinCodeEntity.Partition, Codes.Normalize(request.Code), cancellationToken)
            ?? throw new NotFoundException("game_not_found");
        return await JoinAsync(user, index.GameId, request.DeckId, cancellationToken);
    }

    /// <summary>Removes the caller from a lobby.</summary>
    /// <param name="user">Current user.</param>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task LeaveAsync(CurrentUser user, string gameId, CancellationToken cancellationToken = default)
    {
        await MutateCoreAsync(gameId, s => GameRules.Leave(s, user.Id, clock.GetUtcNow()), cancellationToken);
        await store.DeleteAsync(Tables.PlayerGames, user.Id, gameId, cancellationToken);
    }

    /// <summary>Invites more friends to a lobby.</summary>
    /// <param name="user">Current user (must be seated).</param>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="request">Friends to invite.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated game.</returns>
    public async Task<GameState> InviteAsync(CurrentUser user, string gameId, InviteRequest request, CancellationToken cancellationToken = default)
    {
        var invited = await FilterFriendsAsync(user.Id, request.UserIds, cancellationToken);
        List<string> added = [];
        var state = await MutateAsync(user, gameId, s =>
        {
            if (s.Status != GameStatus.Lobby) throw new DomainException("game_already_started");
            foreach (var id in invited.Where(id => s.FindPlayer(id) is null && !s.InvitedUserIds.Contains(id)))
            {
                s.InvitedUserIds.Add(id);
                added.Add(id);
            }

            s.Version++;
        }, cancellationToken);
        await SendInvitationsAsync(state, state.FindPlayer(user.Id)!.DisplayName, added, cancellationToken);
        return state;
    }

    /// <summary>Applies an action from a seated player, then persists and broadcasts the new state.</summary>
    /// <param name="user">Current user (must be seated).</param>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="action">Mutation to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated game.</returns>
    /// <exception cref="ForbiddenException">Thrown when the caller is not seated.</exception>
    public async Task<GameState> MutateAsync(CurrentUser user, string gameId, Action<GameState> action, CancellationToken cancellationToken = default)
    {
        var previousStatus = GameStatus.Lobby;
        var state = await MutateCoreAsync(gameId, s =>
        {
            if (s.FindPlayer(user.Id) is null) throw new ForbiddenException("not_a_player");
            previousStatus = s.Status;
            action(s);
        }, cancellationToken);

        if (state.Status != previousStatus)
        {
            await SyncPlayerGamesAsync(state, cancellationToken);
            if (state.Status == GameStatus.InProgress) await ClearInvitationsAsync(state, cancellationToken);
            if (state.Status == GameStatus.Finished) _cache.TryRemove(state.Id, out _);
        }

        return state;
    }

    /// <summary>Sets the deck a seated player uses.</summary>
    /// <param name="user">Current user.</param>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="deckId">Deck identifier, or <see langword="null"/> to clear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated game.</returns>
    public async Task<GameState> SelectDeckAsync(CurrentUser user, string gameId, string? deckId, CancellationToken cancellationToken = default)
    {
        var deck = await GetDeckInfoAsync(user.Id, deckId, cancellationToken);
        var state = await MutateAsync(user, gameId, s => GameRules.SelectDeck(s, user.Id, deck), cancellationToken);
        await SyncPlayerGamesAsync(state, cancellationToken);
        return state;
    }

    /// <summary>Lists pending invitations of the caller.</summary>
    /// <param name="user">Current user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Invitations, newest first.</returns>
    public async Task<List<InvitationDto>> ListInvitationsAsync(CurrentUser user, CancellationToken cancellationToken = default) =>
        (await store.QueryPartitionAsync<InvitationEntity>(Tables.Invitations, user.Id, cancellationToken))
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InvitationDto(i.RowKey, i.HostUserId, i.HostName, i.CreatedAt))
            .ToList();

    /// <summary>Declines an invitation.</summary>
    /// <param name="user">Current user.</param>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task DeclineInvitationAsync(CurrentUser user, string gameId, CancellationToken cancellationToken = default)
    {
        await store.DeleteAsync(Tables.Invitations, user.Id, gameId, cancellationToken);
        try
        {
            await MutateCoreAsync(gameId, s =>
            {
                if (s.InvitedUserIds.Remove(user.Id)) s.Version++;
            }, cancellationToken);
        }
        catch (NotFoundException)
        {
            // Game no longer exists; removing the invitation is enough.
        }
    }

    /// <summary>Lists the caller's games, newest first.</summary>
    /// <param name="user">Current user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>History entries.</returns>
    public async Task<List<HistoryEntryDto>> HistoryAsync(CurrentUser user, CancellationToken cancellationToken = default) =>
        (await store.QueryPartitionAsync<PlayerGameEntity>(Tables.PlayerGames, user.Id, cancellationToken))
            .OrderByDescending(g => g.CreatedAt)
            .Select(g => new HistoryEntryDto(
                g.RowKey, g.Status, g.CreatedAt, g.FinishedAt, g.Won, g.WinnerName, g.PlayerCount,
                g.PlayerNames.Split('\n', StringSplitOptions.RemoveEmptyEntries), g.DeckName, g.CommanderName, g.FinalLife))
            .ToList();

    private async Task<GameState> MutateCoreAsync(string gameId, Action<GameState> action, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(gameId, cancellationToken);

            // Mutate a copy so a failing rule or save never leaves a half-applied cached state.
            var working = Clone(state);
            var version = working.Version;
            action(working);
            if (working.Version == version) return state;

            await SaveAsync(working, cancellationToken);
            _cache[gameId] = working;
            await notifier.GameUpdatedAsync(working, cancellationToken);
            return working;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<GameState> LoadAsync(string gameId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(gameId, out var cached)) return cached;
        var entity = await store.GetAsync<GameEntity>(Tables.Games, gameId, GameEntity.StateRow, cancellationToken)
            ?? throw new NotFoundException("game_not_found");
        var state = JsonSerializer.Deserialize<GameState>(entity.StateJson, JsonDefaults.Options)
            ?? throw new NotFoundException("game_not_found");
        if (state.Status != GameStatus.Finished) _cache[gameId] = state;
        return state;
    }

    private Task SaveAsync(GameState state, CancellationToken cancellationToken) =>
        store.UpsertAsync(Tables.Games, new GameEntity
        {
            PartitionKey = state.Id,
            RowKey = GameEntity.StateRow,
            Status = state.Status.ToString(),
            StateJson = JsonSerializer.Serialize(state, JsonDefaults.Options),
        }, cancellationToken);

    private static GameState Clone(GameState state) =>
        JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state, JsonDefaults.Options), JsonDefaults.Options)!;

    private async Task<string> ReserveJoinCodeAsync(string gameId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = Codes.New(JoinCodeLength);
            var index = new JoinCodeEntity { PartitionKey = JoinCodeEntity.Partition, RowKey = code, GameId = gameId };
            if (await store.TryAddAsync(Tables.Games, index, cancellationToken)) return code;

            // Codes of finished games can be recycled.
            var existing = await store.GetAsync<JoinCodeEntity>(Tables.Games, JoinCodeEntity.Partition, code, cancellationToken);
            var game = existing is null ? null : await store.GetAsync<GameEntity>(Tables.Games, existing.GameId, GameEntity.StateRow, cancellationToken);
            if (game is null || game.Status == nameof(GameStatus.Finished))
            {
                await store.UpsertAsync(Tables.Games, index, cancellationToken);
                return code;
            }
        }

        throw new InvalidOperationException("Could not allocate a join code.");
    }

    private async Task<PlayerState> BuildPlayerAsync(CurrentUser user, string? deckId, CancellationToken cancellationToken)
    {
        var profile = await users.EnsureProfileAsync(user, cancellationToken);
        var deck = await GetDeckInfoAsync(user.Id, deckId, cancellationToken);
        return new PlayerState
        {
            UserId = user.Id,
            DisplayName = profile.DisplayName,
            DeckId = deck?.Id,
            DeckName = deck?.Name,
            CommanderName = deck?.CommanderName,
            CommanderImageUrl = deck?.CommanderImageUrl,
        };
    }

    private async Task<DeckInfo?> GetDeckInfoAsync(string userId, string? deckId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deckId)) return null;
        var deck = await decks.GetAsync(userId, deckId, cancellationToken) ?? throw new NotFoundException("deck_not_found");
        return new DeckInfo(deck.RowKey, deck.Name, deck.CommanderName, deck.CommanderImageUrl);
    }

    private async Task<List<string>> FilterFriendsAsync(string userId, List<string>? candidates, CancellationToken cancellationToken)
    {
        List<string> result = [];
        foreach (var id in (candidates ?? []).Distinct().Take(GameRules.MaxPlayersLimit * 2))
        {
            if (await friends.AreFriendsAsync(userId, id, cancellationToken)) result.Add(id);
        }

        return result;
    }

    private async Task SendInvitationsAsync(GameState state, string hostName, IEnumerable<string> userIds, CancellationToken cancellationToken)
    {
        foreach (var id in userIds)
        {
            var invitation = new InvitationEntity
            {
                PartitionKey = id,
                RowKey = state.Id,
                HostUserId = state.HostUserId,
                HostName = hostName,
                CreatedAt = clock.GetUtcNow(),
            };
            await store.UpsertAsync(Tables.Invitations, invitation, cancellationToken);
            await notifier.InvitationsChangedAsync(id, cancellationToken);
        }
    }

    private async Task ClearInvitationsAsync(GameState state, CancellationToken cancellationToken)
    {
        // Rules clear InvitedUserIds on start, so remove every invitation row pointing at this game.
        await foreach (var invitation in store.Table(Tables.Invitations)
            .QueryAsync<InvitationEntity>(i => i.RowKey == state.Id, cancellationToken: cancellationToken))
        {
            await store.DeleteAsync(Tables.Invitations, invitation.PartitionKey, invitation.RowKey, cancellationToken);
            await notifier.InvitationsChangedAsync(invitation.PartitionKey, cancellationToken);
        }
    }

    private async Task SyncPlayerGamesAsync(GameState state, CancellationToken cancellationToken)
    {
        var winner = state.WinnerUserId is null ? null : state.FindPlayer(state.WinnerUserId);
        var names = string.Join('\n', state.Players.Select(p => p.DisplayName));
        foreach (var player in state.Players)
        {
            var entity = new PlayerGameEntity
            {
                PartitionKey = player.UserId,
                RowKey = state.Id,
                Status = state.Status.ToString(),
                CreatedAt = state.CreatedAt,
                FinishedAt = state.FinishedAt,
                Won = state.WinnerUserId == player.UserId,
                WinnerName = winner?.DisplayName,
                PlayerCount = state.Players.Count,
                PlayerNames = names,
                DeckName = player.DeckName,
                CommanderName = player.CommanderName,
                FinalLife = state.Status == GameStatus.Finished ? player.Life : null,
            };
            await store.UpsertAsync(Tables.PlayerGames, entity, cancellationToken);
        }
    }
}
