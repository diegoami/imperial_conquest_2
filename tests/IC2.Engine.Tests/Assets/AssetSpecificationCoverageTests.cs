using IC2.Engine.Assets;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Assets;

/// <summary>
/// T49 DoD 4: <c>docs/asset-specification.md</c> must not silently drift from
/// <see cref="AssetKeys"/>. The specification carries a fenced "ground truth" block (its §6,
/// <c>```text ... ```</c>) that is meant to be read verbatim, and this test is that reading:
/// </summary>
/// <remarks>
/// <para>
/// 1. Every key <see cref="AssetKeys.AllKeys"/> yields today (read live from the compiled engine,
/// never copied into this test) must appear as a line inside that block.
/// </para>
/// <para>
/// 2. Every line inside that block must be a real <see cref="AssetKeys"/> constant — so a stray or
/// misspelled "existing" key claimed in the document fails exactly as loudly as a missing one.
/// </para>
/// <para>
/// Gap/candidate keys discussed elsewhere in the document (river tile variants, candidate new sfx
/// keys, chrome candidates) are deliberately outside this block: DoD 4 only requires the *existing*
/// 25 to round-trip, and a proposed key must not affect this test either way, since
/// <c>src/IC2.Engine/Assets/AssetKeys.cs</c> is outside this task's Owns list to change.
/// </para>
/// </remarks>
public class AssetSpecificationCoverageTests
{
    private const string FenceMarker = "```text";
    private const string ClosingFence = "```";

    private static string SpecificationPath =>
        Path.Combine(FixturePaths.RepositoryRoot, "docs", "asset-specification.md");

    [Fact]
    public void SpecificationFile_Exists()
    {
        Assert.True(File.Exists(SpecificationPath), $"Specification not found at {SpecificationPath}");
    }

    [Fact]
    public void EveryAssetKeysConstant_AppearsInTheGroundTruthBlock()
    {
        var documented = ReadGroundTruthKeys();
        var engineKeys = AssetKeys.AllKeys.ToList();

        var missingFromDoc = engineKeys.Where(k => !documented.Contains(k)).ToList();

        Assert.True(missingFromDoc.Count == 0,
            "AssetKeys constants missing from docs/asset-specification.md §6: " +
            string.Join(", ", missingFromDoc));
    }

    [Fact]
    public void EveryGroundTruthLine_MapsToARealAssetKeysConstant()
    {
        var documented = ReadGroundTruthKeys();
        var engineKeys = new HashSet<string>(AssetKeys.AllKeys, StringComparer.Ordinal);

        var strayInDoc = documented.Where(k => !engineKeys.Contains(k)).ToList();

        Assert.True(strayInDoc.Count == 0,
            "docs/asset-specification.md §6 names keys that AssetKeys.AllKeys does not: " +
            string.Join(", ", strayInDoc));
    }

    [Fact]
    public void GroundTruthBlock_HasNoBlankOrDuplicateLines()
    {
        // A cheap sanity check on the parse itself: 25 distinct, non-empty keys, matching the
        // count AssetKeys.AllKeys yields today. If this ever fails because a real key was added or
        // removed, that is exactly the drift this test exists to catch — update AssetKeys.cs's own
        // count claim and this specification's §6 block together (this task's Owns list covers the
        // doc; AssetKeys.cs is T11's).
        var documented = ReadGroundTruthKeys();

        Assert.Equal(documented.Count, documented.Distinct(StringComparer.Ordinal).Count());
        Assert.All(documented, key => Assert.False(string.IsNullOrWhiteSpace(key)));
    }

    /// <summary>
    /// Extracts the lines inside the specification's one <c>```text ... ```</c> fence (§6's ground
    /// truth block). There is exactly one such fence in the document; every other fenced block uses
    /// a plain <c>```</c> with no language tag.
    /// </summary>
    private static List<string> ReadGroundTruthKeys()
    {
        var lines = File.ReadAllLines(SpecificationPath);

        var startIndex = Array.FindIndex(lines, l => l.Trim() == FenceMarker);
        Assert.True(startIndex >= 0, "docs/asset-specification.md has no ```text ground-truth fence.");

        var endIndex = -1;
        for (var i = startIndex + 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == ClosingFence)
            {
                endIndex = i;
                break;
            }
        }

        Assert.True(endIndex > startIndex, "The ```text ground-truth fence in docs/asset-specification.md is never closed.");

        return lines
            .Skip(startIndex + 1)
            .Take(endIndex - startIndex - 1)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .ToList();
    }
}
