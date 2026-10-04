using System.Text.Json;
using IC2.Data;
using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// T122 Done-when 6 (local-only): each of the 16 nations' three battle-icon colours, read from the
/// DAT's own three words where it carries them and otherwise from the start save the fixtures name,
/// decode to the exported world's <c>nations[].battleColorsHex</c>; the start save and a second save
/// agree per nation. The three words are the nation record's <c>+0x424</c>, <c>+0x428</c> and
/// <c>+0x42C</c> that <c>FUN_0044A6C8</c> substitutes for a tactical-battle icon's purple, white and
/// blue (<c>2026-10-04-decompiled-tactical-battle-rules.md</c> §10).
/// </summary>
/// <remarks>
/// The values' home was read at implementation time: the DAT's 1,055-byte nation record ends at the
/// recruitment queue and carries none of the three (see
/// <see cref="The_dat_genuinely_lacks_battle_colours"/>), no triple appears anywhere in the DAT, and
/// three separate saves agree per nation — so the world's values come from the start save. The
/// separate second-save comparison is the T122 hazard's check: if the words differed between saves of
/// the same nation, they would be runtime state, not world data.
/// </remarks>
public class NationBattleColoursTests
{
    private const string StartSave = "1_rome_270_winter_11.sav";
    private const string SecondSave = "1_rome_270_summer_7.sav";

    private static string WorldPath =>
        Path.Combine(LocalAssets.RepositoryRoot, "data", "worlds", "classical-mediterranean.json");

    [SkippableFact]
    public void Every_nations_colours_decode_to_the_worlds_battleColorsHex()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);

        var dat = SaveNationTable.Parse(File.ReadAllBytes(LocalAssets.Settings!.DatPath));
        var start = SaveNationTable.Parse(File.ReadAllBytes(FixtureResolver.ResolveOrThrow(StartSave)));
        var second = SaveNationTable.Parse(File.ReadAllBytes(FixtureResolver.ResolveOrThrow(SecondSave)));

        using var world = JsonDocument.Parse(File.ReadAllText(WorldPath));
        var nations = world.RootElement.GetProperty("nations");
        Assert.Equal(16, nations.GetArrayLength());

        for (var i = 0; i < 16; i++)
        {
            // The DAT's own three words when it carries them (it does not today), else the start save's.
            var source = dat.Nations[i].BattleColors ?? start.Nations[i].BattleColors;
            Assert.NotNull(source);
            Assert.Equal(3, source!.Count);

            // The hazard's check: the start save and a second save agree, nation by nation.
            Assert.Equal(start.Nations[i].BattleColors, second.Nations[i].BattleColors);

            var expected = source.Select(ToHex).ToArray();
            var actual = nations[i].GetProperty("battleColorsHex").EnumerateArray()
                .Select(e => e.GetString())
                .ToArray();

            Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    /// Pins the read that decides the values' home: the DAT genuinely carries none of the three words,
    /// so <see cref="NationRecord.BattleColors"/> is <see langword="null"/> on every DAT record.
    /// </summary>
    [SkippableFact]
    public void The_dat_genuinely_lacks_battle_colours()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);

        var dat = SaveNationTable.Parse(File.ReadAllBytes(LocalAssets.Settings!.DatPath));

        Assert.Equal(16, dat.Nations.Count);
        Assert.All(dat.Nations, nation => Assert.Null(nation.BattleColors));
    }

    /// <summary>A SAV nation record decodes its three 4-byte <c>TColor</c> words to <c>#RRGGBB</c>.</summary>
    [SkippableFact]
    public void A_start_save_record_carries_three_opaque_colour_words()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);

        var start = SaveNationTable.Parse(File.ReadAllBytes(FixtureResolver.ResolveOrThrow(StartSave)));

        // Rome, the record this test's report's §10 example names first: #800080, #FFFFFF, #0000FF.
        var rome = start.Nations[0].BattleColors;
        Assert.NotNull(rome);
        Assert.Equal(new[] { 0x00800080, 0x00FFFFFF, 0x00FF0000 }, rome);
        Assert.Equal(new[] { "#800080", "#FFFFFF", "#0000FF" }, rome!.Select(ToHex));
    }

    private static string ToHex(int color) =>
        $"#{color & 0xFF:X2}{(color >> 8) & 0xFF:X2}{(color >> 16) & 0xFF:X2}";
}
