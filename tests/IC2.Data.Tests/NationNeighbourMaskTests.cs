using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// T85 Done-when 1 (#387, correction for #385): <see cref="SaveNationTable.NeighbourMasks"/>, the
/// 16-bit neighbour mask at DAT nation-record <c>+0x2B</c> (<see cref="DatLayout.NationNeighbourOffset"/>)
/// / SAV runtime <c>+0x46</c> — 24 symmetric pairs, no nation neighbouring itself, and Rome's own row
/// (Carthage, Gaul, Illyria). See
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/dat-neighbour-mask.md
/// §2, "The mask in the DAT".
/// </summary>
public class NationNeighbourMaskTests
{
    // Nation indices, per NationCatalog: Rome 0, Carthage 1, Seleucid 2, Ptolemaic 3, Macedonia 4,
    // Numidia 5, Gaul 6, Greece 7, Celtiberia 8, Illyria 9, Dacia 10, Bithynia 11, Galatia 12,
    // Armenia 13, Media 14, Thracia 15.

    /// <summary>Decodes a 16-bit mask into the set of bit indices ("nation codes") it has set.</summary>
    private static List<int> NeighboursOf(ushort mask)
    {
        var result = new List<int>();
        for (var bit = 0; bit < 16; bit++)
        {
            if ((mask & (1 << bit)) != 0)
            {
                result.Add(bit);
            }
        }
        return result;
    }

    [SkippableFact]
    public void Dat_mask_is_symmetric_with_no_self_bit_and_exactly_24_pairs_and_romes_row_matches()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(LocalAssets.Settings!.DatPath);
        var table = SaveNationTable.Parse(data);
        var masks = table.NeighbourMasks;

        Assert.Equal(16, table.Nations.Count);
        Assert.Equal(16, masks.Count);

        // No nation neighbours itself.
        for (var i = 0; i < masks.Count; i++)
        {
            Assert.False((masks[i] & (1 << i)) != 0, $"Nation {i} borders itself.");
        }

        // Symmetric: bit j of nation i's mask == bit i of nation j's mask.
        var pairCount = 0;
        for (var i = 0; i < masks.Count; i++)
        {
            for (var j = i + 1; j < masks.Count; j++)
            {
                var iHasJ = (masks[i] & (1 << j)) != 0;
                var jHasI = (masks[j] & (1 << i)) != 0;
                Assert.True(iHasJ == jHasI, $"Mask asymmetric between nation {i} and nation {j}.");
                if (iHasJ)
                {
                    pairCount++;
                }
            }
        }

        Assert.Equal(24, pairCount);

        // Rome (0)'s row: Carthage (1), Gaul (6), Illyria (9).
        Assert.Equal(new[] { 1, 6, 9 }, NeighboursOf(masks[0]));
    }

    /// <summary>
    /// Done-when 1's second test: at least three local saves' own <c>+0x46</c> equal the DAT's mask —
    /// "all 16 rows are identical in every one of the 101 local saves" (report §"Answer" item 2).
    /// </summary>
    [SkippableFact]
    public void At_least_three_local_saves_neighbour_masks_equal_the_dats()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var datMasks = SaveNationTable.Parse(File.ReadAllBytes(LocalAssets.Settings!.DatPath)).NeighbourMasks;

        // Three saves spanning the corpus's own date range (report §2's "against the saves" table):
        // the earliest (spring, week 1) and a later one from a different campaign each.
        string[] saveNames =
        {
            "1_rome_270_winter_11.sav",
            "1_thracia_271_spring_1.sav",
            "1_cartago_271_spring_1.sav",
        };

        foreach (var saveName in saveNames)
        {
            var saveMasks = SaveNationTable.Parse(File.ReadAllBytes(FixtureResolver.ResolveOrThrow(saveName))).NeighbourMasks;
            Assert.Equal(16, saveMasks.Count);
            for (var i = 0; i < 16; i++)
            {
                Assert.Equal(datMasks[i], saveMasks[i]);
            }
        }
    }
}
