using IC2.Data;
using IC2.Engine.Import;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Import;

/// <summary>
/// T81 Done-when 6: an imported save's code-1 cells become the imported rough-sea overlay, tested on a
/// winter save against the bytes (the seasonal means are 746 cells in Winter,
/// decompiled-map-code1-overlay.md §3). Skips, never passes silently, without the original files.
/// </summary>
public class RoughSeaImportTests
{
    // 1_rome_270_winter_3.sav is the one corpus save with a fleet whose covered cell (+24) is 1: its
    // marker hides a rough cell, which the overlay must still hold (report §3, "fleets with CoveredCell == 1").
    private const string WinterSaveWithFleetOnRoughSea = "1_rome_270_winter_3.sav";
    private const string WinterSave = "1_rome_270_winter_7.sav";

    [SkippableTheory]
    [InlineData(WinterSaveWithFleetOnRoughSea)]
    [InlineData(WinterSave)]
    public void The_imported_overlay_is_exactly_the_saves_code_1_cells_plus_the_cells_under_fleets_on_rough_sea(string fileName)
    {
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);

        var data = File.ReadAllBytes(OriginalFixture.ResolveOrThrow(fileName));
        var result = OriginalSaveImporter.Import(
            data, fileName, RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario,
            saveId: "imported-" + fileName, saveLabel: "Imported " + fileName);

        // Independently of the importer: the map grid is the first 89,600 bytes, 140 little-endian words
        // per column (x * 140 + y).
        var expected = new SortedSet<(int X, int Y)>();
        for (var x = 0; x < WorldPrefix.MapWidth; x++)
        {
            for (var y = 0; y < WorldPrefix.MapHeight; y++)
            {
                var offset = ((x * WorldPrefix.MapHeight) + y) * 2;
                if (BitConverter.ToUInt16(data, offset) == 1)
                {
                    expected.Add((x, y));
                }
            }
        }

        var code1Cells = expected.Count;
        foreach (var fleet in SaveFleetTable.Parse(data).Fleets)
        {
            var covered = (ushort)(fleet.RawByteAt(24) | (fleet.RawByteAt(25) << 8));
            if (fleet.IsLaunched && covered == 1)
            {
                expected.Add((fleet.X, fleet.Y));
            }
        }

        var imported = result.Save.State.RoughSeaCells!.Select(c => (c.X, c.Y)).ToList();

        Assert.Equal(expected.ToList(), imported);
        Assert.True(code1Cells > 100, $"A winter save is expected to carry hundreds of code-1 cells; found {code1Cells}.");

        // The fleet flag and the overlay agree: every fleet whose covered cell is 1 stands on an overlay cell.
        foreach (var fleet in result.Save.State.Fleets.Where(f => f.CoveredTileCode == 1))
        {
            Assert.Contains(fleet.X, result.Save.State.RoughSeaCells!.Where(c => c.Y == fleet.Y).Select(c => c.X));
        }
    }

    [SkippableFact]
    public void The_winter_3_save_has_a_fleet_on_rough_sea_and_the_import_keeps_its_flag()
    {
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);

        var data = File.ReadAllBytes(OriginalFixture.ResolveOrThrow(WinterSaveWithFleetOnRoughSea));
        var result = OriginalSaveImporter.Import(
            data, WinterSaveWithFleetOnRoughSea, RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario,
            saveId: "imported-winter-3", saveLabel: "Imported winter 3");

        var onRough = result.Save.State.Fleets.Where(f => f.CoveredTileCode == 1).ToList();
        Assert.Single(onRough);
        Assert.True(result.Save.State.IsRoughSea(onRough[0].X, onRough[0].Y));
    }
}
