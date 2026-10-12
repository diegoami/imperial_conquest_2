using System.Buffers.Binary;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;
using Xunit.Extensions;

namespace IC2.Engine.Tests.Export;

/// <summary>
/// T81 Done-when 1: the 20 centres of the weekly rough-sea overlay are world data exported from the DAT at
/// <c>0x1F876</c> through the exporter, each with its provenance. Mirrors
/// <c>MercenaryTemplateExportTests</c>: the count and named centres are asserted against the DAT's own
/// bytes, which are a local-only original file, so the byte-level tests skip (never pass silently) without it.
/// </summary>
public sealed class WeatherCentreExportTests
{
    // decompiled-map-code1-overlay.md §1 [confirmed: DAT bytes]: FUN_004481a0 reads 0x50 bytes into
    // DAT_00479540 at DAT 0x1F876 -- 20 records of two signed 16-bit little-endian words (x, y).
    private const int WeatherCentreOffset = 0x1F876;
    private const int WeatherCentreCount = 20;
    private const int WeatherCentreRecordLength = 4;

    private static readonly Lazy<World> LazyWorld = new(
        () => GameDataLoader.LoadFile<World>(ExportedDataPaths.WorldFile));

    [Fact]
    public void The_shipped_world_carries_twenty_centres_with_a_provenance_note_citing_the_dat_offset()
    {
        var world = LazyWorld.Value;

        Assert.Equal(WeatherCentreCount, world.WeatherCentres!.Count);

        var note = world.Provenance!.SourceFor("weatherCentres");
        Assert.NotNull(note);
        Assert.StartsWith("confirmed:", note, StringComparison.Ordinal);
        Assert.Contains("0x1F876", note, StringComparison.Ordinal);
        Assert.Contains("decompiled-map-code1-overlay.md", note, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void The_twenty_centres_reproduce_from_the_DAT_bytes_record_for_record()
    {
        Skip.IfNot(OriginalFilesAvailability.IsConfigured, OriginalFilesAvailability.SkipReason);

        var dat = File.ReadAllBytes(OriginalFilesAvailability.DatPath!);
        var centres = LazyWorld.Value.WeatherCentres!;

        Assert.Equal(WeatherCentreCount, centres.Count);
        for (var i = 0; i < WeatherCentreCount; i++)
        {
            var at = WeatherCentreOffset + (i * WeatherCentreRecordLength);
            var x = BinaryPrimitives.ReadInt16LittleEndian(dat.AsSpan(at, 2));
            var y = BinaryPrimitives.ReadInt16LittleEndian(dat.AsSpan(at + 2, 2));

            Assert.True(
                centres[i].X == x && centres[i].Y == y,
                $"Centre {i} does not reproduce from the DAT: world says ({centres[i].X}, {centres[i].Y}), DAT 0x{at:X} reads ({x}, {y}).");
        }
    }

    [SkippableTheory]
    [InlineData(0, 10, 10)]   // the first, the north-west corner of the Atlantic approaches
    [InlineData(2, 74, 50)]
    [InlineData(10, 177, 83)]
    [InlineData(19, 233, 22)] // the last
    public void Named_centres_match_both_the_report_and_the_DAT(int index, int x, int y)
    {
        Skip.IfNot(OriginalFilesAvailability.IsConfigured, OriginalFilesAvailability.SkipReason);

        // The values are the report's own list (decompiled-map-code1-overlay.md §1): (10,10) (32,79)
        // (74,50) (68,68) (92,68) (100,56) (105,81) (131,66) (126,89) (148,86) (177,83) ... (233,22).
        var dat = File.ReadAllBytes(OriginalFilesAvailability.DatPath!);
        var at = WeatherCentreOffset + (index * WeatherCentreRecordLength);

        Assert.Equal(x, BinaryPrimitives.ReadInt16LittleEndian(dat.AsSpan(at, 2)));
        Assert.Equal(y, BinaryPrimitives.ReadInt16LittleEndian(dat.AsSpan(at + 2, 2)));
        Assert.Equal(new WeatherCentre(x, y), LazyWorld.Value.WeatherCentres![index]);
    }

    [Fact]
    public void Every_centre_lies_at_least_the_clear_radius_from_every_map_edge()
    {
        // The report: all 20 are at least 10 from every edge, so the unclamped clear box never leaves the grid.
        var world = LazyWorld.Value;
        var margin = RealRulesetClearRadius();

        Assert.All(world.WeatherCentres!, c =>
        {
            Assert.InRange(c.X, margin, world.Width - 1 - margin);
            Assert.InRange(c.Y, margin, world.Height - 1 - margin);
        });
    }

    private static int RealRulesetClearRadius() =>
        GameDataLoader.LoadFile<Ruleset>(ExportedDataPaths.RulesetFile).Economy.Weather.ClearRadiusTiles;
}
