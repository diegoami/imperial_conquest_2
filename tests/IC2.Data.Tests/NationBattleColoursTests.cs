using System.Buffers.Binary;
using System.Text.Json;
using IC2.Data;
using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// T122 Done-when 6 (local-only): each of the 16 nations' three battle-icon colours, read from the
/// start save the fixtures name, decode to the exported world's <c>nations[].battleColorsHex</c>; the
/// start save and a second save from a different campaign agree per nation. The three words are the
/// nation record's <c>+0x424</c>, <c>+0x428</c> and <c>+0x42C</c> that <c>FUN_0044A6C8</c>
/// substitutes for a tactical-battle icon's purple, white and blue
/// (<c>2026-10-04-decompiled-tactical-battle-rules.md</c> §10).
/// </summary>
/// <remarks>
/// The values' home was read at implementation time: the SAV's three words sit in a 12-byte block
/// between its own recruitment queue (<c>+0x2E4..+0x424</c>) and wealth (<c>+0x430</c>); the DAT has
/// no such block (its queue <c>+0x2C9..+0x409</c> runs straight into wealth), so the colours come from
/// the SAV only. The DAT genuinely lacks them, but <see cref="NationRecord.BattleColors"/> is a
/// modelled absence and not evidence —
/// <see cref="The_dat_genuinely_lacks_battle_colours"/> searches the raw DAT bytes for every one of
/// the 16 triples instead. A different-campaign save agrees per nation, so the words are not runtime
/// state the player picks.
/// </remarks>
public class NationBattleColoursTests
{
    private const string StartSave = "1_rome_270_winter_11.sav";

    // A different campaign from the start save (the 2026-09-20 IP* batch, per FleetTombstoneTests'
    // own remark): stronger against "the player chooses them" than another save of the same
    // playthrough (T122 review N4). Skipped per-case when this machine's corpus lacks it.
    private const string OtherCampaignSave = "IP012B.sav";

    private static string WorldPath =>
        Path.Combine(LocalAssets.RepositoryRoot, "data", "worlds", "classical-mediterranean.json");

    [SkippableFact]
    public void Every_nations_colours_decode_to_the_worlds_battleColorsHex()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var secondPath = FixtureResolver.TryResolve(OtherCampaignSave);
        Skip.If(secondPath is null,
            $"'{OtherCampaignSave}' (a second, different campaign) is not present in the configured corpus on this machine.");

        var start = SaveNationTable.Parse(File.ReadAllBytes(FixtureResolver.ResolveOrThrow(StartSave)));
        var second = SaveNationTable.Parse(File.ReadAllBytes(secondPath!));

        using var world = JsonDocument.Parse(File.ReadAllText(WorldPath));
        var nations = world.RootElement.GetProperty("nations");
        Assert.Equal(16, nations.GetArrayLength());

        for (var i = 0; i < 16; i++)
        {
            var colors = start.Nations[i].BattleColors;
            Assert.NotNull(colors);
            Assert.Equal(3, colors!.Count);

            // The hazard's check: a save from another campaign agrees, nation by nation.
            Assert.Equal(start.Nations[i].BattleColors, second.Nations[i].BattleColors);

            var expected = colors.Select(ToHex).ToArray();
            var actual = nations[i].GetProperty("battleColorsHex").EnumerateArray()
                .Select(e => e.GetString())
                .ToArray();

            Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    /// The values' home: no nation's three SAV words occurs anywhere in the raw DAT bytes, so the DAT
    /// does not carry the colours and they must come from the SAV. This searches the bytes directly,
    /// not the parsed <see cref="NationRecord.BattleColors"/> (which <c>ParseDat</c> sets to null by
    /// construction, so asserting null there would only test the constant — T122 review B3).
    /// </summary>
    [SkippableFact]
    public void The_dat_genuinely_lacks_battle_colours()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);

        var dat = File.ReadAllBytes(LocalAssets.Settings!.DatPath);
        var start = SaveNationTable.Parse(File.ReadAllBytes(FixtureResolver.ResolveOrThrow(StartSave)));

        for (var i = 0; i < start.Nations.Count; i++)
        {
            var colors = start.Nations[i].BattleColors;
            Assert.NotNull(colors);
            var triple = new byte[12];
            for (var k = 0; k < 3; k++)
                BinaryPrimitives.WriteInt32LittleEndian(triple.AsSpan(k * 4, 4), colors![k]);

            Assert.True(dat.AsSpan().IndexOf(triple) < 0,
                $"nation {i}'s SAV battle-colour triple occurs in the DAT; the colours' home is not settled.");
        }
    }

    /// <summary>A SAV nation record decodes its three 4-byte <c>TColor</c> words to <c>#RRGGBB</c>.</summary>
    [SkippableFact]
    public void A_start_save_record_carries_three_opaque_colour_words()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);

        var start = SaveNationTable.Parse(File.ReadAllBytes(FixtureResolver.ResolveOrThrow(StartSave)));

        // Rome (nation 0). Its three words happen to equal the icon's own purple/white/blue defaults
        // (TColor 0x00BBGGRR 0x800080 / 0xFFFFFF / 0xFF0000), but the exporter reads them from the
        // record rather than substituting a fallback, so they must round-trip through ToHex.
        var rome = start.Nations[0].BattleColors;
        Assert.NotNull(rome);
        Assert.Equal(new[] { 0x00800080, 0x00FFFFFF, 0x00FF0000 }, rome);
        Assert.Equal(new[] { "#800080", "#FFFFFF", "#0000FF" }, rome!.Select(ToHex));
    }

    private static string ToHex(int color) =>
        $"#{color & 0xFF:X2}{(color >> 8) & 0xFF:X2}{(color >> 16) & 0xFF:X2}";
}
