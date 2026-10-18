using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Battle;

/// <summary>Captured purse credits obey the field bound while battle reporting keeps the removed amount.</summary>
public sealed class CapturePurseBoundTests
{
    [Theory]
    [InlineData(32000, 700, 32700)]
    [InlineData(32000, 767, 32767)]
    [InlineData(32767, 500, 32767)]
    [InlineData(0, 0, 0)]
    [InlineData(32767, 32767, 32767)]
    public void Attacker_winner_credits_only_the_field_range_and_reports_the_full_capture(
        int winnerPurse, int loserPurse, int expectedPurse)
    {
        var state = FieldBattleTests.Fixture();
        state = WithPurses(state, winnerPurse, loserPurse);
        var events = new RecordingEventSink();
        var treasury = state.NationById("north")!.Treasury;

        var (after, result) = InstantBattleResolver.ResolveField(
            state, "north-attacker", "south-defender", BattleTestbed.Destroyed, BattleTestbed.World,
            BattleTestbed.BattleRng(), events);

        var winner = Assert.Single(after.Armies);
        Assert.Equal(expectedPurse, winner.Money);
        Assert.Equal(loserPurse, result.AbsorbedMoney);
        Assert.Equal(loserPurse, Assert.Single(events.Events.OfType<BattleResolved>()).Result.AbsorbedMoney);
        Assert.Equal(treasury, after.NationById("north")!.Treasury);
        Assert.Null(after.ArmyById("south-defender"));
    }

    [Theory]
    [InlineData(32000, 700, 32700)]
    [InlineData(32767, 500, 32767)]
    [InlineData(32767, 32767, 32767)]
    public void Defender_winner_uses_the_same_bounded_capture(int winnerPurse, int loserPurse, int expectedPurse)
    {
        var state = BattleTestbed.StateWith(armies: new[]
        {
            BattleTestbed.Army("north-attacker", "north", 1, 2, 60, loserPurse, 40,
                BattleTestbed.Unit("light_infantry", 9000, 6, "Weak Foot"),
                BattleTestbed.Unit("heavy_infantry", 3000, 6, "Weak Guards")),
            BattleTestbed.Army("south-defender", "south", 2, 2, 68, winnerPurse, 185,
                BattleTestbed.Unit("light_infantry", 12000, 5, "Strong Foot"),
                BattleTestbed.Unit("heavy_infantry", 4000, 6, "Strong Guards"),
                BattleTestbed.Unit("archers", 2000, 9, "Strong Bowmen")),
        });
        var events = new RecordingEventSink();
        var treasury = state.NationById("south")!.Treasury;

        var (after, result) = InstantBattleResolver.ResolveField(
            state, "north-attacker", "south-defender", BattleTestbed.Destroyed, BattleTestbed.World,
            BattleTestbed.BattleRng(), events);

        Assert.Equal(BattleSide.Defender, result.Winner);
        Assert.Equal(expectedPurse, after.ArmyById("south-defender")!.Money);
        Assert.Equal(loserPurse, result.AbsorbedMoney);
        Assert.Equal(loserPurse, Assert.Single(events.Events.OfType<BattleResolved>()).Result.AbsorbedMoney);
        Assert.Equal(treasury, after.NationById("south")!.Treasury);
        Assert.Null(after.ArmyById("north-attacker"));
    }

