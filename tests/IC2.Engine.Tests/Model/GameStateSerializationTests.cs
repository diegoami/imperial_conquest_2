using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Model;

/// <summary>
/// DoD 4: "<c>GameState</c> is a fully serializable tree: a test constructs a non-trivial state,
/// serializes it, deserializes it, and asserts deep equality."
/// </summary>
public class GameStateSerializationTests
{
    [Fact]
    public void A_non_trivial_state_round_trips_with_deep_equality()
    {
        var state = ToyFixtures.NonTrivialState();

        var json = GameJson.Serialize(state);
        var reloaded = GameDataLoader.Load<GameState>("state.json", json);

        Assert.Equal(state, reloaded);
        Assert.Equal(json, GameJson.Serialize(reloaded));
    }

    [Fact]
    public void The_fixture_state_is_actually_non_trivial()
    {
        var state = ToyFixtures.NonTrivialState();

        Assert.Contains(state.Armies, a => a.IsEmbarked);
        Assert.Contains(state.Fleets, f => f.IsCarryingArmy);
        Assert.Contains(state.Fleets, f => f.IsUnderConstruction);
        Assert.Contains(state.Cities, c => c.UnderSiege);
        Assert.Contains(state.Cities, c => c.Garrison.Count > 0);
        Assert.NotEmpty(state.MercenaryPool);
        Assert.NotEmpty(state.NewsLog.Slots);
        Assert.True(state.Relations.Get("north", "south") < 0);
        Assert.Contains(state.Armies.SelectMany(a => a.Units), u => u.IsMercenary);
        Assert.Contains(state.Armies.SelectMany(a => a.Units), u => u.IsRegular);
    }

    [Fact]
    public void Deep_equality_is_by_value_not_by_reference()
    {
        var a = ToyFixtures.NonTrivialState();
        var b = ToyFixtures.NonTrivialState();

        Assert.False(ReferenceEquals(a, b));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void A_single_changed_leaf_anywhere_in_the_tree_breaks_equality()
    {
        var state = ToyFixtures.NonTrivialState();
        var armies = state.Armies.ToList();
        armies[0] = armies[0] with { Morale = armies[0].Morale + 1 };
        var changed = state with { Armies = ValueList.From(armies) };

        Assert.NotEqual(state, changed);
    }

    [Fact]
    public void A_changed_unit_slot_deep_inside_an_army_breaks_equality()
    {
        var state = ToyFixtures.NonTrivialState();
        var armies = state.Armies.ToList();
        var units = armies[0].Units.ToList();
        units[0] = units[0] with { Troops = units[0].Troops + 1 };
        armies[0] = armies[0] with { Units = ValueList.From(units) };
        var changed = state with { Armies = ValueList.From(armies) };

        Assert.NotEqual(state, changed);
    }

    [Fact]
    public void A_save_round_trips_and_keeps_the_state_it_wraps()
    {
        var save = ToyFixtures.NonTrivialSave();

        var reloaded = GameDataLoader.Load<SaveGame>("save.json", GameJson.Serialize(save));

        Assert.Equal(save, reloaded);
        Assert.Equal(save.State, reloaded.State);
    }
}
