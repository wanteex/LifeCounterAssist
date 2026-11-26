using System.Text.Json;
using LifeCounterAssist.Api.Domain;
using LifeCounterAssist.Api.Infrastructure;
using LifeCounterAssist.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace LifeCounterAssist.Api.Tests;

public sealed class FakeNotifier : IGameNotifier
{
    public List<GameState> Updated { get; } = [];
    public List<IReadOnlyList<GameSummaryDto>> Lists { get; } = [];
    public List<GameState?> Screens { get; } = [];
    public bool Throw { get; set; }

    public Task GameUpdatedAsync(GameState state, CancellationToken ct) { if (Throw) throw new InvalidOperationException("boom"); Updated.Add(state); return Task.CompletedTask; }
    public Task ListChangedAsync(IReadOnlyList<GameSummaryDto> games, CancellationToken ct) { if (Throw) throw new InvalidOperationException("boom"); Lists.Add(games); return Task.CompletedTask; }
    public Task ScreenChangedAsync(GameState? featured, CancellationToken ct) { if (Throw) throw new InvalidOperationException("boom"); Screens.Add(featured); return Task.CompletedTask; }
}

public class GameServiceTests
{
    private readonly FakeNotifier _notifier = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly GameService _service;

    public GameServiceTests() => _service = new GameService(_notifier, _clock, NullLogger<GameService>.Instance);

    private async Task<SeatDto> StartedGameAsync(string host = "Host", string guest = "Guest")
    {
        var seat = await _service.CreateAsync(new CreateGameRequest(host));
        await _service.JoinAsync(seat.Game.Id, new JoinGameRequest(guest));
        await _service.ActAsync(seat.Game.Id, seat.SeatToken, (s, p) => GameRules.Start(s, p, _clock.GetUtcNow()));
        return seat;
    }

    [Fact]
    public async Task Create_ReturnsSeatTokenOf32HexChars()
    {
        var seat = await _service.CreateAsync(new CreateGameRequest("Host"));
        Assert.Equal(32, seat.SeatToken.Length);
        Assert.Matches("^[0-9a-f]{32}$", seat.SeatToken);
    }

    [Fact]
    public async Task Create_ReturnsGameIdOf32HexChars()
    {
        var seat = await _service.CreateAsync(new CreateGameRequest("Host"));
        Assert.Matches("^[0-9a-f]{32}$", seat.Game.Id);
    }

    [Fact]
    public async Task Act_WithThrowingNotifier_DoesNotThrowAndStateIsUpdated()
    {
        var seat = await StartedGameAsync();
        _notifier.Throw = true;
        var target = seat.PlayerId;

        var state = await _service.ActAsync(seat.Game.Id, seat.SeatToken,
            (s, p) => GameRules.AdjustLife(s, p, target, -5, _clock.GetUtcNow()));

        Assert.Equal(35, state.Players.Single(p => p.PlayerId == target).Life);
        Assert.Equal(35, _service.Get(seat.Game.Id).Players.Single(p => p.PlayerId == target).Life);
    }

    [Fact]
    public async Task Tick_WithThrowingNotifier_StillCompletesAllCountdowns()
    {
        var a = await StartedGameAsync("A1", "A2");
        var b = await StartedGameAsync("B1", "B2");
        foreach (var seat in new[] { a, b })
            await _service.ActAsync(seat.Game.Id, seat.SeatToken, (s, p) =>
                GameRules.ToggleConcede(s, p, s.Players.First(x => x.PlayerId != p).PlayerId, _clock.GetUtcNow()));
        _notifier.Throw = true;

        _clock.Advance(TimeSpan.FromSeconds(11));
        await _service.TickAsync();

        Assert.All(new[] { a, b }, seat => Assert.Equal(GameStatus.Finished, _service.Get(seat.Game.Id).Status));
    }

    [Fact]
    public async Task SerializedState_NeverContainsSeatToken()
    {
        var seat = await _service.CreateAsync(new CreateGameRequest("Host"));
        Assert.DoesNotContain(seat.SeatToken, JsonSerializer.Serialize(seat.Game, JsonDefaults.Options));
        Assert.DoesNotContain(seat.SeatToken, JsonSerializer.Serialize(_service.Get(seat.Game.Id), JsonDefaults.Options));
    }

