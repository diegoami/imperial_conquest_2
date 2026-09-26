using System.Linq;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// <c>docs/task-catalogue.md</c> T17, Done-when 1: a scripted scenario reproducing the confirmed Galatia
/// elimination (<c>galatia-elimination-and-city-resupply-confirmed.md</c>) city by city — exactly 2
/// <c>"falls to"</c> and exactly 7 <c>"defects from"</c>, with the defections leaving population and
/// fortification provably unchanged. Doubles as DoD 4's elimination reproduction (Galatia's own observed
/// <c>668 → 0</c> unity and <c>0xFFFF</c> capital sentinel) and DoD 6's message coverage.
/// </summary>
/// <remarks>
/// <para>
/// <strong>How the two mechanisms are reproduced, and what is fixture data versus what this task's code
/// actually decides.</strong> Laranda and Gordium are captured directly through
/// <see cref="CityCaptureResolver.Capture"/> (the corpus's own confirmed "falls to" pair,
/// <c>elimination.galatiaFallsToCount</c>), each fed as a separate siege the same historical elimination
/// would have needed (the events are far apart in game time, not one call). Both start this scenario at
/// their real, historical <em>pre-siege</em> fortification and population — Laranda 41/59, Gordium 54/23
/// (<c>galatia-elimination-and-city-resupply-confirmed.md</c> line 12) — real figures throughout, never a
/// placeholder, so there is no question of which numbers are historical and which are scenario placement.
/// </para>
/// <para>
/// <strong>DoD 1's amended text (<c>docs/task-catalogue.md</c>, #198): the capture half asserts the
/// transfer, not the erosion — and T63 changes WHY, not just what.</strong> An earlier revision of this
/// remark said the erosion formula (<c>FUN_0044b230</c>) was undecompiled and reproducing the real
/// post-capture figures (Laranda 30/44, Gordium 42/18) "would mean inventing the rule, which this project
/// does not do". That premise is gone: <c>FUN_0044b230</c> is now decompiled and confirmed at instruction
/// level (<c>decompiled-defection-and-siege-attrition.md</c> §"FUN_0044b27c, instruction by instruction",
/// research 3f6ca09; bug #293), field = max(field × 3/4, min(field × 19/20 + 1, field × def/atk)) — see
/// <c>SiegeAttritionTests.GalatiaHistoricalCaptures_ReproduceThePostSiegeFiguresThroughResolveSiege</c>
/// for that formula proved directly against the same Laranda/Gordium historical figures, driven through
/// <see cref="InstantBattleResolver.ResolveSiege"/> at each city's own confirmed def/atk bound
/// (<c>docs/game-design.md</c> §Combat: "Laranda 41/59 → 30/44 is exactly × 3/4… Gordium 54/23 → 42/18
/// needs def/atk ∈ [0.7826, 0.7963)"), asserting the exact post-capture 30/44 and 42/18.
/// <strong>The gap here is architectural, not evidentiary</strong>:
/// erosion lives in <see cref="InstantBattleResolver.ResolveSiege"/> (<c>src/IC2.Engine/Battle/**</c>,
/// this task's Owns list, not T17's), and this scenario deliberately calls
/// <see cref="CityCaptureResolver.Capture"/> directly rather than through that resolver, to isolate
/// <c>FUN_0044bb18</c>'s own confirmed pseudocode — no population or fortification term at all — from the
/// erosion a real siege attempt would already have applied to the state <see cref="Capture"/> receives.
/// So <see cref="Capture"/> genuinely leaves Laranda and Gordium's fortification/population exactly as
/// constructed (41/59, 54/23) — that assertion is correct and unaffected by T63 — but it is no longer
/// read as "the real post-capture figures are unreproducible"; they are reproducible, by
/// <see cref="InstantBattleResolver.ResolveSiege"/>, just not by this method.
/// </para>
/// <para>
/// The other seven — named for Galatia's own <c>elimination.galatiaDefectsFromCount</c> note (Synnada,
/// Pessinus, Acroinon, Ancyra, Gangra, Nyssa, Halys) — are placed and given loyalty/defence figures that
/// satisfy <c>FUN_0044ba1c</c>'s confirmed cascade gate (distance, unity, defender strength, loyalty) so
/// that this task's own <see cref="CityCaptureResolver"/> code is what decides they defect, not a scripted
/// outcome; their exact pre-elimination fortification/population are not individually in the corpus, so
/// the assertion this scenario proves for them is the confirmed structural one —
/// <c>defection.neverChangesPopOrFort</c> — not a specific historical number.
/// </para>
/// </remarks>
public sealed class GalatiaEliminationScenarioTests
{
    private const string Galatia = "galatia";
    private const string Seleucid = "seleucid";

    private static readonly string[] DefectorIds =
        { "synnada", "pessinus", "acroinon", "ancyra", "gangra", "nyssa", "halys" };

