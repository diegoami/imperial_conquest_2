using System.Text.Json;
using IC2.Inspect;
using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// T34 #40 item 7: <c>SaveJsonExporter</c> must render <c>mercenaryOffers</c> as <c>null</c> for a DAT
/// export — like <c>turn</c>, <c>leader</c> and <c>humanPlayer</c> in the same export — never as
/// <c>[]</c> (a fabricated "empty pool" indistinguishable from a SAV that genuinely has zero
/// available offers).
/// </summary>
public class SaveJsonExporterTests : IDisposable
{
    private readonly string _outputPath =
        Path.Combine(Path.GetTempPath(), $"ic2-export-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_outputPath)) File.Delete(_outputPath);
    }

    [SkippableFact]
    public void MercenaryOffers_is_null_not_an_empty_array_for_a_dat_export()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;

        SaveJsonExporter.Export(settings.DatPath, _outputPath);
        var json = File.ReadAllText(_outputPath);

        using var doc = JsonDocument.Parse(json);
        var offers = doc.RootElement.GetProperty("mercenaryOffers");
        Assert.Equal(JsonValueKind.Null, offers.ValueKind);
    }

    [SkippableFact]
    public void MercenaryOffers_is_an_array_for_a_sav_export()
    {
        // Confirms the DAT-only null modelling did not regress the SAV path.
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;
        var savePath = settings.ResolveSavePath("saves-processed/1_rome_270_summer_7.sav");

        SaveJsonExporter.Export(savePath, _outputPath);
        var json = File.ReadAllText(_outputPath);

        using var doc = JsonDocument.Parse(json);
        var offers = doc.RootElement.GetProperty("mercenaryOffers");
        Assert.Equal(JsonValueKind.Array, offers.ValueKind);
    }

    // T44 (issue #126): the exported army's "moves" must be the signed value (-1 for the one
    // anomalous corpus record), never 65535, and "frozen" flags exactly that record — never blurred
    // with "aboardFleet" (a separate sentinel on a separate field).
    [SkippableFact]
    public void Army_moves_is_signed_and_frozen_flags_the_anomalous_record()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;
        var savePath = settings.ResolveSavePath("saves-processed/1_rome_270_summer_7.sav");

        SaveJsonExporter.Export(savePath, _outputPath);
        var json = File.ReadAllText(_outputPath);

        using var doc = JsonDocument.Parse(json);
        var armies = doc.RootElement.GetProperty("armies").EnumerateArray().ToList();

        var ptolemaic9 = armies.Single(a => a.GetProperty("index").GetInt32() == 9);
        Assert.Equal(-1, ptolemaic9.GetProperty("moves").GetInt32());
        Assert.True(ptolemaic9.GetProperty("frozen").GetBoolean());
        Assert.False(ptolemaic9.GetProperty("aboardFleet").GetBoolean());

        var rome0 = armies.Single(a => a.GetProperty("index").GetInt32() == 0);
        Assert.Equal(8, rome0.GetProperty("moves").GetInt32());
        Assert.False(rome0.GetProperty("frozen").GetBoolean());
    }
}