    [Fact]
    public async Task Act_WithUnknownToken_ThrowsInvalidSeat()
    {
        var seat = await _service.CreateAsync(new CreateGameRequest("Host"));
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.ActAsync(seat.Game.Id, "nope", (_, _) => { }));
        Assert.Equal("invalid_seat", ex.Code);
    }

    [Fact]
    public async Task Act_WithUnknownGame_ThrowsGameNotFound()
    {
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _service.ActAsync("zzz", "t", (_, _) => { }));
        Assert.Equal("game_not_found", ex.Code);
    }

    [Fact]
    public async Task Join_SameName_ReturnsSamePlayerIdAndNewToken_OldTokenRejected()
    {
        var host = await _service.CreateAsync(new CreateGameRequest("Host"));
        var first = await _service.JoinAsync(host.Game.Id, new JoinGameRequest("Bob"));
        var again = await _service.JoinAsync(host.Game.Id, new JoinGameRequest("bob"));

        Assert.Equal(first.PlayerId, again.PlayerId);
        Assert.NotEqual(first.SeatToken, again.SeatToken);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.ActAsync(host.Game.Id, first.SeatToken, (_, _) => { }));
        await _service.ActAsync(host.Game.Id, again.SeatToken, (_, _) => { });
    }

    [Fact]
    public async Task Act_BroadcastsGameListAndScreen()
    {
        var seat = await _service.CreateAsync(new CreateGameRequest("Host"));
        _notifier.Updated.Clear(); _notifier.Lists.Clear(); _notifier.Screens.Clear();

        await _service.ActAsync(seat.Game.Id, seat.SeatToken, (_, _) => { });

        Assert.Single(_notifier.Updated);
        Assert.Single(_notifier.Lists);
        Assert.Single(_notifier.Screens);
    }

    [Fact]
    public async Task Featured_PrefersInProgressOverNewerLobby()
    {
        var started = await StartedGameAsync();
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _service.CreateAsync(new CreateGameRequest("Other"));

        Assert.Equal(started.Game.Id, _service.GetFeatured()?.Id);
    }

    [Fact]
    public async Task Featured_ShowsFinishedGameFor30SecondsThenNextLobby()
    {
        var seat = await StartedGameAsync();
        var lobby = await _service.CreateAsync(new CreateGameRequest("Other"));
        await _service.ActAsync(seat.Game.Id, seat.SeatToken, (s, p) => GameRules.Finish(s, p, _clock.GetUtcNow()));

        Assert.Equal(seat.Game.Id, _service.GetFeatured()?.Id);
        _notifier.Screens.Clear();

        _clock.Advance(TimeSpan.FromSeconds(31));
        await _service.TickAsync();

        Assert.Equal(lobby.Game.Id, _service.GetFeatured()?.Id);
        Assert.Equal(lobby.Game.Id, Assert.Single(_notifier.Screens)?.Id);
    }

    [Fact]
    public async Task Tick_CompletesCountdownAfter10Seconds()
    {
        var seat = await StartedGameAsync();
        var loser = _service.Get(seat.Game.Id).Players.First(p => p.PlayerId != seat.PlayerId);
        await _service.ActAsync(seat.Game.Id, seat.SeatToken,
            (s, p) => GameRules.AdjustLife(s, p, loser.PlayerId, -100, _clock.GetUtcNow()));
        Assert.Equal(GameStatus.InProgress, _service.Get(seat.Game.Id).Status);

        _clock.Advance(TimeSpan.FromSeconds(9));
        await _service.TickAsync();
        Assert.Equal(GameStatus.InProgress, _service.Get(seat.Game.Id).Status);

        _clock.Advance(TimeSpan.FromSeconds(2));
        await _service.TickAsync();
        var state = _service.Get(seat.Game.Id);
        Assert.Equal(GameStatus.Finished, state.Status);
        Assert.Equal(seat.PlayerId, state.WinnerPlayerId);
    }

    [Fact]
    public async Task Tick_RemovesFinishedGamesAfter10Minutes()
    {
        var seat = await StartedGameAsync();
        await _service.ActAsync(seat.Game.Id, seat.SeatToken, (s, p) => GameRules.Finish(s, p, _clock.GetUtcNow()));

        _clock.Advance(TimeSpan.FromMinutes(9));
        await _service.TickAsync();
        _ = _service.Get(seat.Game.Id);

        _clock.Advance(TimeSpan.FromMinutes(2));
        await _service.TickAsync();
        Assert.Throws<NotFoundException>(() => _service.Get(seat.Game.Id));
    }

    [Fact]
    public async Task Tick_RemovesCancelledGamesAfter10Minutes()
    {
        var seat = await _service.CreateAsync(new CreateGameRequest("Host"));
        await _service.ActAsync(seat.Game.Id, seat.SeatToken, (s, p) => GameRules.Cancel(s, p, _clock.GetUtcNow()));

        _clock.Advance(TimeSpan.FromMinutes(11));
        await _service.TickAsync();
        Assert.Throws<NotFoundException>(() => _service.Get(seat.Game.Id));
    }

    [Fact]
    public async Task Tick_RemovesIdleGamesAfter6Hours()
    {
        var seat = await StartedGameAsync();

        _clock.Advance(TimeSpan.FromHours(5));
        await _service.TickAsync();
        _ = _service.Get(seat.Game.Id);

        _clock.Advance(TimeSpan.FromHours(1.1));
        await _service.TickAsync();
        Assert.Throws<NotFoundException>(() => _service.Get(seat.Game.Id));
        Assert.Null(_service.GetFeatured());
    }

    [Fact]
    public async Task List_ExcludesFinishedAndCancelled()
    {
        var finished = await StartedGameAsync();
        await _service.ActAsync(finished.Game.Id, finished.SeatToken, (s, p) => GameRules.Finish(s, p, _clock.GetUtcNow()));
        var cancelled = await _service.CreateAsync(new CreateGameRequest("C"));
        await _service.ActAsync(cancelled.Game.Id, cancelled.SeatToken, (s, p) => GameRules.Cancel(s, p, _clock.GetUtcNow()));
        _clock.Advance(TimeSpan.FromSeconds(1));
        var lobby = await _service.CreateAsync(new CreateGameRequest("L"));

        Assert.Equal(lobby.Game.Id, Assert.Single(_service.List()).Id);
    }

    [Fact]
    public async Task Act_AfterLeave_OldTokenIsRejected()
    {
        var host = await _service.CreateAsync(new CreateGameRequest("Host"));
        var guest = await _service.JoinAsync(host.Game.Id, new JoinGameRequest("Guest"));
        await _service.ActAsync(host.Game.Id, guest.SeatToken, (s, p) => GameRules.Leave(s, p, _clock.GetUtcNow()));

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.ActAsync(host.Game.Id, guest.SeatToken, (_, _) => { }));
        Assert.Equal("invalid_seat", ex.Code);
    }

    [Fact]
    public async Task Join_Reclaim_FlagsSeatAsReclaimed()
    {
        var host = await _service.CreateAsync(new CreateGameRequest("Host"));
        var first = await _service.JoinAsync(host.Game.Id, new JoinGameRequest("Bob"));
        var again = await _service.JoinAsync(host.Game.Id, new JoinGameRequest("bob"));

        Assert.False(host.Reclaimed);
        Assert.False(first.Reclaimed);
        Assert.True(again.Reclaimed);
    }

    [Fact]
    public async Task Create_WithTooManyActiveGames_ThrowsTooManyGames()
    {
        for (var i = 0; i < 20; i++) await _service.CreateAsync(new CreateGameRequest($"H{i}"));

        var ex = await Assert.ThrowsAsync<DomainException>(() => _service.CreateAsync(new CreateGameRequest("Extra")));
        Assert.Equal("too_many_games", ex.Code);
    }

    [Fact]
    public async Task Create_FinishedAndCancelledGamesDoNotCountTowardCap()
    {
        var first = await _service.CreateAsync(new CreateGameRequest("H0"));
        await _service.ActAsync(first.Game.Id, first.SeatToken, (s, p) => GameRules.Cancel(s, p, _clock.GetUtcNow()));
        for (var i = 1; i < 20; i++) await _service.CreateAsync(new CreateGameRequest($"H{i}"));

        await _service.CreateAsync(new CreateGameRequest("Extra"));
    }

    private static readonly CommanderInfo Atraxa = new("Atraxa", "https://cards.scryfall.io/art_crop/front/a.jpg", "Marta Nael");
    private static readonly CommanderInfo Edgar = new("Edgar Markov", "https://cards.scryfall.io/art_crop/front/e.jpg", "Svetlin Velinov");

    [Fact]
    public async Task Join_ReclaimWithNewCommander_Broadcasts()
    {
        var host = await _service.CreateAsync(new CreateGameRequest("Host"));
        await _service.JoinAsync(host.Game.Id, new JoinGameRequest("Bob", Atraxa));
        var before = _notifier.Updated.Count;

        var again = await _service.JoinAsync(host.Game.Id, new JoinGameRequest("bob", Edgar));

        Assert.True(again.Reclaimed);
        Assert.Equal(before + 1, _notifier.Updated.Count);
        Assert.Equal(Edgar, _notifier.Updated[^1].FindPlayer(again.PlayerId)!.Commander);
        Assert.Equal(Edgar, _service.Get(host.Game.Id).FindPlayer(again.PlayerId)!.Commander);
    }

    [Fact]
    public async Task Join_ReclaimWithoutCommander_DoesNotBroadcast()
    {
        var host = await _service.CreateAsync(new CreateGameRequest("Host"));
        await _service.JoinAsync(host.Game.Id, new JoinGameRequest("Bob", Atraxa));
        var before = _notifier.Updated.Count;

        var again = await _service.JoinAsync(host.Game.Id, new JoinGameRequest("bob"));

        Assert.Equal(before, _notifier.Updated.Count);
        Assert.Equal(Atraxa, again.Game.FindPlayer(again.PlayerId)!.Commander);
    }

    [Fact]
    public async Task Create_WithBadCommander_ThrowsAndCreatesNothing()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.CreateAsync(new CreateGameRequest("Host", 40, new CommanderInfo("X", "https://evil.example/x.jpg", ""))));
        Assert.Equal("invalid_commander", ex.Code);
        Assert.Empty(_service.List());
    }

    [Fact]
    public async Task Create_WithCommander_SurvivesCloneToSeat()
    {
        var host = await _service.CreateAsync(new CreateGameRequest("Host", 40, Atraxa));
        Assert.Equal(Atraxa, host.Game.Players[0].Commander);
    }
}
