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
/// <c>tests/IC2.Engine.Tests/Cities/Capture/SiegeAttritionTests.cs</c> for that formula proved directly,
/// including a scenario recomputed to the same Laranda/Gordium historical figures' own def/atk bounds
/// (<c>docs/game-design.md</c> §Combat: "Laranda 41/59 → 30/44 is exactly × 3/4… Gordium 54/23 → 42/18
/// needs def/atk ∈ [0.7826, 0.7963)"). <strong>The gap here is architectural, not evidentiary</strong>:
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

        // Siege 1: Laranda falls, and its cascade sweeps the seven weakly-defended cities.
        var afterLaranda = CityCaptureResolver.Capture(
            state, "seleucid-army", "laranda", ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        // Galatia now holds only Gordium.
        Assert.Equal(1, afterLaranda.CountCitiesOwnedBy(Galatia));
        Assert.False(afterLaranda.NationById(Galatia)!.Eliminated);

        // ---- DoD 2 / DoD 3, after Laranda's own capture plus the cascade's seven defections: treasury,
        // tax base, wealth, unity and city count all move by the confirmed terms. Laranda's contribution
        // is 10*59/80 = 7 (treasury/tax base += 7*4 = 28); every defector's own contribution truncates to
        // 0 (tribute 2 * population <= 9 / max 30), so the cascade adds nothing further to treasury or tax
        // base, only wealth (population*3000, summed: 6+7+6+8+9+9+7 = 52 -> 156,000) and unity (+3 per
        // defection for Seleucid, -20 floored at 250 per defection for Galatia). ----
        var seleucidAfterLaranda = afterLaranda.NationById(Seleucid)!;
        Assert.Equal(28, seleucidAfterLaranda.Treasury); // 7*4, no further treasury credit from the (all-zero-contribution) cascade.
        Assert.Equal(28, seleucidAfterLaranda.TaxBase);
        Assert.Equal(333_000, seleucidAfterLaranda.Wealth); // 59*3000 (Laranda) + 52*3000 (the seven defectors' populations).
        Assert.Equal(530, seleucidAfterLaranda.Unity); // 500 + 9 (Laranda) + 3*7 (seven defections).

        var galatiaAfterLaranda = afterLaranda.NationById(Galatia)!;
        Assert.Equal(-28, galatiaAfterLaranda.TaxBase);
        Assert.Equal(-333_000, galatiaAfterLaranda.Wealth);
        Assert.Equal(513, galatiaAfterLaranda.Unity); // 668 - 15 (Laranda) - 20*7 (seven defections), none hitting the 250 floor.

        // DoD 2: Laranda's loyalty moves to the forced-capture floor (owner now differs from allegiance).
        Assert.Equal(40, afterLaranda.CityById("laranda")!.Loyalty);

        // Siege 2: Gordium falls -- Galatia's last city, eliminating it.
        var final = CityCaptureResolver.Capture(
            afterLaranda, "seleucid-army", "gordium", ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        // ---- DoD 2 / DoD 3 / DoD 4, after Gordium's own capture (Galatia's last city, contribution
        // 8*23/40 = 4): the same confirmed terms, plus elimination. ----
        var seleucidFinal = final.NationById(Seleucid)!;
        Assert.Equal(44, seleucidFinal.Treasury); // 28 + 4*4.
        Assert.Equal(44, seleucidFinal.TaxBase);
        Assert.Equal(402_000, seleucidFinal.Wealth); // 333,000 + 23*3000.
        Assert.Equal(539, seleucidFinal.Unity); // 530 + 9.
        Assert.Equal(9, final.CountCitiesOwnedBy(Seleucid)); // Every one of Galatia's 9 cities.

        // DoD 2: Gordium's loyalty also moves to the forced-capture floor.
        Assert.Equal(40, final.CityById("gordium")!.Loyalty);

        // ---- Exactly 2 "falls to", exactly 7 "defects from" (DoD 1, DoD 6). ----
        var fallsTo = sink.Events.OfType<CityFallsToNation>().ToArray();
        var defectsFrom = sink.Events.OfType<CityDefectsToNation>().ToArray();
        Assert.Equal(2, fallsTo.Length);
        Assert.Equal(7, defectsFrom.Length);
        Assert.Equal(new[] { "Laranda", "Gordium" }, fallsTo.Select(e => e.CityName).ToArray());
        Assert.Equal(DefectorIds, defectsFrom.Select(e => e.CityName).ToArray());
        foreach (var e in fallsTo)
        {
            Assert.Equal(Galatia, e.OldOwner);
            Assert.Equal(Seleucid, e.NewOwner);
        }
        foreach (var e in defectsFrom)
        {
            Assert.Equal(Galatia, e.OldOwner);
            Assert.Equal(Seleucid, e.NewOwner);
        }

        // ---- The falls-to pair keeps whatever population/fortification it already carried into the
        // transfer: FUN_0044bb18's own confirmed pseudocode has no population or fortification term, so
        // Capture leaves both exactly as given -- the real, historical PRE-siege figures this scenario
        // constructed them with (Laranda 41/59, Gordium 54/23). The real POST-capture figures (Laranda
        // 30/44, Gordium 42/18, same report) are NOT asserted here, but T63 (bug #293) means this is no
        // longer an evidence gap: FUN_0044b230's erosion formula is now decompiled and confirmed (see the
        // class remarks), and reproducing those two figures is exactly what
        // SiegeAttritionTests.cs proves, through InstantBattleResolver.ResolveSiege -- the method that
        // owns erosion, not this one. This scenario calls Capture directly, deliberately bypassing
        // ResolveSiege, to isolate FUN_0044bb18's own transfer pseudocode from a real attempt's erosion;
        // that isolation is the reason these two fields are unchanged here, not a missing formula. ----
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

        // ---- DoD 4: Galatia is eliminated once its last city (Gordium) is gone. ----
        Assert.Equal(0, final.CountCitiesOwnedBy(Galatia));
        var galatiaAfter = final.NationById(Galatia)!;
        Assert.True(galatiaAfter.Eliminated);
        Assert.Null(galatiaAfter.CapitalCityId);
        Assert.Equal(0, galatiaAfter.Unity); // The confirmed Galatia figure: 668 -> 0.

        var conquered = Assert.Single(sink.Events.OfType<NationConquered>());
        Assert.Equal(Seleucid, conquered.ConqueringNation);
        Assert.Equal(Galatia, conquered.ConqueredNation);
    }
}
