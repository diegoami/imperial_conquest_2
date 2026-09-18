using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Battle;

/// <summary>
/// The <c>improved</c> ruleset's <c>combat.onDefeat: "scatter"</c> outcome — Done-when lines 10 and 11
/// of <c>docs/task-catalogue.md</c> T16, under <see cref="BattleTestbed.Seed"/>.
/// </summary>
/// <remarks>
/// This outcome is <c>[designed, no original analogue]</c> (<c>design-audit.md</c> Q1 follow-up and
/// Q10): its survivor-fraction and scatter-tile-range constants are ruleset data with a documented
/// placeholder provenance, deliberately <em>not</em> re-grounded on the rout mechanic's per-type
/// thresholds. Nothing in this file reaches for a confirmed tactical number.
/// </remarks>
public class ScatteredDefeatTests
{
    private const string Attacker = "north-attacker";
    private const string Defender = "south-defender";
    private const string Loser = "north-loser";
    private const string Winner = "south-winner";

    /// <summary>
    /// Done-when 10: under <c>improved</c>, a lost field battle applies the mirrored
    /// <c>loserPower × 40 / winnerPower</c>-shaped figure to the loser's own troops instead of destroying
    /// it, relocates the survivor 2–4 tiles from the battle site, and zeroes its moves.
    /// </summary>
    [Fact]
    public void DoD10_ALostFieldBattleLeavesAScatteredSurvivorWithNoMovesLeft()
    {
        var scattered = BattleTestbed.Scatter.Combat.ScatteredDefeat;
        var state = FieldBattleTests.Fixture();

        var (after, result) = InstantBattleResolver.ResolveField(
            state, Attacker, Defender, BattleTestbed.Scatter, BattleTestbed.World,
            BattleTestbed.BattleRng(), NullEventSink.Instance);

        Assert.Equal(DefeatOutcome.Scatter, result.AppliedDefeatOutcome);
        Assert.Equal(LoserFate.Scattered, result.LoserFate);

        // Mirrored, not the winner's own number: winnerPower × 40 / loserPower = 6120 × 40 / 3600 = 68,
        // against the winner's own 23.
        var mirrored = (result.WinnerPower * scattered.SurvivorCasualtyNumerator) / result.LoserPower;
        Assert.Equal(68, mirrored);
        Assert.Equal(mirrored, result.LoserCasualties);
        Assert.NotEqual(result.WinnerCasualties, result.LoserCasualties);

        var survivor = after.ArmyById(Defender)!;
        Assert.Equal(new[] { 8949, 2983 }, survivor.Units.Select(u => u.Troops).ToArray());
        Assert.Equal(12000 - mirrored, survivor.TotalTroops);

        // Relocated 2-4 tiles, and its moves are gone for the rest of the turn it lost on.
        var placement = result.Scatter!;
        Assert.InRange(placement.RequestedDistance, scattered.ScatterTilesMin, scattered.ScatterTilesMax);
        Assert.Equal(3, placement.RequestedDistance);
        Assert.Equal(3, placement.ActualDistance);
        Assert.Equal((2, 2), (placement.FromX, placement.FromY));
        Assert.Equal((5, 1), (placement.ToX, placement.ToY));
        Assert.Equal((5, 1), (survivor.X, survivor.Y));
        Assert.Equal(0, survivor.Moves);

        // Chebyshev distance from the battle site, and strictly further from the victor than it was.
        Assert.Equal(3, Math.Max(Math.Abs(5 - 2), Math.Abs(1 - 2)));
        var victor = after.ArmyById(Attacker)!;
        Assert.True(
            Math.Max(Math.Abs(survivor.X - victor.X), Math.Abs(survivor.Y - victor.Y)) > 1,
            "the survivor must end further from the victor than the tile it was beaten on");

        // The baggage stays behind either way: the winner still absorbs it, and the survivor keeps none.
        Assert.Equal(90, result.AbsorbedMoney);
        Assert.Equal(0, survivor.Money);
        Assert.Equal(0, survivor.SupplyTons);

        // And the state still reloads.
        var reloaded = GameDataLoader.Load<GameState>("scatter.json", GameJson.Serialize(after));
        GameDataValidation.Validate("scatter.json", reloaded);
    }

    /// <summary>
    /// Done-when 10 publishes <see cref="ArmyScattered"/> rather than the "destroys army of" news line,
    /// which would be false: the army is still on the map.
    /// </summary>
    [Fact]
    public void DoD10_AScatteredArmyDoesNotPublishTheDestroyedNewsLine()
    {
        var events = new RecordingEventSink();
        InstantBattleResolver.ResolveField(
            FieldBattleTests.Fixture(), Attacker, Defender, BattleTestbed.Scatter, BattleTestbed.World,
            BattleTestbed.BattleRng(), events);

        var scattered = Assert.Single(events.Events.OfType<ArmyScattered>());
        Assert.Equal(Defender, scattered.ArmyId);
        Assert.Equal("south", scattered.NationId);
        Assert.Equal((5, 1), (scattered.ToX, scattered.ToY));
        Assert.DoesNotContain(events.Events, e => e is BattleArmyDestroyed);
        Assert.False(scattered.IsNewsWorthy);
    }

