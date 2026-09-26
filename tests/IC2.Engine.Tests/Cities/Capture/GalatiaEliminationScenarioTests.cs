using System.Linq;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// <c>docs/task-catalogue.md</c> T17, Done-when 1: a scripted scenario reproducing the confirmed Galatia
/// elimination (<c>galatia-elimination-and-city-resupply-confirmed.md</c>) city by city. T91 (bug #415)
/// corrects this scenario's own shape: the cascade's unity gate reads the LOSER's (Galatia's) own unity,
/// not the new owner's (Seleucid's) -- so Galatia's own confirmed 668 → 653 → 638 trajectory now decides
/// when the cascade fires, exactly as the save shows it. Laranda's capture alone (668 → 653) stays above
/// <see cref="CaptureRules.CascadeUnityThreshold"/> (650), so no cascade fires there at all; only Gordium's
/// own capture (653 → 638) crosses it, and only two of the six remaining candidates (Synnada and Acroinon)
/// also clear the cascade's other gates (distance, defense, loyalty) -- exactly the two the news log names.
/// The other five (Pessinus, Ancyra, Gangra, Nyssa, Halys) are conquered instead once Galatia drops below
/// <see cref="CaptureRules.ConquestCityCountThreshold"/>. Doubles as DoD 4's elimination reproduction
/// (Galatia's own observed <c>668 → 0</c> unity and <c>0xFFFF</c> capital sentinel) and DoD 6's message
/// coverage.
/// </summary>
/// <remarks>
/// <para>
/// <strong>T90/#409: "ancyra" is both a scripted defection candidate and Galatia's own capital.</strong>
/// This fixture names Galatia's capital <c>"ancyra"</c> (line below) and, independently, names one of the
/// seven weakly-defended candidate cities <c>"ancyra"</c> too — the same city. T90's corrected
/// <c>FUN_0044b8d0</c> gate excludes it from the cascade outright, because it IS Galatia's capital, so it
/// can only ever leave through the conquest cascade below (T86), never through defection — matching bug
/// #407's own symptom.
/// </para>
/// <para>
/// <strong>T91/#415: why the corrected gate leaves only Synnada and Acroinon as defectors, not six.</strong>
/// Before this fix, the gate read Seleucid's (the new owner's) own unity, which starts at 500 and stays
/// under 650 throughout regardless of what Galatia's own trajectory does — so every non-capital candidate
/// that cleared the OTHER gates defected during Laranda's own single capture call, leaving Galatia down to
/// one city (Gordium) immediately. Reading Galatia's own unity instead means NO candidate can defect until
/// Galatia's own unity first crosses 650, which does not happen until AFTER Gordium's own capture (653 →
/// 638) — so this scenario now drives two separate <see cref="CityCaptureResolver.Capture"/> calls, one per
/// historical siege, exactly as the two "falls to" events in the save's own news log imply. Pessinus,
/// Gangra, Nyssa and Halys are given loyalty at/above <see cref="CaptureRules.CascadeLoyaltyThreshold"/>
/// (65) so they fail the cascade's own separate loyalty gate even once Galatia's unity gate opens for
/// Gordium's own cascade — leaving exactly Synnada and Acroinon (loyalty 10, well under the threshold) to
/// defect, and the remaining five (Pessinus, Ancyra, Gangra, Nyssa, Halys) to fall to the conquest cascade
/// that fires in that same call once Galatia drops to 5 cities, under
/// <see cref="CaptureRules.ConquestCityCountThreshold"/> (6). This is exactly the save's own news log: two
/// "falls to" lines (Laranda, Gordium), two "defects from" lines (Synnada, Acroinon), and the conquest
/// banner taking the rest.
/// </para>
/// </remarks>
/// <remarks>
/// <para>
/// <strong>How the two mechanisms are reproduced, and what is fixture data versus what this task's code
/// actually decides.</strong> Laranda and Gordium are captured directly through
/// <see cref="CityCaptureResolver.Capture"/> (the corpus's own confirmed "falls to" pair,
/// <c>elimination.galatiaFallsToCount</c>), each fed as a separate siege the same historical elimination
/// would have needed (the events are far apart in game time, not one call, and now -- T91 -- also far apart
/// in which capture actually crosses Galatia's own unity gate). Both start this scenario at their real,
/// historical <em>pre-siege</em> fortification and population — Laranda 41/59, Gordium 54/23
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
/// The other seven — Galatia's remaining cities besides Laranda and Gordium, named after the save's own
/// news log (<c>galatia-elimination-and-city-resupply-confirmed.md</c>): Synnada, Pessinus, Acroinon,
/// Ancyra, Gangra, Nyssa, Halys — are placed and given loyalty/defence figures so that this task's own
/// <see cref="CityCaptureResolver"/> code, not a scripted outcome, decides which of them defect during
/// Gordium's own cascade (Synnada and Acroinon only, T91 — matching the corpus's own corrected
/// <c>elimination.galatiaDefectsFromCount</c>, 2) and which are left for the conquest cascade (Pessinus,
/// Ancyra, Gangra, Nyssa, Halys); their exact pre-elimination fortification/population are not
/// individually in the corpus, so the assertion this scenario proves for the two defectors is the
/// confirmed structural one — <c>defection.neverChangesPopOrFort</c> — not a
/// specific historical number.
/// </para>
/// </remarks>
public sealed class GalatiaEliminationScenarioTests
{
    private const string Galatia = "galatia";
    private const string Seleucid = "seleucid";

