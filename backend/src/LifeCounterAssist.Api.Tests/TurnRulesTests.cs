using LifeCounterAssist.Api.Domain;

namespace LifeCounterAssist.Api.Tests;

public class TurnRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 20, 0, 0, TimeSpan.Zero);

    private static readonly (string Id, string Name)[] Seats = [("p1", "Alice"), ("p2", "Bob"), ("p3", "Chloé"), ("p4", "Dan")];

    private static GameState NewGame(int players = 3, GameMode mode = GameMode.Normal, BlitzSettings? blitz = null)
    {
        var s = GameRules.Create("g", Seats[0].Id, Seats[0].Name, 40, Now, null, mode, blitz);
        for (var i = 1; i < players; i++) GameRules.Join(s, Seats[i].Id, Seats[i].Name, Now);
        return s;
    }

    private static GameState Started(int players = 3, GameMode mode = GameMode.Normal, BlitzSettings? blitz = null)
    {
        var s = NewGame(players, mode, blitz);
        GameRules.Start(s, "p1", Now);
        return s;
    }

    private static GameState Blitz(int baseSeconds = 30, bool penalty = false, int players = 3) =>
        Started(players, GameMode.Blitz, new BlitzSettings(baseSeconds, penalty));

    private static DateTimeOffset At(double seconds) => Now.AddSeconds(seconds);

    // Task 1

    [Fact]
    public void Create_DefaultsToNormalWithoutBlitz()
    {
        var s = GameRules.Create("g", "p1", "Alice", 40, Now);
        Assert.Equal(GameMode.Normal, s.Mode);
        Assert.Null(s.Blitz);
        Assert.Null(s.Turn);
    }

    [Fact]
    public void Create_Blitz_DefaultSettingsWhenNull()
    {
        var s = GameRules.Create("g", "p1", "Alice", 40, Now, null, GameMode.Blitz);
        Assert.Equal(new BlitzSettings(30, false), s.Blitz);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(90)]
    public void Create_Blitz_RejectsBaseSeconds(int seconds)
    {
        var ex = Assert.Throws<DomainException>(() =>
            GameRules.Create("g", "p1", "Alice", 40, Now, null, GameMode.Blitz, new BlitzSettings(seconds, false)));
        Assert.Equal("invalid_blitz", ex.Code);
    }

    [Fact]
    public void Create_Normal_IgnoresBlitzSettings()
    {
        var s = GameRules.Create("g", "p1", "Alice", 40, Now, null, GameMode.Normal, new BlitzSettings(15, true));
        Assert.Null(s.Blitz);
    }

    [Theory]
    [InlineData(30, 1, 30)]
    [InlineData(30, 2, 45)]
    [InlineData(30, 3, 68)]
    [InlineData(15, 2, 23)]
    [InlineData(60, 5, 300)]
    [InlineData(30, 9, 300)]
    public void TurnLimitSeconds_GrowsAndCaps(int baseSeconds, int round, int expected) =>
        Assert.Equal(expected, GameRules.TurnLimitSeconds(baseSeconds, round));

    [Fact]
    public void Start_SetsTurnOnFirstSeat_ClockRunning()
    {
        var t = Started().Turn!;
        Assert.Equal("p1", t.ActivePlayerId);
        Assert.Equal(1, t.Round);
        Assert.Equal(0, t.ElapsedMs);
        Assert.Equal(Now, t.RunningSince);
        Assert.Null(t.LimitSeconds);
    }

    [Fact]
    public void Start_Blitz_SetsLimitOfRoundOne() => Assert.Equal(30, Blitz().Turn!.LimitSeconds);
}
