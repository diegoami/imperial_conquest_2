using IC2.Inspect;

namespace IC2.Data.Tests;

/// <summary>
/// Resolves a fixture file by NAME ALONE — never a folder-prefixed path — immune to which of the
/// documented save-folder locations currently holds it. Issue #203: seven tests hardcoded
/// <c>saves/&lt;name&gt;.sav</c> for files that later moved to <c>saves-processed/</c>, because the
/// author followed operating-guide.md §1.2's own convention ("move a save to <c>saves-processed/</c>
/// once a report cites it"). The convention is right; the hardcoded folder was the bug.
/// </summary>
/// <remarks>
/// <para><b>One search order, in one place (#213).</b> This used to keep its own candidate list,
/// which omitted <c>saves-processed/processed/</c> and ordered <c>saves-processed/</c> ahead of
/// <c>saves/</c> — while <c>IC2.Inspect</c>'s <see cref="CorpusFileLocator"/> (used by the corpus
/// sweep) kept a different list that omitted <c>releases/&lt;tag&gt;/</c> entirely. A save present
/// only under <c>releases/</c> was found by this resolver's named-fixture tests and silently invisible
/// to the sweep, and the reverse for <c>saves-processed/processed/</c>. Both now delegate to
/// <see cref="CorpusFileLocator.TryResolve"/> and <see cref="CorpusFileLocator.SearchedLocations"/> —
/// the three plain save folders, then every <c>releases/&lt;tag&gt;/</c> subtree — so there is exactly
/// one list, and <see cref="CorpusFileLocator"/>'s own remarks state the duplicate rule (a
/// byte-identical repeat between a plain folder and a release cache is the same file, not an
/// ambiguity).</para>
/// <para><c>LocalAssets</c> already resolves <c>IC2_FIXTURES_DIR</c> (CI's fetch of the
/// <c>ic2-test-fixtures</c> clone — the DAT plus the whole 54-save corpus, not a named subset; see
/// that repo's own README and issue #207) ahead of a developer's own <c>assets.local.ini</c> and points
/// <see cref="LocalAssets.Settings"/>'s directory at whichever one is configured — so "the CI fixtures
/// directory when set" is exactly the same resolution running against that directory: the fixtures
/// repo's own layout has no <c>saves-processed/</c> (so that check is a harmless miss) and everything
/// sits flat under <c>saves/</c>, which the search finds. No separate CI-only branch is needed
/// here.</para>
/// </remarks>
internal static class FixtureResolver
{
    public const string DatFileName = CorpusFileLocator.DatFileName;

    /// <summary>Resolves <paramref name="fileName"/> (bare — e.g. <c>"11.sav"</c> or
    /// <see cref="DatFileName"/>, never a folder-prefixed path) to its on-disk location across every
    /// location in the search order, or null if it is present in none of them (or nothing is
    /// configured).</summary>
    public static string? TryResolve(string fileName) =>
        LocalAssets.Settings is { } settings ? TryResolve(settings, fileName) : null;

    /// <summary>As <see cref="TryResolve(string)"/>, but against an explicit
    /// <see cref="AssetSettings"/> rather than <see cref="LocalAssets.Settings"/> — internal-only, so
    /// <c>CorpusFileLocatorTests</c> (#213) can prove this resolver finds exactly what
    /// <see cref="CorpusFileLocator.TryResolve"/> finds, against a synthetic directory, without needing
    /// to fake <see cref="LocalAssets"/>' own static, once-computed <see cref="LocalAssets.Settings"/>.
    /// <see cref="TryResolve(string)"/> is this overload plus that one substitution, and is otherwise
    /// unchanged.</summary>
    internal static string? TryResolve(AssetSettings settings, string fileName) =>
        CorpusFileLocator.TryResolve(settings, fileName);

    /// <summary>Resolves <paramref name="fileName"/> or throws, naming every location searched — so a
    /// stale or missing fixture fails loudly, naming the resolved (or attempted) path, instead of a
    /// bare <see cref="FileNotFoundException"/> pointing at one hardcoded guess (issue #203).</summary>
    public static string ResolveOrThrow(string fileName)
    {
        if (LocalAssets.Settings is { } settings)
        {
            var hit = CorpusFileLocator.TryResolve(settings, fileName);
            if (hit is not null) return hit;

            var tried = CorpusFileLocator.SearchedLocations(settings, fileName).ToList();
            throw new FileNotFoundException(
                $"Fixture '{fileName}' was not found. Searched, in order: " + string.Join(" | ", tried) +
                (string.IsNullOrEmpty(LocalAssets.SkipReason) ? "." : $" ({LocalAssets.SkipReason})."));
        }

        // Unreachable through every current call site — each one sits behind
        // Skip.IfNot(LocalAssets.IsConfigured, ...) — but this is a small, reusable resolver, not a
        // test method, so it stays defensive against a future caller that resolves a fixture without
        // checking IsConfigured first, rather than assuming today's callers forever.
        throw new FileNotFoundException(
            $"Fixture '{fileName}' was not found. Searched, in order: (nothing — no asset directory is configured)" +
            (string.IsNullOrEmpty(LocalAssets.SkipReason) ? "." : $" ({LocalAssets.SkipReason})."));
    }
}
