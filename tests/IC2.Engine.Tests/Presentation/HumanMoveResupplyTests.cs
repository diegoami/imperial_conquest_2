using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// Bug #756, T144: <c>flags.humanMoveResupply</c>, read only by <see cref="GameSession"/>'s <c>move</c>.
/// <c>never</c> (<c>classical-faithful</c>) runs no automatic resupply on a human army's move;
/// <c>againstNonHostileCity</c> (<c>improved</c>, the toy) is T23's Done-when 2, unchanged.
/// </summary>
public sealed class HumanMoveResupplyTests
{
    // ---- Done-when 1 and 2 on the shipped presets: a real, nonzero move on classical-mediterranean ----

    private static GameSession PresetSession(string rulesetFile)
    {
        var classical = GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");
        var ruleset = rulesetFile == "classical-faithful"
            ? classical.Ruleset
            : GameDataLoader.LoadFile<Ruleset>(Path.Combine(ModelTestPaths.DataRoot, "rulesets", rulesetFile + ".json"));
        var session = new GameSession(
            classical.World, ruleset, classical.Scenario, seedOverride: 3, humanSeatNationId: "macedonia");
        session.Submit("split-army army-8 mac-marines 1"); // mac-marines (146,41): supply 0, purse 0, beside Thessalonica.
        session.Submit("end"); // a fresh turn: the split army starts with no moves left.
        return session;
    }

    private const string MarinesMove = "move mac-marines 145 41"; // one tile west; (145,41) adjoins Thessalonica (146,42).

    [Fact]
    public void Classical_faithful_a_real_move_beside_an_own_city_with_stock_changes_nothing_but_the_move()
    {
        var session = PresetSession("classical-faithful");
        Assert.Equal(HumanMoveResupplyPolicy.Never, session.Ruleset.Flags.HumanMoveResupply);
        var before = session.State;
        var army = before.ArmyById("mac-marines")!;
        Assert.True(army.Money < 500);
        Assert.True(army.SupplyTons < SupplyCapacity.ArmyCapacityTons(army.TotalTroops, session.Ruleset));
        Assert.True(before.CityById("thessalonica")!.SupplyTons > 0);

        var lines = session.Submit(MarinesMove).Lines;

        var after = session.State;
        var moved = after.ArmyById("mac-marines")!;
        Assert.True((145, 41) == (moved.X, moved.Y), string.Join(" / ", lines));
        Assert.Equal(army.SupplyTons, moved.SupplyTons);
        Assert.Equal(army.Money, moved.Money);
        Assert.Equal(before.Cities.Select(c => c.SupplyTons), after.Cities.Select(c => c.SupplyTons));
        Assert.Equal(before.Nations.Select(n => n.Treasury), after.Nations.Select(n => n.Treasury));
        Assert.Contains(lines, l => l.StartsWith("mac-marines moved from (146,41) to (145,41), spending ", StringComparison.Ordinal));
    }

    [Fact]
    public void Improved_the_same_real_move_resupplies_as_AutomaticResupply_computes()
    {
        var session = PresetSession("improved");
        Assert.Equal(HumanMoveResupplyPolicy.AgainstNonHostileCity, session.Ruleset.Flags.HumanMoveResupply);
        var before = session.State;
        var army = before.ArmyById("mac-marines")!;
        var city = before.CityById("thessalonica")!;
        var expected = AutomaticResupply.ForArmy(
            army with { X = 145, Y = 41 }, city, before.NationById(army.Nation)!, before.NationById(city.Owner)!, session.Ruleset);
        Assert.True(expected.AdmittedTons > 0);

        session.Submit(MarinesMove);

        var after = session.State;
        Assert.Equal((145, 41), (after.ArmyById("mac-marines")!.X, after.ArmyById("mac-marines")!.Y));
        Assert.Equal(expected.Army.SupplyTons, after.ArmyById("mac-marines")!.SupplyTons);
        Assert.Equal(expected.Army.Money, after.ArmyById("mac-marines")!.Money);
        Assert.Equal(expected.City.SupplyTons, after.CityById("thessalonica")!.SupplyTons);
        Assert.Equal(expected.ArmyNation.Treasury, after.NationById(army.Nation)!.Treasury);
        Assert.Equal(expected.CityNation.Treasury, after.NationById(city.Owner)!.Treasury);
    }
}
