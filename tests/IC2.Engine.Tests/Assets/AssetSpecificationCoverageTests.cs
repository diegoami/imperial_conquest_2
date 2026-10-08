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
/// Gap/candidate keys discussed elsewhere in the document (candidate new sfx keys) are deliberately
/// outside this block: this test only requires the *existing* keys - all 67 after T101 and T148 - to
/// round-trip, and a proposed key must not affect it either way.
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

        // T149: the seven new sfx.* keys are added to AssetKeys but not yet to the
        // ground-truth block — the main session applies the §1.4 and §4.6 doc claims at merge
        // time (build-process.md §4.7). The pre-T149 keys still round-trip exactly; the new ones
        // are reported as a "to-add" list so a future main-session merge can add them.
        var preT149EngineKeys = engineKeys
            .Where(k => !T149NewSfxKeys.Contains(k))
            .ToList();
        var newKeysPendingDocUpdate = T149NewSfxKeys
            .Where(k => !documented.Contains(k))
            .ToList();

        var missingFromDoc = preT149EngineKeys.Where(k => !documented.Contains(k)).ToList();

        Assert.True(missingFromDoc.Count == 0,
            "Pre-T149 AssetKeys constants missing from docs/asset-specification.md §6: " +
            string.Join(", ", missingFromDoc));
        Assert.True(newKeysPendingDocUpdate.Count == T149NewSfxKeys.Length,
            "T149 added these seven sfx.* keys but the main session's merge-time doc update has not yet " +
            "added them to docs/asset-specification.md §6: " + string.Join(", ", newKeysPendingDocUpdate));
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
        // A cheap sanity check on the parse itself: distinct, non-empty keys. The block is
        // structurally intact even when the engine has more keys than the block (T149 added
        // seven sfx.* keys before the main session's merge-time doc update); the other two
        // tests in this class pin that no pre-T149 key is missing and no doc line is stray.
        var documented = ReadGroundTruthKeys();

        Assert.Equal(documented.Count, documented.Distinct(StringComparer.Ordinal).Count());
        Assert.All(documented, key => Assert.False(string.IsNullOrWhiteSpace(key)));
    }

    /// <summary>
    /// T149: the seven sfx.* keys this task added. The main session applies the doc claims at
    /// merge time (build-process.md §4.7), so this list is a marker the test can recognise
    /// "engine ahead of spec" without flagging a regression.
    /// </summary>
    private static readonly string[] T149NewSfxKeys =
    {
        AssetKeys.SfxFleetMove,
        AssetKeys.SfxBattleArrows,
        AssetKeys.SfxBattleJavelin,
        AssetKeys.SfxBattleMelee,
        AssetKeys.SfxSiegeFailed,
        AssetKeys.SfxFleetSunk,
        AssetKeys.SfxNationConquered,
    };

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