    [Fact]
    public void ReproducesTheGalatiaEliminationPattern_TwoFallsTo_SevenDefectsFrom()
    {
        var ruleset = CaptureTestbed.Ruleset;

        // Laranda: the corpus's own real, historical PRE-siege fortification and population (41/59) --
        // the input Capture receives, which it must leave untouched (see the class remarks).
        var laranda = CaptureTestbed.City(
            "laranda", "Laranda", 0, 0, Galatia, Galatia,
            loyalty: 30, fortificationCode: 41, populationThousands: 59, maxPopulationThousands: 80, tribute: 10);

        // Gordium: the second forced capture, far enough from the attacker's siege-1 position that it is
        // never itself a candidate in Laranda's own cascade. Also its real, historical pre-siege
        // fortification and population (54/23).
        var gordium = CaptureTestbed.City(
            "gordium", "Gordium", 50, 50, Galatia, Galatia,
            loyalty: 35, fortificationCode: 54, populationThousands: 23, maxPopulationThousands: 40, tribute: 8);

        // Seven cities, close to the attacker's siege-1 position, low loyalty, weakly defended -- exactly
        // FUN_0044ba1c's gate -- so CityCaptureResolver's own cascade decides they defect.
        var defectorPositions = new (int X, int Y)[] { (1, 1), (2, 2), (3, 1), (1, 3), (4, 4), (2, 4), (4, 2) };
        var defectors = DefectorIds.Zip(defectorPositions, (id, pos) => CaptureTestbed.City(
            id, id, pos.X, pos.Y, Galatia, Galatia,
            loyalty: 10, fortificationCode: 40 + pos.X, populationThousands: 5 + pos.Y, maxPopulationThousands: 30, tribute: 2)).ToArray();

        var galatia = CaptureTestbed.Nation(Galatia, unity: 668, capitalCityId: "ancyra");
        var seleucid = CaptureTestbed.Nation(Seleucid, unity: 500);

        var attacker = CaptureTestbed.Army(
            "seleucid-army", Seleucid, 0, 0, morale: 80, CaptureTestbed.Unit("heavy_infantry", 50_000));

        var allCities = new[] { laranda, gordium }.Concat(defectors).ToArray();
        var state = CaptureTestbed.StateWith(
            nations: new[] { galatia, seleucid }, cities: allCities, armies: new[] { attacker });

        Assert.Equal(9, state.CountCitiesOwnedBy(Galatia));

        var sink = new RecordingEventSink();

        // Siege 1: Laranda falls (not Galatia's capital -- that is "ancyra", not part of this scenario),
        // and its cascade sweeps the seven weakly-defended cities. That leaves Galatia owning only
        // Gordium -- 1 city, under CaptureRules.ConquestCityCountThreshold (6) -- so T86's own conquest
        // trigger fires in this SAME call, sweeping Gordium away too, with no second siege needed. This
        // is exactly Galatia's own historical shape (galatia-elimination-and-city-resupply-confirmed.md):
        // a conquest below 6 cities silently takes the remaining city with no "falls to" news line of its
        // own, only the cascade's "defects from" lines and the final "X conquers Y." banner.
        var final = CityCaptureResolver.Capture(
            state, "seleucid-army", "laranda", ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        // ---- DoD 2 / DoD 3, after Laranda's own capture plus the cascade's seven defections (before the
        // conquest trigger's own effects below): treasury, tax base, wealth and unity all move by the
        // confirmed single-city terms. Laranda's contribution is 10*59/80 = 7 (treasury/tax base += 7*4 =
        // 28); every defector's own contribution truncates to 0 (tribute 2 * population <= 9 / max 30),
        // so the cascade adds nothing further to treasury or tax base, only wealth (population*3000,
        // summed: 6+7+6+8+9+9+7 = 52 -> 156,000) and unity (+3 per defection for Seleucid, -20 floored at
        // 250 per defection for Galatia). These are intermediate values, not final ones -- the conquest
        // trigger's own effects (below) add Gordium's own contribution on top, through its own distinct
        // multipliers.
        //
        // T86: Laranda's own loyalty is now a formula, not the flat 40 floor -- allegiance ("galatia")
        // differs from the new owner ("seleucid"), so max(ForcedCaptureFloor, min(ForcedCaptureCap,
        // NonAllegiantTransferBase - L)) = max(40, min(60, 100 - 30)) = max(40, 60) = 60.
        Assert.Equal(60, final.CityById("laranda")!.Loyalty);

        // ---- T86: Gordium is conquered, not captured a second time -- its own conquest loyalty formula
        // (allegiance "galatia" differs from the new owner too): min(70, max(40, 100 - 35)) = min(70, 65)
        // = 65, plus one independent Random(6) draw the production code derives from the same
        // GameState.RandomSeed and the city's own id (ConquestCascade's own remarks) -- reproduced here
        // directly rather than hard-coding whatever it happens to draw, so this assertion tracks the
        // production formula instead of one arbitrary seed's output.
        var expectedGordiumBonus = SplitMix64Rng.ForStream(state.RandomSeed, "capture.conquestLoyalty")
            .ForStream("gordium")
            .NextInt(ruleset.Capture.ConquestLoyaltyRandomBonusMax);
        Assert.Equal(65 + expectedGordiumBonus, final.CityById("gordium")!.Loyalty);

        // ---- Final Seleucid totals: the single-city terms above, plus Gordium's own conquest-cascade
        // contribution (contribution 8*23/40 = 4) through the conquest's own distinct multipliers --
        // ConquestTreasuryCreditMultiplier (6, not the single-capture CaptureTreasuryCreditMultiplier, 4)
        // for treasury, the same generic economy multipliers for tax base and wealth, and
        // ConquestWinnerUnityGain (50, not the single-capture CaptureUnityGain, 9) for unity. ----
        var seleucidFinal = final.NationById(Seleucid)!;
        Assert.Equal(52, seleucidFinal.Treasury); // 28 + 4*6.
        Assert.Equal(44, seleucidFinal.TaxBase); // 28 + 4*4 -- the same generic multiplier either way.
        Assert.Equal(402_000, seleucidFinal.Wealth); // 333,000 + 23*3000 -- the same generic multiplier either way.
        Assert.Equal(580, seleucidFinal.Unity); // 530 + 50 (conquest), not +9 (a second forced capture).
        Assert.Equal(9, final.CountCitiesOwnedBy(Seleucid)); // Every one of Galatia's 9 cities.

        // ---- Exactly 1 "falls to" (Laranda only -- Gordium's own conquest sweep writes no such event,
        // matching the historical Galatia record's own silent transfers), exactly 7 "defects from"
        // (DoD 1, DoD 6). ----
        var fallsTo = sink.Events.OfType<CityFallsToNation>().ToArray();
        var defectsFrom = sink.Events.OfType<CityDefectsToNation>().ToArray();
        Assert.Single(fallsTo);
        Assert.Equal(7, defectsFrom.Length);
        Assert.Equal("Laranda", Assert.Single(fallsTo).CityName);
        Assert.Equal(DefectorIds, defectsFrom.Select(e => e.CityName).ToArray());
        Assert.Equal(Galatia, fallsTo[0].OldOwner);
        Assert.Equal(Seleucid, fallsTo[0].NewOwner);
        foreach (var e in defectsFrom)
        {
            Assert.Equal(Galatia, e.OldOwner);
            Assert.Equal(Seleucid, e.NewOwner);
        }

        // ---- Laranda keeps whatever population/fortification it already carried into the transfer:
        // FUN_0044bb18's own confirmed pseudocode has no population or fortification term, so Capture
        // leaves both exactly as given -- the real, historical PRE-siege figures this scenario
        // constructed it with (41/59). The real POST-capture figures (30/44, same report) are NOT
        // asserted here, but T63 (bug #293) means this is no longer an evidence gap: FUN_0044b230's
        // erosion formula is now decompiled and confirmed (see the class remarks), and reproducing that
        // figure is exactly what
        // SiegeAttritionTests.GalatiaHistoricalCaptures_ReproduceThePostSiegeFiguresThroughResolveSiege
        // proves, through InstantBattleResolver.ResolveSiege -- the method that owns erosion, not this
        // one. This scenario calls Capture directly, deliberately bypassing ResolveSiege, to isolate
        // FUN_0044bb18's own transfer pseudocode from a real attempt's erosion; that isolation is the
        // reason this field is unchanged here, not a missing formula. Gordium, being conquered rather
        // than captured, keeps its own population/fortification unchanged too -- ConquestCascade's own
        // effect list has no such term either, exactly like the single-city formulas. ----
        Assert.Equal(41, final.CityById("laranda")!.FortificationCode);
        Assert.Equal(59, final.CityById("laranda")!.PopulationThousands);
        Assert.Equal(54, final.CityById("gordium")!.FortificationCode);
        Assert.Equal(23, final.CityById("gordium")!.PopulationThousands);

        // ---- The seven defectors: population and fortification exactly unchanged (defection.neverChangesPopOrFort). ----
        foreach (var before in defectors)
        {
            var after = final.CityById(before.Id)!;
            Assert.Equal(before.FortificationCode, after.FortificationCode);
            Assert.Equal(before.PopulationThousands, after.PopulationThousands);
            Assert.Equal(Seleucid, after.Owner);
        }

        // ---- DoD 4 / T86: Galatia is conquered once it drops below the conquest threshold. Its capital
        // IS cleared (unlike a defection's own capital-preserving elimination -- see
        // NationElimination's own remarks): ConquestCascade's own effect list writes the sentinel
        // explicitly. ----
        Assert.Equal(0, final.CountCitiesOwnedBy(Galatia));
        var galatiaAfter = final.NationById(Galatia)!;
        Assert.True(galatiaAfter.Eliminated);
        Assert.Null(galatiaAfter.CapitalCityId);
        Assert.Equal(0, galatiaAfter.Unity); // The confirmed Galatia figure: 668 -> 0.
        Assert.Equal(Seleucid, galatiaAfter.ConqueredBy);

        var conquered = Assert.Single(sink.Events.OfType<NationConquered>());
        Assert.Equal(Seleucid, conquered.ConqueringNation);
        Assert.Equal(Galatia, conquered.ConqueredNation);
    }
}
