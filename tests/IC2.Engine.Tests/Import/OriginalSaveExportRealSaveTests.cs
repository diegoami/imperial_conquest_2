using IC2.Data;
using IC2.Engine.Import;
using IC2.Inspect;
using Xunit;
using Xunit.Abstractions;

namespace IC2.Engine.Tests.Import;

/// <summary>
/// T185 Done-when 2 over the private corpus: an original save imported into native state and exported back
/// equals the original's canonical form except where the import or the export provably loses or recomputes
/// a field. Each such difference is named and counted; any other difference fails.
/// </summary>
public sealed class OriginalSaveExportRealSaveTests(ITestOutputHelper output)
{
    [SkippableFact]
    public void Import_then_export_reproduces_every_corpus_save_but_the_documented_fields()
    {
        var settings = LocalOriginalAssets.Settings;
        Skip.If(settings is null, "No private asset directory is configured.");
        var names = CorpusFileLocator.DiscoverFileNames(settings!).Keys
            .Where(n => n.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Skip.If(names.Count == 0, "No .sav files found in the configured corpus.");

        var differences = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int compared = 0, tombstoned = 0;
        foreach (var name in names)
        {
            var bytes = File.ReadAllBytes(CorpusFileLocator.TryResolve(settings!, name)!);
            var original = OriginalSaveCodec.Decode(bytes);
            var imported = OriginalSaveImporter.Import(bytes, name, RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario, "id", "label");
            if (original.Armies.Any(a => a.Owner < 0) || original.Fleets.Any(f => f.Owner < 0))
            {
                // The import drops the tombstones, so the tables no longer line up record for record; the
                // export must still succeed and satisfy the policy (ToDocument checks it).
                var withoutTombstones = OriginalSaveExporter.ToDocument(imported.Save, RealGameData.World, RealGameData.Ruleset,
                    OriginalSaveUiProfile.FromTemplate(original)).Document;
                Assert.Equal(original.Armies.Count(a => a.Owner >= 0), withoutTombstones.Armies.Count);
                tombstoned++;
                continue;
            }

            var exported = OriginalSaveExporter.ToDocument(imported.Save, RealGameData.World, RealGameData.Ruleset, OriginalSaveUiProfile.FromTemplate(original)).Document;
            foreach (var field in Diff(original, exported)) differences[field] = differences.GetValueOrDefault(field) + 1;
            compared++;
        }

        output.WriteLine($"{compared} compared, {tombstoned} with tombstones exported without them.");
        foreach (var (field, count) in differences) output.WriteLine($"  {field}: {count} files");
        Assert.True(compared > 0);
        // Each allowed difference is a field the native state does not hold or the export recomputes
        // (OriginalSaveExporter's remarks): anything else is a mapping error.
        string[] allowed =
        [
            "army negative moves clamped by the import",          // OriginalSaveImporter clamps the original's underflow
            "building fleet leftover moves zeroed",               // gate-2: moves 0 under construction
            "eliminated nation stale count corrected",            // the original's stale count; the export writes the list length
            "fleet.route",                                        // the AI waypoint is not native state; x -1, y 0
            "map army -> army",                                   // army band recomputed (the original's lags)
            "map city -> city same owner",                        // city tier recomputed (the original's refreshes only on capture)
            "mercenary empty slot",                               // empty-slot residue
            "nation.conqueror",                                   // OriginalSaveImporter does not read +0x44E
            "nation.recruits all",                                // empty-entry residue and slot positions (live entries equal)
            "trailer.offerType",                                  // a stale offer type with no offer pending is not native state
        ];
        Assert.Empty(differences.Keys.Except(allowed));
    }

    /// <summary>The names of the fields that differ, one entry per field kind per file.</summary>
    private static IEnumerable<string> Diff(OriginalSaveDocument a, OriginalSaveDocument b)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        void Check(bool same, string field) { if (!same) found.Add(field); }

        for (var i = 0; i < a.Map.Count; i++)
        {
            if (a.Map[i] == b.Map[i]) continue;
            var (x, y) = (a.Map[i], b.Map[i]);
            found.Add($"map {Kind(x)} -> {Kind(y)}" + (Kind(x) == "city" && Kind(y) == "city" ? ((x - 20) % 16 == (y - 20) % 16 ? " same owner" : " OTHER OWNER") : ""));
        }

        for (var c = 0; c < a.Cities.Count; c++) Check(a.Cities[c] == b.Cities[c], "city");
        Check(a.Armies.Count == b.Armies.Count, "army count");
        for (var k = 0; k < Math.Min(a.Armies.Count, b.Armies.Count); k++)
        {
            var (x, y) = (a.Armies[k], b.Armies[k]);
            Check(x with { Units = [] } == y with { Units = [] } || x with { Units = [], Cell = 0 } != y with { Units = [], Cell = 0 }, "army.cell");
            // The import clamps a negative move count (an original underflow) to 0.
            Check(x with { Units = [], Cell = 0 } == y with { Units = [], Cell = 0 } || (x.Moves < 0 && y.Moves == 0
                && x with { Units = [], Cell = 0, Moves = 0 } == y with { Units = [], Cell = 0 }), "army header");
            Check(x.Moves >= 0 || y.Moves == 0, "army negative moves clamped");
            if (x.Moves < 0) found.Add("army negative moves clamped by the import");
            Check(x.Units.SequenceEqual(y.Units), "army units");
        }

        Check(a.Fleets.Count == b.Fleets.Count, "fleet count");
        for (var k = 0; k < Math.Min(a.Fleets.Count, b.Fleets.Count); k++)
        {
            var (x, y) = (a.Fleets[k], b.Fleets[k]);
            Check(x.RouteX == y.RouteX && x.RouteY == y.RouteY, "fleet.route");
            Check(x.Cell == y.Cell, "fleet.cell");
            Check(x with { RouteX = 0, RouteY = 0, Cell = 0, Moves = 0 } == y with { RouteX = 0, RouteY = 0, Cell = 0, Moves = 0 }, "fleet other");
            if (x.Moves != y.Moves) found.Add(x.Countdown >= 0 && y.Moves == 0 ? "building fleet leftover moves zeroed" : "fleet moves");
        }

        for (var n = 0; n < 16; n++)
        {
            var (x, y) = (a.Nations[n], b.Nations[n]);
            Check(x.Name == y.Name && x.Leader == y.Leader, "nation.text");
            Check(x.Relations.SequenceEqual(y.Relations), "nation.relations");
            Check(x.Neighbours == y.Neighbours, "nation.neighbours");
            Check(x.CityList.SequenceEqual(y.CityList), "nation.cityList");
            Check(x.Recruits.Where(r => r.Troops != 0).SequenceEqual(y.Recruits.Where(r => r.Troops != 0)), "nation.recruits live");
            Check(x.Recruits.SequenceEqual(y.Recruits), "nation.recruits all");
            Check(x.Colours.SequenceEqual(y.Colours), "nation.colours");
            Check(x.Wealth == y.Wealth, "nation.wealth");
            Check(x.Wealth0 == y.Wealth0, "nation.wealth0");
            Check(x.Treasury == y.Treasury, "nation.treasury");
            Check(x.Treasury0 == y.Treasury0, "nation.treasury0");
            Check(x.Unity == y.Unity && x.Mobilization == y.Mobilization && x.Tax == y.Tax && x.TaxBase == y.TaxBase, "nation.economy");
            Check(x.Capital == y.Capital, "nation.capital");
            Check(x.Cities == y.Cities || x.Capital >= 0, "eliminated nation stale count corrected");
            Check(x.Cities == y.Cities || x.Capital < 0, "nation.cities");
            Check(x.Cities0 == y.Cities0, "nation.cities0");
            Check(x.Conqueror == y.Conqueror, "nation.conqueror");
            Check(x.Controller == y.Controller, "nation.controller");
        }

        for (var m = 0; m < 50; m++)
        {
            var (x, y) = (a.Mercenaries[m], b.Mercenaries[m]);
            if (x == y) continue;
            found.Add(x.Troops is -1 or 0 ? "mercenary empty slot" : "mercenary live slot");
        }

        Check(a.News.SequenceEqual(b.News), "news");
        var (t, u) = (a.Trailer, b.Trailer);
        Check(t.TurnOrder.SequenceEqual(u.TurnOrder) && t.CurrentNation == u.CurrentNation && t.SeatIndex == u.SeatIndex, "trailer.turn");
        Check(t.OfferFrom == u.OfferFrom, "trailer.offerFrom");
        Check(t.OfferType == u.OfferType, "trailer.offerType");
        Check(t.Week == u.Week && t.YearBc == u.YearBc && t.Season == u.Season, "trailer.calendar");
        return found;
    }

    private static string Kind(short code) => code switch
    {
        1 => "rough",
        >= 0 and <= 11 => "terrain",
        >= 20 and <= 99 => "city",
        >= 200 and <= 247 => "army",
        >= 300 and <= 347 => "fleet",
        _ => $"other({code})",
    };
}