    [Fact]
    public void Scattered_loser_keeps_no_purse_and_bounded_state_validates_and_round_trips()
    {
        var state = WithPurses(FieldBattleTests.Fixture(), 32767, 500);
        var (after, result) = InstantBattleResolver.ResolveField(
            state, "north-attacker", "south-defender", BattleTestbed.Scatter, BattleTestbed.World,
            BattleTestbed.BattleRng(), NullEventSink.Instance);

        var survivor = Assert.IsType<ArmyState>(after.ArmyById("south-defender"));
        Assert.Equal(LoserFate.Scattered, result.LoserFate);
        Assert.Equal(0, survivor.Money);
        Assert.Equal(0, survivor.SupplyTons);
        Assert.Equal(32767, after.ArmyById("north-attacker")!.Money);
        Assert.Equal(500, result.AbsorbedMoney);

        GameDataValidation.Validate("capture-purse.json", after);
        var save = new SaveGame(GameDataSchema.CurrentVersion, "capture-purse", "capture purse",
            after.ScenarioId, after.WorldId, after.RulesetId, after);
        var serialized = SaveManager.Serialize(save);
        var reloaded = SaveManager.Load("capture-purse.json", serialized, BattleTestbed.World, BattleTestbed.Destroyed);
        GameDataValidation.Validate("capture-purse.json", reloaded.State);
        Assert.Equal(serialized, SaveManager.Serialize(reloaded));
        Assert.InRange(reloaded.State.ArmyById("north-attacker")!.Money, 0, short.MaxValue);
    }

    [Fact]
    public void Emptied_winner_is_still_removed_after_capturing_the_losers_purse()
    {
        var winner = BattleTestbed.Army("south-defender", "south", 5, 5, 60, 32767, 0,
            BattleTestbed.Unit("archers", 340, 6, "Below deletion threshold"));
        var loser = BattleTestbed.Army("north-attacker", "north", 4, 5, 1, 500, 0,
            BattleTestbed.Unit("light_infantry", 1, 1, "Negligible"));
        var bystander = BattleTestbed.Army("bystander", "north", 20, 20, 60, 0, 0,
            BattleTestbed.Unit("light_infantry", 12000, 6, "Unrelated survivor"));
        var state = BattleTestbed.StateWith(armies: new[] { winner, loser, bystander });

        var (after, result) = InstantBattleResolver.ResolveField(
            state, "north-attacker", "south-defender", BattleTestbed.Destroyed, BattleTestbed.World,
            BattleTestbed.BattleRng(), NullEventSink.Instance);

        Assert.Equal(BattleSide.Defender, result.Winner);
        Assert.Equal(500, result.AbsorbedMoney);
        Assert.Null(after.ArmyById("south-defender"));
        Assert.Null(after.ArmyById("north-attacker"));
        Assert.Equal("bystander", Assert.Single(after.Armies).Id);
    }

    [Fact]
    public void Capture_preserves_seeded_draws_and_results()
    {
        var state = WithPurses(FieldBattleTests.Fixture(), 32767, 32767);
        var firstRng = new SplitMix64Rng(BattleTestbed.Seed);
        var secondRng = new SplitMix64Rng(BattleTestbed.Seed);
        var firstEvents = new RecordingEventSink();
        var secondEvents = new RecordingEventSink();

        var first = InstantBattleResolver.ResolveField(state, "north-attacker", "south-defender",
            BattleTestbed.Scatter, BattleTestbed.World, firstRng, firstEvents);
        var second = InstantBattleResolver.ResolveField(state, "north-attacker", "south-defender",
            BattleTestbed.Scatter, BattleTestbed.World, secondRng, secondEvents);

        Assert.Equal(first.Result, second.Result);
        Assert.Equal(GameJson.Serialize(first.State), GameJson.Serialize(second.State));
        Assert.Equal(firstRng.State, secondRng.State);
        Assert.Equal(firstEvents.Events, secondEvents.Events);
        Assert.Equal(32767, first.State.ArmyById("north-attacker")!.Money);
        Assert.Equal(32767, first.Result.AbsorbedMoney);
    }

    private static GameState WithPurses(GameState state, int winnerPurse, int loserPurse) => state with
    {
        Armies = ValueList.From(state.Armies.Select(army => army.Id switch
        {
            "north-attacker" => army with { Money = winnerPurse },
            "south-defender" => army with { Money = loserPurse },
            _ => army,
        })),
    };
}
