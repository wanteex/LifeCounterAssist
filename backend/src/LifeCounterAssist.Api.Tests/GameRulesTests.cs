using LifeCounterAssist.Api.Domain;

namespace LifeCounterAssist.Api.Tests;

public class GameRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 20, 0, 0, TimeSpan.Zero);

    private static readonly (string Id, string Name)[] Seats =
        [("p1", "Alice"), ("p2", "Bob"), ("p3", "Chloé"), ("p4", "Dan"), ("p5", "Eve"), ("p6", "Fay"), ("p7", "Gus"), ("p8", "Hal")];

    private static GameState NewGame(int players = 2)
    {
        var s = GameRules.Create("g", Seats[0].Id, Seats[0].Name, 40, Now);
        for (var i = 1; i < players; i++) GameRules.Join(s, Seats[i].Id, Seats[i].Name, Now);
        return s;
    }

    private static GameState Started(int players = 2)
    {
        var s = NewGame(players);
        GameRules.Start(s, "p1", Now);
        return s;
    }

    private static void Kill(GameState s, string id, DateTimeOffset now) => GameRules.AdjustLife(s, "p1", id, -40, now);

    [Theory]
    [InlineData(19)]
    [InlineData(41)]
    [InlineData(0)]
    public void Create_RejectsStartingLifeOutsideAllowedValues(int life)
    {
        var ex = Assert.Throws<DomainException>(() => GameRules.Create("g", "p1", "Alice", life, Now));
        Assert.Equal("invalid_starting_life", ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012345678901")]
    public void Create_RejectsInvalidName(string name)
    {
        var ex = Assert.Throws<DomainException>(() => GameRules.Create("g", "p1", name, 40, Now));
        Assert.Equal("invalid_name", ex.Code);
    }

    [Fact]
    public void Create_TrimsNameAndSeatsHostWithStartingLife()
    {
        var s = GameRules.Create("g", "p1", "  Alice ", 40, Now);

        var host = Assert.Single(s.Players);
        Assert.Equal("Alice", host.DisplayName);
        Assert.Equal(40, host.Life);
        Assert.Equal("p1", s.HostPlayerId);
    }

    [Fact]
    public void Join_SameNameDifferentCase_ReclaimsExistingSeat()
    {
        var s = NewGame();

        var (player, reclaimed) = GameRules.Join(s, "pX", "alice", Now);

        Assert.Equal("p1", player.PlayerId);
        Assert.True(reclaimed);
        Assert.Equal(2, s.Players.Count);
    }

    [Fact]
    public void Join_ReclaimWorksWhileInProgress()
    {
        var s = Started();

        var (player, reclaimed) = GameRules.Join(s, "pX", "BOB", Now);

        Assert.Equal("p2", player.PlayerId);
        Assert.True(reclaimed);
    }

    [Fact]
    public void Join_NewNameWhileInProgress_Throws()
    {
        var s = Started();
        var ex = Assert.Throws<DomainException>(() => GameRules.Join(s, "p9", "Zed", Now));
        Assert.Equal("game_already_started", ex.Code);
    }

    [Fact]
    public void Join_NinthPlayer_Throws()
    {
        var s = NewGame(8);
        var ex = Assert.Throws<DomainException>(() => GameRules.Join(s, "p9", "Zed", Now));
        Assert.Equal("game_full", ex.Code);
    }

    [Fact]
    public void Join_FinishedGame_Throws()
    {
        var s = Started();
        GameRules.Finish(s, "p1", Now);
        var ex = Assert.Throws<DomainException>(() => GameRules.Join(s, "pX", "Alice", Now));
        Assert.Equal("game_over", ex.Code);
    }

    [Fact]
    public void Join_CancelledGame_Throws()
    {
        var s = NewGame();
        GameRules.Cancel(s, "p1", Now);
        var ex = Assert.Throws<DomainException>(() => GameRules.Join(s, "pX", "Alice", Now));
        Assert.Equal("game_over", ex.Code);
    }

    [Fact]
    public void Start_RequiresHostAndTwoPlayers()
    {
        var s = NewGame();
        Assert.Throws<ForbiddenException>(() => GameRules.Start(s, "p2", Now));

        var solo = NewGame(1);
        var ex = Assert.Throws<DomainException>(() => GameRules.Start(solo, "p1", Now));
        Assert.Equal("not_enough_players", ex.Code);
    }

    [Fact]
    public void Cancel_ByHostInLobby_SetsCancelled()
    {
        var s = NewGame();
        var later = Now.AddMinutes(1);

        GameRules.Cancel(s, "p1", later);

        Assert.Equal(GameStatus.Cancelled, s.Status);
        Assert.Equal(later, s.FinishedAt);
    }

    [Fact]
    public void Leave_HostCannotLeave()
    {
        var s = NewGame();
        var ex = Assert.Throws<DomainException>(() => GameRules.Leave(s, "p1", Now));
        Assert.Equal("host_cannot_leave", ex.Code);
    }

    [Fact]
    public void CommanderDamage_AlsoReducesLife()
    {
        var s = Started();

        GameRules.AdjustCommanderDamage(s, "p1", "p1", "p2", 5, Now);

        var p1 = s.FindPlayer("p1")!;
        Assert.Equal(5, p1.CommanderDamage["p2"]);
        Assert.Equal(35, p1.Life);
    }

    [Fact]
    public void Poison_NeverBelowZero()
    {
        var s = Started();
        GameRules.AdjustPoison(s, "p1", "p2", -3, Now);
        Assert.Equal(0, s.FindPlayer("p2")!.Poison);
    }

    [Fact]
    public void LastPlayerStanding_StartsTenSecondCountdown()
    {
        var s = Started();

        Kill(s, "p2", Now);

        Assert.Equal("p1", s.PendingWinnerPlayerId);
        Assert.Equal(Now.AddSeconds(10), s.PendingFinishAt);
        Assert.Equal(GameStatus.InProgress, s.Status);
    }

    [Fact]
    public void Healing_DuringCountdown_CancelsIt()
    {
        var s = Started();
        Kill(s, "p2", Now);

        GameRules.AdjustLife(s, "p1", "p2", 1, Now);

        Assert.Null(s.PendingWinnerPlayerId);
        Assert.Null(s.PendingFinishAt);
    }

    [Fact]
    public void CompletePendingFinish_BeforeDeadline_DoesNothing()
    {
        var s = Started();
        Kill(s, "p2", Now);

        Assert.False(GameRules.CompletePendingFinish(s, Now.AddSeconds(9)));
        Assert.Equal(GameStatus.InProgress, s.Status);
    }

    [Fact]
    public void CompletePendingFinish_AtDeadline_FinishesWithWinner()
    {
        var s = Started();
        Kill(s, "p2", Now);

        Assert.True(GameRules.CompletePendingFinish(s, Now.AddSeconds(10)));
        Assert.Equal(GameStatus.Finished, s.Status);
        Assert.Equal("p1", s.WinnerPlayerId);
        Assert.Null(s.PendingFinishAt);
    }

    [Fact]
    public void CancelPendingFinish_ThenUnrelatedChange_DoesNotRestart()
    {
        var s = Started(3);
        Kill(s, "p2", Now);
        Kill(s, "p3", Now);
        Assert.Equal("p1", s.PendingWinnerPlayerId);

        GameRules.CancelPendingFinish(s, "p1", Now);
        GameRules.AdjustLife(s, "p1", "p1", -1, Now);

        Assert.Null(s.PendingWinnerPlayerId);
        Assert.Null(s.PendingFinishAt);
    }

    [Fact]
    public void CancelPendingFinish_ThenSurvivorChanges_Restarts()
    {
        var s = Started(3);
        Kill(s, "p2", Now);
        Kill(s, "p3", Now);
        GameRules.CancelPendingFinish(s, "p1", Now);

        GameRules.AdjustLife(s, "p1", "p2", 40, Now);
        Assert.Null(s.PendingWinnerPlayerId);

        var later = Now.AddSeconds(3);
        Kill(s, "p2", later);

        Assert.Equal("p1", s.PendingWinnerPlayerId);
        Assert.Equal(later.AddSeconds(10), s.PendingFinishAt);
    }

    [Fact]
    public void AllPlayersDead_NoCountdown()
    {
        var s = Started();
        Kill(s, "p1", Now);
        Kill(s, "p2", Now);

        Assert.Null(s.PendingWinnerPlayerId);
        Assert.Null(s.PendingFinishAt);
    }

    [Fact]
    public void ManualFinish_HasNoWinner()
    {
        var s = Started();
        Kill(s, "p2", Now);

        GameRules.Finish(s, "p1", Now);

        Assert.Equal(GameStatus.Finished, s.Status);
        Assert.Null(s.WinnerPlayerId);
        Assert.Null(s.PendingWinnerPlayerId);
    }

    [Fact]
    public void AdjustLife_AfterFinish_Throws()
    {
        var s = Started();
        GameRules.Finish(s, "p1", Now);
        var ex = Assert.Throws<DomainException>(() => GameRules.AdjustLife(s, "p1", "p2", -1, Now));
        Assert.Equal("game_not_in_progress", ex.Code);
    }

    [Fact]
    public void EveryMutation_UpdatesLastActivityAt()
    {
        var s = NewGame();
        var t = Now;
        void Step(Action<DateTimeOffset> act)
        {
            t = t.AddSeconds(1);
            var v = s.Version;
            act(t);
            Assert.Equal(t, s.LastActivityAt);
            Assert.True(s.Version > v);
        }

        Step(n => GameRules.Join(s, "p3", "Chloé", n));
        Step(n => GameRules.Leave(s, "p3", n));
        Step(n => GameRules.Start(s, "p1", n));
        Step(n => GameRules.AdjustLife(s, "p1", "p1", -1, n));
        Step(n => GameRules.AdjustCommanderDamage(s, "p1", "p1", "p2", 1, n));
        Step(n => GameRules.AdjustPoison(s, "p1", "p1", 1, n));
        Step(n => GameRules.ToggleConcede(s, "p1", "p2", n));
        Step(n => GameRules.Finish(s, "p1", n));

        var c = NewGame();
        GameRules.Cancel(c, "p1", Now.AddSeconds(5));
        Assert.Equal(Now.AddSeconds(5), c.LastActivityAt);
    }

    private const string Art = "https://cards.scryfall.io/art_crop/front/4/a/x.jpg";
    private static readonly CommanderInfo Atraxa = new("Atraxa", Art, "Marta Nael");
    private static readonly CommanderInfo Edgar = new("Edgar Markov", Art, "Svetlin Velinov");

    [Fact]
    public void Create_WithCommander_StoresIt()
    {
        var s = GameRules.Create("g", "p1", "Alice", 40, Now, Atraxa);
        Assert.Equal(Atraxa, s.Players[0].Commander);
    }

    [Fact]
    public void Join_WithCommander_StoresIt()
    {
        var s = NewGame(1);
        var (player, _) = GameRules.Join(s, "p2", "Bob", Now, Edgar);
        Assert.Equal(Edgar, player.Commander);
    }

    [Fact]
    public void Join_Reclaim_WithoutCommander_KeepsExisting()
    {
        var s = NewGame(1);
        GameRules.Join(s, "p2", "Bob", Now, Atraxa);
        var (player, reclaimed) = GameRules.Join(s, "p9", "bob", Now);
        Assert.True(reclaimed);
        Assert.Equal(Atraxa, player.Commander);
    }

    [Fact]
    public void Join_Reclaim_WithCommander_ReplacesIt()
    {
        var s = NewGame(1);
        GameRules.Join(s, "p2", "Bob", Now, Atraxa);
        var (player, _) = GameRules.Join(s, "p9", "bob", Now, Edgar);
        Assert.Equal(Edgar, player.Commander);
    }

    [Theory]
    [InlineData("https://evil.example/art_crop/x.jpg")]
    [InlineData("http://cards.scryfall.io/art_crop/x.jpg")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://cards.scryfall.io/large/front/x.jpg")]
    public void NormalizeCommander_RejectsBadUrl(string url)
    {
        var ex = Assert.Throws<DomainException>(() => GameRules.NormalizeCommander(new CommanderInfo("Atraxa", url, "A")));
        Assert.Equal("invalid_commander", ex.Code);
    }

    [Fact]
    public void NormalizeCommander_RejectsTooLongUrl()
    {
        var url = "https://cards.scryfall.io/art_crop/" + new string('a', 300);
        Assert.Throws<DomainException>(() => GameRules.NormalizeCommander(new CommanderInfo("Atraxa", url, "A")));
    }

    [Theory]
    [InlineData("  ")]
    [InlineData(null)]
    public void NormalizeCommander_RejectsEmptyName(string? name)
    {
        var ex = Assert.Throws<DomainException>(() => GameRules.NormalizeCommander(new CommanderInfo(name!, Art, "A")));
        Assert.Equal("invalid_commander", ex.Code);
    }

    [Fact]
    public void NormalizeCommander_RejectsTooLongNameOrArtist()
    {
        Assert.Throws<DomainException>(() => GameRules.NormalizeCommander(new CommanderInfo(new string('a', 151), Art, "A")));
        Assert.Throws<DomainException>(() => GameRules.NormalizeCommander(new CommanderInfo("Atraxa", Art, new string('a', 101))));
    }

    [Fact]
    public void NormalizeCommander_TrimsNameAndArtist_AndAcceptsNull()
    {
        Assert.Equal(new CommanderInfo("Atraxa", Art, "Marta"), GameRules.NormalizeCommander(new CommanderInfo(" Atraxa ", Art, " Marta ")));
        Assert.Equal(new CommanderInfo("Atraxa", Art, ""), GameRules.NormalizeCommander(new CommanderInfo("Atraxa", Art, null!)));
        Assert.Null(GameRules.NormalizeCommander(null));
    }

    [Fact]
    public void Join_WithBadCommander_DoesNotSeatPlayer()
    {
        var s = NewGame(1);
        Assert.Throws<DomainException>(() => GameRules.Join(s, "p2", "Bob", Now, new CommanderInfo("X", "https://evil/x", "")));
        Assert.Single(s.Players);
    }

    [Fact]
    public void Acknowledge_RecordsTheActionSequenceOnTheActor()
    {
        var s = Started();
        GameRules.Acknowledge(s, "p2", 1_700_000_000_123);
        Assert.Equal(1_700_000_000_123, s.FindPlayer("p2")!.AckSeq);
        Assert.Equal(0, s.FindPlayer("p1")!.AckSeq);
    }

    [Fact]
    public void Acknowledge_NeverMovesBackwards()
    {
        var s = Started();
        GameRules.Acknowledge(s, "p1", 50);
        GameRules.Acknowledge(s, "p1", 20);
        Assert.Equal(50, s.FindPlayer("p1")!.AckSeq);
    }

    [Fact]
    public void Acknowledge_UnknownPlayer_DoesNothing()
    {
        var s = Started();
        GameRules.Acknowledge(s, "nobody", 5);
        Assert.All(s.Players, p => Assert.Equal(0, p.AckSeq));
    }
}
