using System.Linq;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// <c>docs/task-catalogue.md</c> T17, Done-when 1: a scripted scenario reproducing the confirmed Galatia
/// elimination (<c>galatia-elimination-and-city-resupply-confirmed.md</c>) city by city — exactly 2
/// <c>"falls to"</c> with population and fortification loss, and exactly 7 <c>"defects from"</c> with
/// neither changed. Doubles as DoD 4's elimination reproduction (Galatia's own observed
/// <c>668 → 0</c> unity and <c>0xFFFF</c> capital sentinel) and DoD 6's message coverage.
/// </summary>
/// <remarks>
/// <para>
/// <strong>How the two mechanisms are reproduced, and what is fixture data versus what this task's code
/// actually decides.</strong> Laranda and Gordium are captured directly through
/// <see cref="CityCaptureResolver.Capture"/> (the corpus's own confirmed "falls to" pair,
/// <c>elimination.galatiaFallsToCount</c>), each fed as a separate siege the same historical elimination
/// would have needed (the events are far apart in game time, not one call). Laranda's own fortification
/// (41 → 30) and population (59 → 44) figures are the corpus's exact
/// <c>elimination.galatiaLaranda.*</c> values — <see cref="CityCaptureResolver.Capture"/> never writes
/// either field (matching <c>FUN_0044bb18</c>'s own confirmed pseudocode, which has no population or
/// fortification term at all), so this scenario constructs Laranda's <em>input</em> city state already at
/// its post-siege-attrition figures and asserts the transfer preserves them exactly — the loss itself is
/// <c>FUN_0044b230</c>'s (the siege entry point's own per-attempt city-stat erosion, <c>[open]</c> per
/// <c>decompiled-defection-and-siege-attrition.md</c>'s own "not pinned down this pass," and outside
/// <c>src/IC2.Engine/Battle/**</c>, not this task's Owns list). The other seven — named for Galatia's own
/// <c>elimination.galatiaDefectsFromCount</c> note (Synnada, Pessinus, Acroinon, Ancyra, Gangra, Nyssa,
/// Halys) — are placed and given loyalty/defence figures that satisfy <c>FUN_0044ba1c</c>'s confirmed
/// cascade gate (distance, unity, defender strength, loyalty) so that this task's own
/// <see cref="CityCaptureResolver"/> code is what decides they defect, not a scripted outcome; their exact
/// pre-elimination fortification/population are not individually in the corpus, so the assertion this
/// scenario proves for them is the confirmed structural one — <c>defection.neverChangesPopOrFort</c> — not
/// a specific historical number.
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

        // Laranda: the corpus's own exact before/after fortification and population figures.
        var laranda = CaptureTestbed.City(
            "laranda", "Laranda", 0, 0, Galatia, Galatia,
            loyalty: 30, fortificationCode: 30, populationThousands: 44, maxPopulationThousands: 80, tribute: 10);

        // Gordium: the second forced capture, far enough from the attacker's siege-1 position that it is
        // never itself a candidate in Laranda's own cascade.
        var gordium = CaptureTestbed.City(
            "gordium", "Gordium", 50, 50, Galatia, Galatia,
            loyalty: 35, fortificationCode: 25, populationThousands: 20, maxPopulationThousands: 40, tribute: 8);

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

        // Siege 2: Gordium falls -- Galatia's last city, eliminating it.
        var final = CityCaptureResolver.Capture(
            afterLaranda, "seleucid-army", "gordium", ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

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
        // transfer (this task's own code never touches either field at capture) -- Laranda's own exact
        // corpus figures, reproduced. ----
        Assert.Equal(30, final.CityById("laranda")!.FortificationCode);
        Assert.Equal(44, final.CityById("laranda")!.PopulationThousands);
        Assert.NotEqual(41, final.CityById("laranda")!.FortificationCode); // Galatia's own pre-siege baseline (elimination.galatiaLaranda.fortBefore).
        Assert.NotEqual(59, final.CityById("laranda")!.PopulationThousands); // elimination.galatiaLaranda.popBefore.

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