    private static readonly string[] DefectingCandidateIds = { "synnada", "acroinon" };
    private static readonly string[] ConqueredCandidateIds = { "pessinus", "ancyra", "gangra", "nyssa", "halys" };

    [Fact]
    public void ReproducesTheGalatiaEliminationPattern_TwoFallTo_TwoDefectFrom_ThenConquersTheRest()
    {
        var ruleset = CaptureTestbed.Ruleset;

        // Laranda: the corpus's own real, historical PRE-siege fortification and population (41/59) --
        // the input Capture receives, which it must leave untouched (see the class remarks).
        var laranda = CaptureTestbed.City(
            "laranda", "Laranda", 0, 0, Galatia, Galatia,
            loyalty: 30, fortificationCode: 41, populationThousands: 59, maxPopulationThousands: 80, tribute: 10);

        // Gordium: the second forced capture. Also its real, historical pre-siege fortification and
        // population (54/23). T91: unlike the pre-fix scenario, Gordium's own position no longer needs to
        // sit outside Laranda's own cascade -- Laranda's capture alone (668 -> 653) never crosses Galatia's
        // own unity gate (650), so no cascade of any kind fires during Laranda's own capture call.
        var gordium = CaptureTestbed.City(
            "gordium", "Gordium", 50, 50, Galatia, Galatia,
            loyalty: 35, fortificationCode: 54, populationThousands: 23, maxPopulationThousands: 40, tribute: 8);

        // Synnada and Acroinon: close to the SAME attacker position Gordium's own siege uses, low loyalty,
        // weakly defended -- exactly FUN_0044ba1c's gate once Galatia's own unity crosses 650 -- so
        // CityCaptureResolver's own cascade code, not a scripted outcome, decides they defect.
        var synnada = CaptureTestbed.City(
            "synnada", "Synnada", 1, 1, Galatia, Galatia,
            loyalty: 10, fortificationCode: 41, populationThousands: 6, maxPopulationThousands: 30, tribute: 2);
        var acroinon = CaptureTestbed.City(
            "acroinon", "Acroinon", 3, 1, Galatia, Galatia,
            loyalty: 10, fortificationCode: 43, populationThousands: 6, maxPopulationThousands: 30, tribute: 2);

        // Pessinus, Gangra, Nyssa and Halys: loyalty AT CascadeLoyaltyThreshold (65) or above -- they fail
        // the cascade's own separate loyalty gate even though Galatia's own unity gate is open by the time
        // Gordium's siege runs, so CityCaptureResolver's cascade code correctly leaves them for the
        // conquest cascade instead, exactly as the save's own news log shows (only Synnada and Acroinon
        // "defect from" Galatia).
        var pessinus = CaptureTestbed.City(
            "pessinus", "Pessinus", 2, 2, Galatia, Galatia,
            loyalty: 70, fortificationCode: 42, populationThousands: 7, maxPopulationThousands: 30, tribute: 2);
        var gangra = CaptureTestbed.City(
            "gangra", "Gangra", 4, 4, Galatia, Galatia,
            loyalty: 70, fortificationCode: 44, populationThousands: 9, maxPopulationThousands: 30, tribute: 2);
        var nyssa = CaptureTestbed.City(
            "nyssa", "Nyssa", 2, 4, Galatia, Galatia,
            loyalty: 70, fortificationCode: 42, populationThousands: 9, maxPopulationThousands: 30, tribute: 2);
        var halys = CaptureTestbed.City(
            "halys", "Halys", 4, 2, Galatia, Galatia,
            loyalty: 70, fortificationCode: 44, populationThousands: 7, maxPopulationThousands: 30, tribute: 2);

        // Ancyra: Galatia's own capital -- excluded from the cascade outright by the capital gate (T90),
        // regardless of loyalty, so its own loyalty value is unused by any gate and left at the same
        // figure the pre-fix scenario used.
        var ancyra = CaptureTestbed.City(
            "ancyra", "Ancyra", 1, 3, Galatia, Galatia,
            loyalty: 10, fortificationCode: 41, populationThousands: 8, maxPopulationThousands: 30, tribute: 2);

        var galatia = CaptureTestbed.Nation(Galatia, unity: 668, capitalCityId: "ancyra");
        var seleucid = CaptureTestbed.Nation(Seleucid, unity: 500);

        var attacker = CaptureTestbed.Army(
            "seleucid-army", Seleucid, 0, 0, morale: 80, CaptureTestbed.Unit("heavy_infantry", 50_000));

        var allCities = new[] { laranda, gordium, synnada, pessinus, acroinon, ancyra, gangra, nyssa, halys };
        var state = CaptureTestbed.StateWith(
            nations: new[] { galatia, seleucid }, cities: allCities, armies: new[] { attacker });

        Assert.Equal(9, state.CountCitiesOwnedBy(Galatia));

        var sink = new RecordingEventSink();

        // Siege 1: Laranda falls. Galatia's own unity: 668 -> 653 (CaptureUnityLoss 15) -- NOT below
        // CascadeUnityThreshold (650), so T91's own corrected gate fires the cascade for no candidate at
        // all, exactly matching the save (no "defects from" line accompanies Laranda's own "falls to").
        var afterLaranda = CityCaptureResolver.Capture(
            state, "seleucid-army", "laranda", ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        Assert.Empty(sink.Events.OfType<CityDefectsToNation>());
        Assert.Empty(sink.Events.OfType<NationConquered>());
        Assert.Equal(653, afterLaranda.NationById(Galatia)!.Unity);
        Assert.Equal(8, afterLaranda.CountCitiesOwnedBy(Galatia));

        // ---- T86: Laranda's own loyalty is a formula, not a flat floor -- allegiance ("galatia") differs
        // from the new owner ("seleucid"), so max(ForcedCaptureFloor, min(ForcedCaptureCap,
        // NonAllegiantTransferBase - L)) = max(40, min(60, 100 - 30)) = max(40, 60) = 60.
        Assert.Equal(60, afterLaranda.CityById("laranda")!.Loyalty);
        Assert.Equal(41, afterLaranda.CityById("laranda")!.FortificationCode);
        Assert.Equal(59, afterLaranda.CityById("laranda")!.PopulationThousands);

        // Siege 2: Gordium falls. Galatia's own unity: 653 -> 638 -- NOW below 650, so the cascade fires
        // for every one of Galatia's remaining candidates (everything but Gordium itself and "ancyra", the
        // capital). Only Synnada and Acroinon also clear the loyalty gate; the cascade's own second
        // defection (Acroinon) sees Galatia's unity at 638 - DefectionUnityLoss(20) = 618, still comfortably
        // under 650, so both defect in the same sweep. That leaves Galatia with 5 cities (Pessinus, Ancyra,
        // Gangra, Nyssa, Halys) -- under ConquestCityCountThreshold (6) -- so the SAME call's conquest
        // trigger (T86) sweeps them all away too, with no second siege needed.
        var final = CityCaptureResolver.Capture(
            afterLaranda, "seleucid-army", "gordium", ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        // ---- T86: Gordium's own loyalty formula -- allegiance ("galatia") differs from the new owner too:
        // max(40, min(60, 100 - 35)) = max(40, 60) = 60. Captured directly through a siege (T91: no longer
        // conquered), so it keeps its own pre-siege fortification/population exactly as Laranda does.
        Assert.Equal(60, final.CityById("gordium")!.Loyalty);
        Assert.Equal(54, final.CityById("gordium")!.FortificationCode);
        Assert.Equal(23, final.CityById("gordium")!.PopulationThousands);

        // ---- Final Seleucid totals. Laranda's contribution is 10*59/80 = 7 (treasury/tax base += 7*4 =
        // 28); Gordium's is 8*23/40 = 4 (+= 4*4 = 16 -- Gordium is a direct capture, CaptureTreasuryCreditMultiplier
        // 4, T91: not the conquest multiplier the pre-fix scenario used). Every defector's and every
        // conquered city's own contribution truncates to 0 (tribute 2 * population <= 9 / max 30), so
        // neither mechanism adds anything further to treasury or tax base. Unity: 500 (start) + 9 (Laranda
        // capture) + 9 (Gordium capture) + 3 + 3 (Synnada, Acroinon defections) + 50 (one flat conquest
        // gain covering the remaining five) = 574. Wealth is population*3000 summed over all nine cities
        // regardless of which mechanism moved each one: 59+23+6+7+6+8+9+9+7 = 134 -> 402,000 (unchanged
        // from before this fix, since wealth is mechanism-agnostic).
        var seleucidFinal = final.NationById(Seleucid)!;
        Assert.Equal(44, seleucidFinal.Treasury); // 28 + 16 + 0 (defections) + 0 (conquest sweep).
        Assert.Equal(44, seleucidFinal.TaxBase); // 28 + 16, the same generic multiplier either way.
        Assert.Equal(402_000, seleucidFinal.Wealth);
        Assert.Equal(574, seleucidFinal.Unity);
        Assert.Equal(9, final.CountCitiesOwnedBy(Seleucid)); // Every one of Galatia's 9 cities.

        // ---- Exactly 2 "falls to" (Laranda, Gordium -- the conquest sweep writes no such event of its
        // own, matching the historical Galatia record's own silent transfers for the other five), exactly
        // 2 "defects from" (Synnada, Acroinon) (DoD 1, DoD 4, DoD 6). ----
        var fallsTo = sink.Events.OfType<CityFallsToNation>().ToArray();
        var defectsFrom = sink.Events.OfType<CityDefectsToNation>().ToArray();
        Assert.Equal(2, fallsTo.Length);
        Assert.Equal(new[] { "Laranda", "Gordium" }, fallsTo.Select(e => e.CityName).ToArray());
        Assert.Equal(2, defectsFrom.Length);
        Assert.Equal(new[] { "Synnada", "Acroinon" }, defectsFrom.Select(e => e.CityName).ToArray());
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

        // ---- "ancyra" -- Galatia's own capital -- ends up owned by Seleucid through the conquest cascade,
        // never through defection (T90/#409). ----
        Assert.Equal(Seleucid, final.CityById("ancyra")!.Owner);
        Assert.DoesNotContain(defectsFrom, e => e.CityName == "Ancyra");

        // ---- Synnada and Acroinon: population and fortification exactly unchanged (defection.neverChangesPopOrFort). ----
        foreach (var id in DefectingCandidateIds)
        {
            var before = allCities.Single(c => c.Id == id);
            var after = final.CityById(id)!;
            Assert.Equal(before.FortificationCode, after.FortificationCode);
            Assert.Equal(before.PopulationThousands, after.PopulationThousands);
            Assert.Equal(Seleucid, after.Owner);
        }

        // ---- Pessinus, Ancyra, Gangra, Nyssa and Halys: taken by the conquest cascade instead -- also
        // owned by Seleucid, ConquestCascade's own effect list writes no fortification/population term
        // either. ----
        foreach (var id in ConqueredCandidateIds)
        {
            Assert.Equal(Seleucid, final.CityById(id)!.Owner);
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
