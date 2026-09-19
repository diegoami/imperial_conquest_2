using System.Text.Json;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// <c>docs/task-catalogue.md</c> T19 DoD 10: "shown again when a save is loaded" — proved here as a
/// round-trip: a state carrying a pending offer serializes and deserializes back to an equal
/// <see cref="PendingDiplomaticOffer"/>, so a reloaded save still has it to show.
/// </summary>
public sealed class PendingOfferSaveRoundTripTests
{
    [Fact]
    public void DoD10_APendingOffer_SurvivesSerializeThenDeserialize()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation("proposer", "Proposer", control: SeatControl.Ai),
            DiplomacyTestbed.Nation("human", "Human", control: SeatControl.Human));
        state = state with
        {
            PendingOffer = new PendingDiplomaticOffer("proposer", ruleset.Diplomacy.StateCodes.Trade),
        };

        var json = GameJson.Serialize(state);
        var reloaded = JsonSerializer.Deserialize<GameState>(json, GameJson.Options);

        Assert.NotNull(reloaded);
        Assert.Equal(state.PendingOffer, reloaded!.PendingOffer);
    }

    /// <summary>A state with no pending offer round-trips to <see langword="null"/>, not an empty object.</summary>
    [Fact]
    public void DoD10_NoPendingOffer_RoundTripsToNull()
    {
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation("a", "A"), DiplomacyTestbed.Nation("b", "B"));

        var json = GameJson.Serialize(state);
        var reloaded = JsonSerializer.Deserialize<GameState>(json, GameJson.Options);

        Assert.Null(reloaded!.PendingOffer);
    }
}