    /// <summary>Done-when 10, at sea: a beaten fleet survives at reduced strength on a sea tile.</summary>
    [Fact]
    public void DoD10_ALostNavalBattleLeavesAScatteredFleetOnASeaTile()
    {
        var state = BattleTestbed.StateWith(
            fleets: new[]
            {
                BattleTestbed.Fleet("north-fleet", "north", 0, 2, 100, 100),
                BattleTestbed.Fleet("south-fleet", "south", 0, 3, 100, 100),
            });

        var (after, result) = InstantBattleResolver.ResolveNaval(
            state, "north-fleet", "south-fleet", BattleTestbed.Scatter, BattleTestbed.World,
            BattleTestbed.BattleRng(), "archers", NullEventSink.Instance);

        Assert.Equal(LoserFate.Scattered, result.LoserFate);

        var survivor = after.FleetById("south-fleet")!;
        Assert.Equal(0, survivor.Moves);

        var terrain = BattleTestbed.World.Terrain.Decode(BattleTestbed.World.Width, BattleTestbed.World.Height);
        var tile = BattleTestbed.World.TileTypeByCode(terrain[(survivor.Y * BattleTestbed.World.Width) + survivor.X])!;
        Assert.True(tile.PassableByFleets, $"a scattered fleet must land on sea, not on {tile.Name}");
        Assert.False(tile.PassableByArmies);
    }

    /// <summary>
    /// Done-when 11: when no valid tile exists even at distance 1, the outcome falls back to the
    /// <c>classical-faithful</c> destroyed result. Scripted, not incidental: the fixture is a two-tile
    /// island in an otherwise empty sea, with the victor standing on the only other land.
    /// </summary>
    [Fact]
    public void DoD11_AFullyBoxedInSurvivorFallsBackToTheDestroyedOutcome()
    {
        var island = BattleTestbed.IslandWorld(3, (0, 1), (1, 1));
        var state = BoxedInFixture();

        var (after, result) = InstantBattleResolver.ResolveField(
            state, Loser, Winner, BattleTestbed.Scatter, island,
            BattleTestbed.BattleRng(), NullEventSink.Instance);

        Assert.Equal(DefeatOutcome.Scatter, result.AppliedDefeatOutcome);
        Assert.Equal(LoserFate.Destroyed, result.LoserFate);
        Assert.Null(result.Scatter);
        Assert.Null(after.ArmyById(Loser));
        Assert.Single(after.Armies);

        // Reported as the whole force, exactly as the classical-faithful outcome reports it.
        Assert.Equal(6000, result.LoserCasualties);
    }

    /// <summary>
    /// The control for Done-when 11: the same two armies on the open map <em>do</em> scatter, so the
    /// fallback above is the boxed-in path and not an artefact of the fixture's casualty figure.
    /// </summary>
    [Fact]
    public void DoD11_TheSameArmiesScatterWhenThereIsSomewhereToGo()
    {
        var (after, result) = InstantBattleResolver.ResolveField(
            BoxedInFixture(), Loser, Winner, BattleTestbed.Scatter, BattleTestbed.World,
            BattleTestbed.BattleRng(), NullEventSink.Instance);

        Assert.Equal(LoserFate.Scattered, result.LoserFate);
        Assert.NotNull(result.Scatter);
        Assert.NotNull(after.ArmyById(Loser));

        // Survivors existed all along -- 226 of 6,000 lost -- so only the geography differed.
        Assert.Equal(226, result.LoserCasualties);
    }

    /// <summary>
    /// The other way a scatter degenerates: when the mirrored figure takes every last troop there is
    /// nobody to relocate, and the outcome is destruction rather than an empty army left on the map.
    /// </summary>
    [Fact]
    public void AnnihilationByTheMirroredFigureAlsoFallsBackToDestroyed()
    {
        var state = BattleTestbed.StateWith(
            armies: new[]
            {
                BattleTestbed.Army(Attacker, "north", 1, 2, 68, 0, 0, BattleTestbed.Unit("heavy_infantry", 9000, 6, "Overwhelming")),
                BattleTestbed.Army(Defender, "south", 2, 2, 30, 0, 0, BattleTestbed.Unit("light_infantry", 1000, 6, "Doomed")),
            });

        var (after, result) = InstantBattleResolver.ResolveField(
            state, Attacker, Defender, BattleTestbed.Scatter, BattleTestbed.World,
            BattleTestbed.BattleRng(), NullEventSink.Instance);

        Assert.Equal(LoserFate.Destroyed, result.LoserFate);
        Assert.Null(result.Scatter);
        Assert.Null(after.ArmyById(Defender));
        Assert.Equal(1000, result.LoserCasualties);
    }

    /// <summary>
    /// Two armies on a two-tile island: the loser on one land tile, the victor on the only other. Every
    /// ring around the loser is water, the victor, or off the map.
    /// </summary>
    private static GameState BoxedInFixture() =>
        BattleTestbed.StateWith(
            armies: new[]
            {
                BattleTestbed.Army(Loser, "north", 1, 1, 60, 0, 0, BattleTestbed.Unit("light_infantry", 6000, 6, "Trapped Foot")),
                BattleTestbed.Army(Winner, "south", 0, 1, 68, 0, 0, BattleTestbed.Unit("heavy_infantry", 6000, 6, "Blocking Guards")),
            },
            // No cities: the island world has no room for the toy map's, and a city off the map would be
            // a distraction rather than an obstacle.
            cities: Array.Empty<CityState>());
}
