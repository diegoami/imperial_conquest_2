using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Battle;
using Xunit;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

/// <summary>Negative coverage of every field compared after the battle, independent of replay's early block failures.</summary>
public sealed class PostBattleTests
{
    private static GameState Original()
    {
        var armies = new[]
        {
            BattleTestbed.Army("army-0", "north", 1, 1, 60, 10, 20, BattleTestbed.Unit("heavy_infantry", 100, 6, "A")),
            BattleTestbed.Army("army-1", "south", 2, 1, 60, 10, 20, BattleTestbed.Unit("heavy_infantry", 100, 6, "B")),
        };
        return BattleTestbed.StateWith(armies) with { NewsLog = new NewsLog(0, ValueList<NewsEntry>.Of(new NewsEntry("original news"))) };
    }

    [Fact]
    public void Equal_strategic_state_passes()
    {
        var state = Original();
        Assert.Null(PostBattle.FirstDifference(state, state));
        Assert.Null(PostBattle.FirstDifference(Conclusion(state), state));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Each_armys_units_money_supplies_and_morale_are_compared(int side)
    {
        var original = Original();
        var army = original.Armies[side];
        GameState Port(ArmyState changed) => original with { Armies = ValueList.From(original.Armies.Select(a => a.Id == army.Id ? changed : a)) };
        var unit = army.Units[0] with { Troops = 99 };
        Assert.Equal($"{army.Id} unit 0: port {unit}, original {army.Units[0]}",
            PostBattle.FirstDifference(Port(army with { Units = ValueList<UnitSlot>.Of(unit) }), original));
        Assert.Equal($"{army.Id}: unit count port 0, original 1",
            PostBattle.FirstDifference(Port(army with { Units = ValueList<UnitSlot>.Empty }), original));
        Assert.Equal($"{army.Id} money: port 11, original 10", PostBattle.FirstDifference(Port(army with { Money = 11 }), original));
        Assert.Equal($"{army.Id} supplies: port 21, original 20", PostBattle.FirstDifference(Port(army with { SupplyTons = 21 }), original));
        Assert.Equal($"{army.Id} morale: port 61, original 60", PostBattle.FirstDifference(Port(army with { Morale = 61 }), original));
    }

    [Theory]
    [InlineData("north")]
    [InlineData("south")]
    public void Each_nations_unity_is_compared(string nationId)
    {
        var original = Original();
        var nation = original.NationById(nationId)!;
        var port = original with { Nations = ValueList.From(original.Nations.Select(n => n.Id == nationId ? n with { Unity = nation.Unity + 1 } : n)) };
        Assert.Equal($"{nationId} unity: port {nation.Unity + 1}, original {nation.Unity}", PostBattle.FirstDifference(port, original));
    }

    [Fact]
    public void Army_and_nation_membership_and_news_are_compared()
    {
        var original = Original();
        Assert.Equal("armies: port [army-0], original [army-0,army-1]", PostBattle.FirstDifference(
            original with { Armies = ValueList<ArmyState>.Of(original.Armies[0]) }, original));
        var nations = original.Nations.Skip(1).ToArray();
        Assert.Equal($"nations: port [{string.Join(",", nations.Select(n => n.Id))}], original [{string.Join(",", original.Nations.Select(n => n.Id))}]",
            PostBattle.FirstDifference(original with { Nations = ValueList.From(nations) }, original));
        Assert.Equal("news: most recent slot port -1, original 0", PostBattle.FirstDifference(original with { NewsLog = NewsLog.Empty }, original));
        Assert.Equal("news slot 0: port 'changed news', original 'original news'", PostBattle.FirstDifference(
            original with { NewsLog = new NewsLog(0, ValueList<NewsEntry>.Of(new NewsEntry("changed news"))) }, original));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Shorter_and_longer_news_logs_report_a_discrepancy(int count)
    {
        var original = Original();
        var port = original with { NewsLog = new NewsLog(0, ValueList.From(Enumerable.Repeat(new NewsEntry("original news"), count))) };
        Assert.Equal($"news: slot count port {count}, original 1", PostBattle.FirstDifference(port, original));
    }

    [Fact]
    public void Refused_null_and_missing_conclusions_cannot_pass()
    {
        var state = Original();
        var success = Conclusion(state);
        Assert.True(new ReplayOutcome { Conclusion = success }.Passed);
        Assert.False(new ReplayOutcome().Passed);
        var missing = success with { Result = success.Result with { State = null } };
        Assert.Equal("the battle write-back produced no strategic state", PostBattle.FirstDifference(missing, state));
        Assert.False(new ReplayOutcome { Conclusion = missing }.Passed);
        var rejected = success with { Result = success.Result with { Rejection = "NotAParty" } };
        Assert.Equal("the battle write-back was refused: NotAParty", PostBattle.FirstDifference(rejected, state));
        Assert.False(new ReplayOutcome { Conclusion = rejected }.Passed);
    }

    private static GoldenReplay.Conclusion Conclusion(GameState state) =>
        new(new TacticalBattleConclusion(state, null, false, false, Array.Empty<DomainEvent>(), null), state);
}
