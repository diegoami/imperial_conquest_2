using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// Bug #756, T144: <c>flags.humanMoveResupply</c>, read only by <see cref="GameSession"/>'s <c>move</c>.
/// <c>never</c> (<c>classical-faithful</c>) runs no automatic resupply on a human army's move;
/// <c>againstNonHostileCity</c> (<c>improved</c>, the toy) is T23's Done-when 2, unchanged.
/// </summary>
public sealed class HumanMoveResupplyTests
{
    private const string ArmyId = "north-army-1";

    private static GameSession DrainedSession(HumanMoveResupplyPolicy policy)
    {
        var toy = CoreTestbed.Toy;
        var ruleset = toy.Ruleset with { Flags = toy.Ruleset.Flags with { HumanMoveResupply = policy } };
        var scenario = toy.Scenario with
        {
            Seats = ValueList.From(
                toy.Scenario.Seats.Select(seat => seat with { Control = SeatControl.Human, Personality = null })),
        };
        var session = new GameSession(toy.World, ruleset, scenario);
        session.Submit($"move {ArmyId} 4 3"); // adjoins portus (own) and meridia (foreign).
        for (var i = 0; i < 12; i++)
        {
            session.Submit("end");
        }

        Assert.Equal(0, session.State.ArmyById(ArmyId)!.SupplyTons);
        return session;
    }

    [Fact]
    public void Under_never_a_move_beside_an_own_city_with_stock_changes_nothing_but_the_move()
    {
        var session = DrainedSession(HumanMoveResupplyPolicy.Never);
        var before = session.State;
        var army = before.ArmyById(ArmyId)!;
        Assert.True(army.Money < 500);
        Assert.Contains(before.Cities, c => c.Owner == army.Nation && c.SupplyTons > 0);

        var lines = session.Submit($"move {ArmyId} 4 3").Lines;

        var after = session.State;
        var moved = after.ArmyById(ArmyId)!;
        Assert.Equal(army.SupplyTons, moved.SupplyTons);
        Assert.Equal(army.Money, moved.Money);
        Assert.Equal(before.Cities.Select(c => c.SupplyTons), after.Cities.Select(c => c.SupplyTons));
        Assert.Equal(before.Nations.Select(n => n.Treasury), after.Nations.Select(n => n.Treasury));
        Assert.Contains(lines, l => l.StartsWith($"{ArmyId} moved from (4,3) to (4,3), spending ", StringComparison.Ordinal));
    }

    [Fact]
    public void Under_againstNonHostileCity_the_same_move_resupplies_as_AutomaticResupply_computes()
    {
        var session = DrainedSession(HumanMoveResupplyPolicy.AgainstNonHostileCity);
        var before = session.State;
        var army = before.ArmyById(ArmyId)!;
        var city = before.CityById("portus")!;
        var expected = AutomaticResupply.ForArmy(
            army, city, before.NationById(army.Nation)!, before.NationById(city.Owner)!, session.Ruleset);
        Assert.True(expected.AdmittedTons > 0);

        session.Submit($"move {ArmyId} 4 3");

        var after = session.State;
        Assert.Equal(expected.Army.SupplyTons, after.ArmyById(ArmyId)!.SupplyTons);
        Assert.Equal(expected.Army.Money, after.ArmyById(ArmyId)!.Money);
        Assert.Equal(expected.City.SupplyTons, after.CityById("portus")!.SupplyTons);
        Assert.Equal(expected.ArmyNation.Treasury, after.NationById(army.Nation)!.Treasury);
    }
}
