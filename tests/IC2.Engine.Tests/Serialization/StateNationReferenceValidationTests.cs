using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Persistence;
using Xunit;

namespace IC2.Engine.Tests.SerializationTests;

/// <summary>
/// T71 (#411 S2): <c>ValidateState</c> holds T86's two nation references to the same rule as every other
/// one -- <see cref="GameState.Neighbours"/> (as a key or inside a list) and
/// <see cref="NationState.ConqueredBy"/> must name a nation that exists -- and keeps a null of either
/// valid. Each rejection test fails when its check is removed from <c>GameDataValidation.ValidateState</c>.
/// </summary>
public class StateNationReferenceValidationTests
{
    private const string DocumentPath = "state-under-test.json (in-memory)";

    private static GameState ValidState()
    {
        var state = PersistenceTestbed.PlayTurns(2);
        Assert.NotNull(state.Neighbours); // the premise the dangling-id tests mutate
        GameDataValidation.Validate(DocumentPath, state);
        return state;
    }

    [Fact]
    public void A_neighbour_entry_keyed_by_a_nation_that_does_not_exist_is_rejected_naming_it()
    {
        var state = ValidState();
        var entries = state.Neighbours!.ToList();
        entries[0] = entries[0] with { NationId = "atlantis" };

        var error = Assert.Throws<UnresolvedReferenceException>(
            () => GameDataValidation.Validate(DocumentPath, state with { Neighbours = ValueList.From(entries) }));

        Assert.Equal("nation", error.Kind);
        Assert.Equal("atlantis", error.Id);
        Assert.Contains("atlantis", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_neighbour_listed_inside_an_entry_that_does_not_exist_is_rejected_naming_it()
    {
        var state = ValidState();
        var entries = state.Neighbours!.ToList();
        entries[0] = entries[0] with { NeighbourIds = ValueList.From(entries[0].NeighbourIds.Append("lemuria")) };

        var error = Assert.Throws<UnresolvedReferenceException>(
            () => GameDataValidation.Validate(DocumentPath, state with { Neighbours = ValueList.From(entries) }));

        Assert.Equal("nation", error.Kind);
        Assert.Equal("lemuria", error.Id);
    }

    [Fact]
    public void A_conqueredBy_naming_a_nation_that_does_not_exist_is_rejected_naming_it()
    {
        var state = ValidState();
        var nations = state.Nations.Select((n, i) => i == 0 ? n with { ConqueredBy = "mu" } : n);

        var error = Assert.Throws<UnresolvedReferenceException>(
            () => GameDataValidation.Validate(DocumentPath, state with { Nations = ValueList.From(nations) }));

        Assert.Equal("nation", error.Kind);
        Assert.Equal("mu", error.Id);
    }

    [Fact]
    public void A_conqueredBy_naming_an_existing_nation_is_valid()
    {
        var state = ValidState();
        var conqueror = state.Nations[1].Id;
        var nations = state.Nations.Select((n, i) => i == 0 ? n with { ConqueredBy = conqueror } : n);

        GameDataValidation.Validate(DocumentPath, state with { Nations = ValueList.From(nations) });
    }

    [Fact]
    public void A_null_neighbour_set_is_still_valid()
    {
        GameDataValidation.Validate(DocumentPath, ValidState() with { Neighbours = null });
    }

    [Fact]
    public void A_null_conqueredBy_is_still_valid()
    {
        var state = ValidState();
        Assert.All(state.Nations, n => Assert.Null(n.ConqueredBy));

        GameDataValidation.Validate(DocumentPath, state);
    }
}
