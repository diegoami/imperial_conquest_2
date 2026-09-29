using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Export;

/// <summary>
/// Bug #525: the classical world's <c>nations[].colorHex</c> must be the settled sixteen-nation
/// palette from <c>docs/asset-specification.md</c> §2.3 (T49), not the pre-T49 placeholder values
/// the T29 exporter shipped — Greece was dark brown, and Gaul (<c>#ef6c00</c>) sat close to Thracia
/// (<c>#d84315</c>). The game screen tints its markers from this field
/// (<c>godot/Assets/MarkerTint.cs</c>), so this is the colour a player actually sees.
/// </summary>
/// <remarks>
/// The expected values are §2.3's own <c>Hex</c> column, transcribed here in <c>NationCatalog</c>
/// order (0 Rome … 15 Thracia), deliberately as literals rather than read out of the Markdown at
/// test time: the test must fail on a world edit, not on a documentation edit. The ids are pinned
/// alongside the colours, because "index i" is only meaningful if the file really is in
/// <c>NationCatalog</c> order — otherwise a reordered file could pass with the wrong nation wearing
/// each colour.
/// </remarks>
public sealed class NationPaletteTests
{
    private static World Load() => GameDataLoader.LoadFile<World>(ExportedDataPaths.WorldFile);

    /// <summary>
    /// §2.3's palette, in NationCatalog order, exactly as that table prints it (uppercase hex).
    /// </summary>
    private static readonly (string Id, string Hex)[] SettledPalette =
    {
        ("rome", "#4C0D19"),
        ("carthage", "#DDB69C"),
        ("seleucid", "#671E0B"),
        ("ptolemaic", "#E1EC25"),
        ("macedonia", "#719D16"),
        ("numidia", "#C3E155"),
        ("gaul", "#107A16"),
        ("greece", "#93F6D6"),
        ("celtiberia", "#29796D"),
        ("illyria", "#A7DCF8"),
        ("dacia", "#237084"),
        ("bithynia", "#5F50E6"),
        ("galatia", "#4C0C8D"),
        ("armenia", "#D760E8"),
        ("media", "#581B45"),
        ("thracia", "#D46CBB"),
    };

    /// <summary>
    /// Every classical nation's colour is its §2.3 value, with the ids proving the file is in
    /// NationCatalog order. This is the assertion the bug is: on the unfixed file it fails on the
    /// first nation whose value is not §2.3's (Rome, whose placeholder was <c>#c62828</c>).
    /// </summary>
    [Fact]
    public void Every_nation_carries_its_section_2_3_colour_in_NationCatalog_order()
    {
        var world = Load();

        Assert.Equal(SettledPalette.Length, world.Nations.Count);
        Assert.Equal(
            SettledPalette.Select(n => n.Id).ToArray(),
            world.Nations.Select(n => n.Id).ToArray());

        for (var i = 0; i < SettledPalette.Length; i++)
        {
            var (id, hex) = SettledPalette[i];
            Assert.Equal(hex, world.Nations[i].ColorHex);
            Assert.Equal(id, world.Nations[i].Id);
        }
    }

    /// <summary>
    /// The sixteen values are pairwise distinct, on the world's own values and not only on the
    /// table above — a duplicate written into the JSON must fail here even if the table is right.
    /// Case-insensitive, since two spellings of one colour are still one colour.
    /// </summary>
    [Fact]
    public void The_sixteen_palette_colours_are_distinct()
    {
        var table = SettledPalette.Select(n => n.Hex).ToArray();
        Assert.Equal(16, table.Length);
        Assert.Equal(16, table.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var world = Load().Nations.Select(n => n.ColorHex).ToArray();
        Assert.Equal(16, world.Length);
        Assert.Equal(16, world.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// Each nation's <c>colorHex</c> provenance cites §2.3, so the value's source is the settled
    /// palette and not, as the shipped placeholder said, "pending T49's asset specification".
    /// </summary>
    [Fact]
    public void Every_nations_colorHex_provenance_cites_the_settled_palette()
    {
        foreach (var nation in Load().Nations)
        {
            var source = nation.Provenance?.SourceFor("colorHex");

            Assert.False(
                string.IsNullOrEmpty(source),
                $"Nation '{nation.Id}' has no colorHex provenance at all.");
            Assert.Contains("asset-specification.md", source, StringComparison.Ordinal);
            Assert.Contains("2.3", source, StringComparison.Ordinal);
        }
    }
}
