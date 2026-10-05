using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Export;

/// <summary>
/// T97: the classical world's <c>nations[].colorHex</c> and <c>nations[].glyphColorHex</c> must be the
/// original's own marker colours from the research repository's
/// <c>docs/reports/2026-09-29-nation-marker-colours.md</c> — the <c>colorHex</c> is the background
/// square, the <c>glyphColorHex</c> the foreground glyph drawn on it. That report supersedes T49's
/// designed palette (bug #525), which the game screen used to tint its markers from
/// (<c>godot/Assets/MarkerTint.cs</c>): the original draws every city, army and fleet marker as a
/// square filled with the owner's background colour, with the glyph in the owner's foreground colour.
/// </summary>
/// <remarks>
/// The expected values are the report's own table, transcribed here in <c>NationCatalog</c> order
/// (0 Rome … 15 Thracia), deliberately as literals rather than read out of the report at test time:
/// the test must fail on a world edit, not on a research edit. The ids are pinned alongside the
/// colours, because "index i" is only meaningful if the file really is in <c>NationCatalog</c> order —
/// otherwise a reordered file could pass with the wrong nation wearing each pair. The backgrounds are
/// deliberately not pairwise distinct (red: Carthage/Media; navy: Ptolemaic/Illyria); the pairs are.
/// </remarks>
public sealed class NationPaletteTests
{
    private static World Load() => GameDataLoader.LoadFile<World>(ExportedDataPaths.WorldFile);

    /// <summary>
    /// The report's table, in NationCatalog order, in the world's own uppercase <c>#RRGGBB</c>
    /// spelling. Column order: code, id, background (<c>colorHex</c>), foreground
    /// (<c>glyphColorHex</c>).
    /// </summary>
    private static readonly (string Id, string ColorHex, string GlyphColorHex)[] ReportPalette =
    {
        ("rome", "#800080", "#0000FF"),
        ("carthage", "#FF0000", "#FFFFFF"),
        ("seleucid", "#808000", "#800000"),
        ("ptolemaic", "#000080", "#FF00FF"),
        ("macedonia", "#FFFFFF", "#0000FF"),
        ("numidia", "#00FF00", "#008080"),
        ("gaul", "#800000", "#00FFFF"),
        ("greece", "#00FFFF", "#FF00FF"),
        ("celtiberia", "#FFFF00", "#FF0000"),
        ("illyria", "#000080", "#808000"),
        ("dacia", "#008000", "#FFFF00"),
        ("bithynia", "#008080", "#0000FF"),
        ("galatia", "#0000FF", "#00FFFF"),
        ("armenia", "#FF00FF", "#FF0000"),
        ("media", "#FF0000", "#800080"),
        ("thracia", "#808080", "#000000"),
    };

    /// <summary>
    /// Every classical nation carries the report's background in <c>colorHex</c> and its foreground in
    /// <c>glyphColorHex</c>, with the ids proving the file is in NationCatalog order. This is the
    /// assertion the correction is: on the pre-T97 file it fails on the first nation whose pair is not
    /// the report's (Rome, whose T49 background was <c>#4C0D19</c> and whose foreground was absent).
    /// </summary>
    [Fact]
    public void Every_nation_carries_the_reports_background_and_foreground_in_NationCatalog_order()
    {
        var world = Load();

        Assert.Equal(ReportPalette.Length, world.Nations.Count);
        Assert.Equal(
            ReportPalette.Select(n => n.Id).ToArray(),
            world.Nations.Select(n => n.Id).ToArray());

        for (var i = 0; i < ReportPalette.Length; i++)
        {
            var (id, colorHex, glyphColorHex) = ReportPalette[i];
            Assert.Equal(id, world.Nations[i].Id);
            Assert.Equal(colorHex, world.Nations[i].ColorHex);
            Assert.Equal(glyphColorHex, world.Nations[i].GlyphColorHex);
        }
    }

    /// <summary>
    /// The report's two shared backgrounds are shared on purpose, so the sixteen <em>pairs</em> — not
    /// the backgrounds — are what must be distinct: red is Carthage's and Media's, navy is Ptolemaic's
    /// and Illyria's, and only the foreground tells each pair apart. A duplicate pair written into the
    /// JSON fails here even if the table above is right. Case-insensitive, since two spellings of one
    /// colour are still one colour.
    /// </summary>
    [Fact]
    public void The_sixteen_background_foreground_pairs_are_distinct()
    {
        var table = ReportPalette.Select(n => Pair(n.ColorHex, n.GlyphColorHex)).ToArray();
        Assert.Equal(16, table.Length);
        Assert.Equal(16, table.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var world = Load().Nations.Select(n => Pair(n.ColorHex, n.GlyphColorHex)).ToArray();
        Assert.Equal(16, world.Length);
        Assert.Equal(16, world.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // The report's own shared-background pairs, pinned as such: exactly two backgrounds appear
        // twice, and each pair differs only by its foreground.
        Assert.Equal(2, world.GroupBy(pair => pair.Split('|')[0], StringComparer.OrdinalIgnoreCase).Count(g => g.Count() == 2));
        Assert.Equal(14, world.Select(pair => pair.Split('|')[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>One canonical, case-insensitive key for a (background, foreground) pair.</summary>
    private static string Pair(string colorHex, string? glyphColorHex) =>
        (colorHex + "|" + (glyphColorHex ?? string.Empty)).ToUpperInvariant();

    /// <summary>
    /// Each nation's <c>colorHex</c> and <c>glyphColorHex</c> provenance cites the report, so both
    /// values' source is the observation and not, as the shipped T49 note said, a designed palette.
    /// </summary>
    [Fact]
    public void Every_nations_colorHex_and_glyphColorHex_provenance_cites_the_report()
    {
        foreach (var nation in Load().Nations)
        {
            foreach (var field in new[] { "colorHex", "glyphColorHex" })
            {
                var source = nation.Provenance?.SourceFor(field);

                Assert.False(
                    string.IsNullOrEmpty(source),
                    $"Nation '{nation.Id}' has no {field} provenance at all.");
                Assert.Contains("confirmed:", source, StringComparison.Ordinal);
                Assert.Contains("2026-09-29-nation-marker-colours.md", source, StringComparison.Ordinal);
            }
        }
    }
}
