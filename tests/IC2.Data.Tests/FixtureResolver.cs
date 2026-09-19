namespace IC2.Data.Tests;

/// <summary>
/// Resolves a fixture file by NAME ALONE — never a folder-prefixed path — immune to which of the
/// documented save-folder locations currently holds it. Issue #203: seven tests hardcoded
/// <c>saves/&lt;name&gt;.sav</c> for files that later moved to <c>saves-processed/</c>, because the
/// author followed operating-guide.md §1.2's own convention ("move a save to <c>saves-processed/</c>
/// once a report cites it"). The convention is right; the hardcoded folder was the bug.
/// </summary>
/// <remarks>
/// Search order, first hit wins (T53 Done-when line 1):
/// <list type="number">
/// <item><description><c>saves-processed/&lt;name&gt;</c> under <see cref="LocalAssets.Settings"/>'s
/// configured directory.</description></item>
/// <item><description><c>saves/&lt;name&gt;</c> under the same directory.</description></item>
/// <item><description><c>releases/&lt;tag&gt;/**/&lt;name&gt;</c> under the same directory — the
/// disposable per-run release cache (operating-guide.md §1.2), searched last because it is the
/// slowest and least authoritative copy, and only when the first two folders don't have it.</description></item>
/// </list>
/// <para><c>LocalAssets</c> already resolves <c>IC2_FIXTURES_DIR</c> (CI's fetch of the
/// <c>ic2-test-fixtures</c> clone — the DAT plus the whole 54-save corpus, not a named subset; see
/// that repo's own README and issue #207) ahead of a developer's own <c>assets.local.ini</c> and points
/// <see cref="LocalAssets.Settings"/>'s directory at whichever one is configured — so "the CI fixtures
/// directory when set" is exactly step 1/2 above running against that directory: the fixtures repo's
/// own layout has no <c>saves-processed/</c> (so that check is a harmless miss) and everything sits
/// flat under <c>saves/</c>, which step 2 finds. No separate CI-only branch is needed here.</para>
/// </remarks>
internal static class FixtureResolver
{
    public const string DatFileName = "Imperial Conquest 2.dat";

    /// <summary>Resolves <paramref name="fileName"/> (bare — e.g. <c>"11.sav"</c> or
    /// <see cref="DatFileName"/>, never a folder-prefixed path) to its on-disk location across every
    /// folder in the search order, or null if it is present in none of them (or nothing is
    /// configured).</summary>
    public static string? TryResolve(string fileName) =>
        Candidates(fileName).FirstOrDefault(File.Exists);

    /// <summary>Resolves <paramref name="fileName"/> or throws, naming every location searched — so a
    /// stale or missing fixture fails loudly, naming the resolved (or attempted) path, instead of a
    /// bare <see cref="FileNotFoundException"/> pointing at one hardcoded guess (issue #203).</summary>
    public static string ResolveOrThrow(string fileName)
    {
        var tried = Candidates(fileName).ToList();
        var hit = tried.FirstOrDefault(File.Exists);
        if (hit is not null) return hit;

        // tried.Count == 0 (nothing configured) is unreachable through every current call site — each
        // one sits behind Skip.IfNot(LocalAssets.IsConfigured, ...) — but this is a small, reusable
        // resolver, not a test method, so it stays defensive against a future caller that resolves a
        // fixture without checking IsConfigured first, rather than assuming today's callers forever.
        throw new FileNotFoundException(
            $"Fixture '{fileName}' was not found. Searched, in order: " +
            (tried.Count == 0 ? "(nothing — no asset directory is configured)" : string.Join(" | ", tried)) +
            (string.IsNullOrEmpty(LocalAssets.SkipReason) ? "." : $" ({LocalAssets.SkipReason})."));
    }

    private static IEnumerable<string> Candidates(string fileName)
    {
        if (LocalAssets.Settings is not { } settings)
            yield break;

        if (fileName == DatFileName)
        {
            yield return settings.DatPath;
            yield break;
        }

        yield return Path.Combine(settings.DirectoryPath, "saves-processed", fileName);
        yield return Path.Combine(settings.DirectoryPath, "saves", fileName);

        var releasesDir = Path.Combine(settings.DirectoryPath, "releases");
        if (!Directory.Exists(releasesDir)) yield break;

        IEnumerable<string> tagDirs;
        try
        {
            tagDirs = Directory.GetDirectories(releasesDir).OrderBy(d => d, StringComparer.Ordinal);
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var tagDir in tagDirs)
        {
            string? hit;
            try
            {
                // Ordered explicitly: Directory.EnumerateFiles' own order is filesystem-dependent, and
                // a release cache is the one search location where the same name could genuinely
                // appear more than once (screenshots/notes get restructured more often than saves do)
                // — picking a stable, sorted "first" keeps a re-run deterministic instead of picking
                // whichever copy the OS happened to enumerate first.
                hit = Directory.EnumerateFiles(tagDir, fileName, SearchOption.AllDirectories)
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .FirstOrDefault();
            }
            catch (IOException)
            {
                continue;
            }
            if (hit is not null) yield return hit;
        }
    }
}
