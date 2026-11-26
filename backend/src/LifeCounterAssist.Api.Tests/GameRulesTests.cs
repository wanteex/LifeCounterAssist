using LifeCounterAssist.Api.Domain;

namespace LifeCounterAssist.Api.Tests;

public class GameRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_SeatsHostWithStartingLifeAndExcludesHostFromInvites()
    {
        var state = GameRules.Create("g", "ABCDE", Player("host"), 4, 40, ["host", "bob", "bob"], Now);

        Assert.Equal(40, state.Players.Single().Life);
        Assert.Equal(["bob"], state.InvitedUserIds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    public void Create_RejectsInvalidPlayerCount(int maxPlayers)
    {
        var ex = Assert.Throws<DomainException>(() => GameRules.Create("g", "C", Player("host"), maxPlayers, 40, [], Now));
        Assert.Equal("invalid_player_count", ex.Code);
    }

    [Fact]
    public void Join_RejectsWhenFull()
    {
        var state = GameRules.Create("g", "C", Player("host"), 2, 40, [], Now);
        GameRules.Join(state, Player("p2"), Now);

        var ex = Assert.Throws<DomainException>(() => GameRules.Join(state, Player("p3"), Now));
        Assert.Equal("game_full", ex.Code);
    }

    [Fact]
    public void Join_RemovesInvitation()
    {
        var state = GameRules.Create("g", "C", Player("host"), 4, 40, ["p2"], Now);

        GameRules.Join(state, Player("p2"), Now);

        Assert.Empty(state.InvitedUserIds);
        Assert.Equal(40, state.FindPlayer("p2")!.Life);
    }

    [Fact]
    public void Start_IsHostOnly()
    {
        var state = TwoPlayerLobby();

        Assert.Throws<ForbiddenException>(() => GameRules.Start(state, "p2", Now));
    }

    [Fact]
    public void AdjustLife_RequiresGameInProgress()
    {
        var state = TwoPlayerLobby();

        var ex = Assert.Throws<DomainException>(() => GameRules.AdjustLife(state, "host", "p2", -1, Now));
        Assert.Equal("game_not_in_progress", ex.Code);
    }

    [Fact]
    public void CommanderDamage_AlsoReducesLifeAndEliminatesAt21()
    {
        var state = StartedGame();

        GameRules.AdjustCommanderDamage(state, "host", "p2", "host", 20, Now);
        var target = state.FindPlayer("p2")!;
        Assert.Equal(20, target.Life);
        Assert.False(target.IsEliminated);

        GameRules.AdjustCommanderDamage(state, "host", "p2", "host", 1, Now);
        Assert.Equal(19, target.Life);
        Assert.True(target.IsEliminated);
        Assert.Equal("host", GameRules.SuggestWinner(state));
    }

    [Fact]
    public void CommanderDamage_UndoCannotGoBelowZeroAndRestoresOnlyAppliedLife()
    {
        var state = StartedGame();
        GameRules.AdjustCommanderDamage(state, "host", "p2", "host", 3, Now);

        GameRules.AdjustCommanderDamage(state, "host", "p2", "host", -5, Now);

        var target = state.FindPlayer("p2")!;
        Assert.Equal(0, target.CommanderDamage["host"]);
        Assert.Equal(40, target.Life);
    }

    [Fact]
    public void CommanderDamage_RejectsSelfSource()
    {
        var state = StartedGame();

        Assert.Throws<DomainException>(() => GameRules.AdjustCommanderDamage(state, "host", "p2", "p2", 1, Now));
    }

    [Fact]
    public void AdjustCounter_ClampsAtZeroAndPoisonEliminatesAt10()
    {
        var state = StartedGame();
        var target = state.FindPlayer("p2")!;

        GameRules.AdjustCounter(state, "host", "p2", CounterType.Energy, -3, Now);
        Assert.Equal(0, target.Energy);

        GameRules.AdjustCounter(state, "host", "p2", CounterType.Poison, 10, Now);
        Assert.True(target.IsEliminated);
    }

    [Fact]
    public void Finish_RecordsWinnerAndBlocksFurtherChanges()
    {
        var state = StartedGame();

        GameRules.Finish(state, "p2", "host", Now);

        Assert.Equal(GameStatus.Finished, state.Status);
        Assert.Equal("host", state.WinnerUserId);
        Assert.Throws<DomainException>(() => GameRules.AdjustLife(state, "host", "p2", -1, Now));
    }

    [Fact]
    public void Log_IsCapped()
    {
        var state = StartedGame();

        for (var i = 0; i < GameRules.MaxLogEntries + 20; i++)
            GameRules.AdjustLife(state, "host", "p2", -1, Now);

        Assert.Equal(GameRules.MaxLogEntries, state.Log.Count);
    }

    private static PlayerState Player(string id) => new() { UserId = id, DisplayName = id };

    private static GameState TwoPlayerLobby()
    {
        var state = GameRules.Create("g", "C", Player("host"), 4, 40, [], Now);
        GameRules.Join(state, Player("p2"), Now);
        return state;
    }

    private static GameState StartedGame()
    {
        var state = TwoPlayerLobby();
        GameRules.Start(state, "host", Now);
        return state;
    }
}
