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
        // against the winner's own 23 -- and it goes through the SAME per-unit expression as DoD 3.
        var mirroredRatio = (result.WinnerPower * scattered.SurvivorCasualtyNumerator) / result.LoserPower;
        Assert.Equal(68, mirroredRatio);
        Assert.NotEqual(23, mirroredRatio);

        // The loser's own divisors are the seed's eighth and ninth draws -- 6 and 5, so 111 and 110 --
        // because every draw the improved ruleset adds comes after every draw the original itself makes.
        var divisors = new[] { 111, 110 };
        var troops = new[] { 9000, 3000 };
        var expected = new[]
        {
            (troops[0] / divisors[0]) * mirroredRatio,
            (troops[1] / divisors[1]) * mirroredRatio,
        };
        Assert.Equal(new[] { 5508, 1836 }, expected);
        Assert.Equal(expected.Sum(), result.LoserCasualties);
        Assert.Equal(7344, result.LoserCasualties);

        var survivor = after.ArmyById(Defender)!;
        Assert.Equal(new[] { 3492, 1164 }, survivor.Units.Select(u => u.Troops).ToArray());
        Assert.Equal(12000 - result.LoserCasualties, survivor.TotalTroops);

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

        // A fleet has no unit slots, so the mirrored figure is read as hulls, scaled to the fleet's own
        // size rather than a flat count (T52 DoD 1/2): the mirrored ratio is 1300 × 40 / 1100 = 47, and
        // that ratio is multiplied into the fleet's own 100 hulls BEFORE dividing by the seed's third
        // draw (the same NextInt(15) call DoD07_AboveTheDamageThresholdTheWinnersCarriedArmyAlsoLosesWholeUnits
        // draws first, 11, divisor 116, since this fixture carries no army and so reaches the scatter
        // branch's own divisor draw at the same stream position): (100 × 47) / 116 = 40.
        Assert.Equal(40, result.LoserCasualties);

        var survivor = after.FleetById("south-fleet")!;
        Assert.Equal(100 - 40, survivor.Ships);
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
        Assert.Equal(24000, result.LoserCasualties);
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

        // Survivors existed all along -- the mirrored ratio is 5,100 x 40 / 4,080 = 50, and at divisors
        // 115 and 105 that costs 5,200 + 5,700 = 10,900 of 24,000 -- so only the geography differed.
        Assert.Equal(10900, result.LoserCasualties);
        Assert.Equal(new[] { 6800, 6300 }, after.ArmyById(Loser)!.Units.Select(u => u.Troops).ToArray());
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
    /// Cloud-review finding 1, end to end: a loser whose strength has truncated to zero is annihilated,
    /// not waved through untouched.
    /// </summary>
    /// <remarks>
    /// The mirrored ratio is <c>winnerPower × 40 / loserPower</c>, so a zero-strength loser puts a zero in
    /// the divisor. Until the cloud review, <see cref="BattleCasualties.Ratio"/>'s degenerate-case guard
    /// returned zero there and this army — 300 men against 6,000 — scattered away with every single
    /// soldier intact, the exact inverse of the intended rule.
    /// </remarks>
    [Fact]
    public void AZeroStrengthLoserIsAnnihilatedRatherThanEscapingUntouched()
    {
        var state = BattleTestbed.StateWith(
            armies: new[]
            {
                // 20 x 300 / 100 = 60, below the ruleset's PowerDivisor of 80, so this truncates to 0.
                BattleTestbed.Army(Loser, "north", 1, 2, 68, 0, 0, BattleTestbed.Unit("light_infantry", 300, 6, "Remnant")),
                BattleTestbed.Army(Winner, "south", 2, 2, 68, 0, 0, BattleTestbed.Unit("heavy_infantry", 6000, 6, "Overwhelming")),
            });

        var (after, result) = InstantBattleResolver.ResolveField(
            state, Loser, Winner, BattleTestbed.Scatter, BattleTestbed.World,
            BattleTestbed.BattleRng(), NullEventSink.Instance);

        Assert.Equal(0, result.AttackerPower);
        Assert.Equal(5100, result.DefenderPower);
        Assert.Equal(BattleSide.Defender, result.Winner);

        // Everything, and therefore nothing left to scatter -- the destroyed fallback.
        Assert.Equal(300, result.LoserCasualties);
        Assert.Equal(LoserFate.Destroyed, result.LoserFate);
        Assert.Null(result.Scatter);
        Assert.Null(after.ArmyById(Loser));

        // The forward call is untouched by the same guard: an opponent of no strength inflicts nothing,
        // so the winner still walks away clean.
        Assert.Equal(0, result.WinnerCasualties);
        Assert.Equal(6000, after.ArmyById(Winner)!.TotalTroops);
    }

    /// <summary>
    /// Cloud-review finding 3, land half: a scattered army's <c>CoveredTileCode</c> is recomputed at the
    /// destination, exactly as every other mover in the engine recomputes it.
    /// </summary>
    [Fact]
    public void AScatteredArmyRecomputesItsCoveredTileCodeAtTheDestination()
    {
        var state = FieldBattleTests.Fixture();
        var before = state.ArmyById(Defender)!;

        var (after, result) = InstantBattleResolver.ResolveField(
            state, Attacker, Defender, BattleTestbed.Scatter, BattleTestbed.World,
            BattleTestbed.BattleRng(), NullEventSink.Instance);

        var survivor = after.ArmyById(Defender)!;
        var terrain = BattleTestbed.World.Terrain.Decode(BattleTestbed.World.Width, BattleTestbed.World.Height);
        var expected = terrain[(survivor.Y * BattleTestbed.World.Width) + survivor.X];

        Assert.Equal((5, 1), (result.Scatter!.ToX, result.Scatter.ToY));
        Assert.Equal(expected, survivor.CoveredTileCode);

        // And it genuinely moved: the army left a tile of a different kind behind it.
        Assert.NotEqual(before.CoveredTileCode, survivor.CoveredTileCode);
        Assert.Equal("forest", BattleTestbed.World.TileTypeByCode(expected)!.Id);
    }

    /// <summary>
    /// Cloud-review finding 3, sea half — the one that is not cosmetic. <c>FleetTickSystem</c> decides
    /// each turn's storm-tripling branch by comparing a fleet's <c>CoveredTileCode</c> with
    /// <see cref="NavalRules.StormTripleConditionTileCode"/>, so a scattered fleet that kept its
    /// pre-battle code would carry the old tile's weather with it indefinitely.
    /// </summary>
    [Fact]
    public void AScatteredFleetRecomputesItsCoveredTileCodeAndSoItsStormBranch()
    {
        var naval = BattleTestbed.Scatter.Naval;

        // An all-deep-sea world with one irrelevant speck of land, so the destination's code is known
        // without depending on the toy map's coastline: every sea tile here is sea_deep, which is also
        // the ruleset's storm-tripling code.
        var deepSea = BattleTestbed.IslandWorld(8, (7, 7));

        var state = BattleTestbed.StateWith(
            fleets: new[]
            {
                BattleTestbed.Fleet("north-fleet", "north", 1, 2, 100, 100),
                BattleTestbed.Fleet("south-fleet", "south", 1, 3, 100, 100),
            },
            cities: Array.Empty<CityState>());

        var before = state.FleetById("south-fleet")!;
        Assert.NotEqual(naval.StormTripleConditionTileCode, before.CoveredTileCode);

        var (after, result) = InstantBattleResolver.ResolveNaval(
            state, "north-fleet", "south-fleet", BattleTestbed.Scatter, deepSea,
            BattleTestbed.BattleRng(), "archers", NullEventSink.Instance);

        Assert.Equal(LoserFate.Scattered, result.LoserFate);

        var survivor = after.FleetById("south-fleet")!;
        var terrain = deepSea.Terrain.Decode(deepSea.Width, deepSea.Height);
        var expected = terrain[(survivor.Y * deepSea.Width) + survivor.X];

        Assert.Equal(expected, survivor.CoveredTileCode);
        Assert.Equal("sea_deep", deepSea.TileTypeByCode(expected)!.Id);

        // The consequence the finding is about: the survivor is on deep sea now, so its storm-tripling
        // branch reflects where it actually is rather than where it used to be.
        Assert.Equal(naval.StormTripleConditionTileCode, survivor.CoveredTileCode);
        Assert.NotEqual(before.CoveredTileCode, survivor.CoveredTileCode);

        // Its carried-army rule is unchanged: an embarked army stays off the map, so it must have no
        // covered cell at all. (This fixture carries none; the invariant is asserted where one does.)
        Assert.DoesNotContain(after.Armies, a => a.AboardFleetId is not null && a.CoveredTileCode is not null);
    }

    /// <summary>
    /// Two armies on a two-tile island: the loser on one land tile, the victor on the only other. Every
    /// ring around the loser is water, the victor, or off the map.
    /// </summary>
    private static GameState BoxedInFixture() =>
        BattleTestbed.StateWith(
            armies: new[]
            {
                // Beaten, but not by enough for the mirrored ratio to reach the divisor: 4,080 against
                // 5,100 gives 50, so survivors exist and only the map can stop them relocating.
                BattleTestbed.Army(
                    Loser, "north", 1, 1, 68, 0, 0,
                    BattleTestbed.Unit("light_infantry", 12000, 6, "Trapped Foot"),
                    BattleTestbed.Unit("light_infantry", 12000, 6, "Trapped Foot II")),
                BattleTestbed.Army(Winner, "south", 0, 1, 68, 0, 0, BattleTestbed.Unit("heavy_infantry", 6000, 6, "Blocking Guards")),
            },
            // No cities: the island world has no room for the toy map's, and a city off the map would be
            // a distraction rather than an obstacle.
            cities: Array.Empty<CityState>());
}
