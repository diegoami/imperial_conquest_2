using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T35 Model: nation tax base, recruitment slots, and the pending
/// diplomatic offer", the hostile-army-adjacent predicate <see cref="Economy.HostileArmyAdjacent"/>
/// (<c>FUN_004497cc</c>), consumed by DoD 9's "a threatened city does not grow" bullet and reused by
/// T37.
/// </summary>
public sealed class HostileArmyAdjacentTests
{
    private static GameState BaseState() => EconomyTestbed.InitialState();

    private static (GameState State, CityState City) WithCityAt(int x, int y, string owner)
    {
        var state = BaseState();
        var city = state.Cities[0] with { X = x, Y = y, Owner = owner, Allegiance = owner };
        var cities = state.Cities.Select((c, i) => i == 0 ? city : c);
        return (state with { Cities = ValueList.From(cities) }, city);
    }

    private static GameState WithArmyAt(GameState state, string armyId, int x, int y, string nation)
    {
        var armies = state.Armies.Select(a => a.Id == armyId ? a with { X = x, Y = y, Nation = nation, AboardFleetId = null, CoveredTileCode = 2 } : a);
        return state with { Armies = ValueList.From(armies) };
    }

    private static GameState AtWar(GameState state, string a, string b) =>
        state with { Relations = state.Relations.WithRelation(a, b, EconomyTestbed.Ruleset.Diplomacy.StateCodes.War) };

    [Fact]
    public void A_hostile_army_inside_the_threeByThree_block_threatens_the_city()
    {
        var (state, city) = WithCityAt(x: 5, y: 5, owner: "north");
        state = WithArmyAt(state, "south-army-1", x: 6, y: 6, nation: "south"); // diagonal neighbour cell.
        state = AtWar(state, "north", "south");

        Assert.True(HostileArmyAdjacent.IsThreatened(city, state, EconomyTestbed.Ruleset));
    }

    [Fact]
    public void The_same_army_one_cell_further_away_does_not_threaten_the_city()
    {
        var (state, city) = WithCityAt(x: 5, y: 5, owner: "north");
        state = WithArmyAt(state, "south-army-1", x: 7, y: 7, nation: "south"); // two cells away diagonally.
        state = AtWar(state, "north", "south");

        Assert.False(HostileArmyAdjacent.IsThreatened(city, state, EconomyTestbed.Ruleset));
    }

    [Fact]
    public void An_adjacent_army_from_a_nation_not_at_war_does_not_threaten_the_city()
    {
        var (state, city) = WithCityAt(x: 5, y: 5, owner: "north");
        state = WithArmyAt(state, "south-army-1", x: 5, y: 6, nation: "south"); // adjacent, but at peace.

        Assert.False(HostileArmyAdjacent.IsThreatened(city, state, EconomyTestbed.Ruleset));
    }

    [Fact]
    public void An_army_embarked_on_a_fleet_covers_no_cell_and_cannot_threaten_a_city()
    {
        var (state, city) = WithCityAt(x: 5, y: 5, owner: "north");
        var armies = state.Armies.Select(a => a.Id == "south-army-1"
            ? a with { X = 5, Y = 6, Nation = "south", AboardFleetId = "north-fleet-1", CoveredTileCode = null }
            : a);
        state = state with { Armies = ValueList.From(armies) };
        state = AtWar(state, "north", "south");

        Assert.False(HostileArmyAdjacent.IsThreatened(city, state, EconomyTestbed.Ruleset));
    }
}
