using IC2.Data;

namespace IC2.Inspect;

/// <summary>
/// Locates corpus files by name only, independent of which of the documented locations currently
/// holds them. This is the point of the path-independent corpus fixture (bug #57, T34): a save
/// moved from <c>saves/</c> to <c>saves-processed/</c> — what <c>/process-evidence</c> does once a
/// research report cites it — must change no test outcome, because the fixture is keyed by file
/// name, not by a directory-relative path.
/// </summary>
/// <remarks>
/// <para><b>The one search order (#213).</b> Two fixture resolvers used to each keep their own list
/// of locations and disagree: this type's own <see cref="SaveDirs"/> omitted
/// <c>releases/&lt;tag&gt;/</c>, and <c>tests/IC2.Data.Tests/FixtureResolver</c> omitted
/// <c>saves-processed/processed/</c>. Both now draw from the locations this type enumerates —
/// <see cref="SaveDirs"/> for the three plain save folders, plus every <c>releases/&lt;tag&gt;/</c>
/// subtree (<see cref="SearchedLocations"/>) — so there is exactly one list, in one place, and
/// <c>FixtureResolver</c> delegates its own resolution to <see cref="TryResolve"/> rather than
/// keeping a second copy.</para>
/// <para><b>The duplicate rule (#213 hazard).</b> A name present in more than one of the three plain
/// <see cref="SaveDirs"/> is always ambiguous and <see cref="TryResolve"/> throws, unchanged from
/// before #213 — the operating guide's own "move, don't copy" convention means that should never
/// legitimately happen. A name present in both a plain save folder and under <c>releases/</c> is
/// different: the disposable per-run release cache (operating-guide.md §1.2,
/// <c>scripts/fetch-release.sh</c>) legitimately mirrors a save that is also already in a plain
/// folder — confirmed live on the configured corpus, where every one of
/// <c>releases/run-1-cartago/</c>'s ten saves is a byte-identical copy of a save already in
/// <c>saves/</c> or <c>saves-processed/</c>. The stated rule: identical bytes count as one file (the
/// plain-folder copy wins the resolution, since it is checked first); a byte-for-byte MISMATCH
/// between the two is a genuine, unresolved ambiguity and still throws. The same rule applies to two
/// same-named files both found only under <c>releases/</c> (across two tags, or two spots in one
/// tag): the first sorted match wins when identical, an actual content mismatch still throws.</para>
/// </remarks>
public static class CorpusFileLocator
{
    public const string DatFileName = "Imperial Conquest 2.dat";

    /// <summary>The three plain save folders, in the order the game/evidence pipeline moves a file
    /// through them: freshly captured, then cited by a report, then folded into a numbered batch. Does
    /// NOT include <c>releases/&lt;tag&gt;/</c> — see this type's own remarks and
    /// <see cref="SearchedLocations"/> for the full, unified search order.</summary>
    public static readonly IReadOnlyList<string> SaveDirs =
        new[] { "saves", "saves-processed", Path.Combine("saves-processed", "processed") };

    /// <summary>Every location <see cref="TryResolve"/> searches for <paramref name="fileName"/>, in
    /// order, whether or not each one currently exists — the one list both this type and
    /// <c>FixtureResolver</c> draw from (#213), and reused here to build "not found" diagnostics. Each
    /// of <see cref="SaveDirs"/> first, then every <c>releases/&lt;tag&gt;/</c> subtree: tag
    /// directories sorted, and every matching file within a tag directory sorted too (a release cache
    /// is the one location where the same name can genuinely occur more than once — screenshots and
    /// notes get restructured more often than saves do — so a stable, sorted "first" keeps a re-run
    /// deterministic).</summary>
    public static IEnumerable<string> SearchedLocations(AssetSettings settings, string fileName)
    {
        if (fileName == DatFileName)
        {
            yield return settings.DatPath;
            yield break;
        }

        foreach (var dir in SaveDirs)
            yield return Path.Combine(settings.DirectoryPath, dir, fileName);

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
            IEnumerable<string> hits;
            try
            {
                hits = Directory.EnumerateFiles(tagDir, fileName, SearchOption.AllDirectories)
                    .OrderBy(p => p, StringComparer.Ordinal);
            }
            catch (IOException)
            {
                continue;
            }
            foreach (var hit in hits) yield return hit;
        }
    }

    /// <summary>Every <c>.sav</c> file name in the configured corpus, plus the DAT (if present), each
    /// mapped to the folder(s) it was found in — relative to the asset directory, using forward
    /// slashes; <c>""</c> for the DAT; <c>releases/&lt;tag&gt;</c> for a release cache subtree. More
    /// than one folder for a name means an ambiguous, duplicated file name: a corpus fixture keyed by
    /// name alone cannot cope with that. Per this type's duplicate rule (#213 hazard), a
    /// <c>releases/</c> copy that is byte-identical to one already found elsewhere for the same name
    /// is the same file, not a second location, and is not added.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> DiscoverFileNames(AssetSettings settings)
    {
        var byName = new Dictionary<string, List<(string Folder, string Path)>>(StringComparer.Ordinal);

        void Add(string name, string folder, string path)
        {
            if (!byName.TryGetValue(name, out var entries))
                byName[name] = entries = new List<(string, string)>();
            entries.Add((folder, path));
        }

        foreach (var dir in SaveDirs)
        {
            var full = Path.Combine(settings.DirectoryPath, dir);
            if (!Directory.Exists(full)) continue;
            foreach (var file in Directory.GetFiles(full, "*.sav"))
                Add(Path.GetFileName(file), dir.Replace('\\', '/'), file);
        }
        if (File.Exists(settings.DatPath)) Add(DatFileName, "", settings.DatPath);

        var releasesDir = Path.Combine(settings.DirectoryPath, "releases");
        if (Directory.Exists(releasesDir))
        {
            IEnumerable<string> tagDirs;
            try
            {
                tagDirs = Directory.GetDirectories(releasesDir).OrderBy(d => d, StringComparer.Ordinal);
            }
            catch (IOException)
            {
                tagDirs = Enumerable.Empty<string>();
            }

            foreach (var tagDir in tagDirs)
            {
                var tagLabel = "releases/" + Path.GetFileName(tagDir);
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(tagDir, "*.sav", SearchOption.AllDirectories);
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    if (byName.TryGetValue(name, out var existing) &&
                        existing.Any(e => FilesAreByteIdentical(e.Path, file)))
                        continue; // Same file as one already found — not a new location (#213 hazard).
                    Add(name, tagLabel, file);
                }
            }
        }

        return byName.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.Select(e => e.Folder).ToList());
    }

    /// <summary>Resolves one file name to its on-disk path across <see cref="SearchedLocations"/> (or,
    /// for the DAT's own fixed name, <see cref="AssetSettings.DatPath"/>), or <c>null</c> when it is
    /// present in none of them. See this type's own remarks for the duplicate rule.</summary>
    /// <exception cref="InvalidOperationException">The name is present in more than one plain save
    /// folder, or present in a plain save folder and under <c>releases/</c> with different
    /// content.</exception>
    public static string? TryResolve(AssetSettings settings, string fileName)
    {
        if (fileName == DatFileName)
            return File.Exists(settings.DatPath) ? settings.DatPath : null;

        var all = SearchedLocations(settings, fileName).ToList();
        var plainHits = all.Take(SaveDirs.Count).Where(File.Exists).ToList();
        var releaseHits = all.Skip(SaveDirs.Count).Where(File.Exists).ToList();

        if (plainHits.Count > 1)
            throw new InvalidOperationException(
                $"'{fileName}' was found in more than one save folder: '{plainHits[0]}' and '{plainHits[1]}'.");

        var primary = plainHits.Count == 1 ? plainHits[0] : releaseHits.FirstOrDefault();
        if (primary is null) return null;

        // #213 hazard: every OTHER hit anywhere — another release copy (T64 rework round 1, N2: this
        // used to compare only the first release hit, so two mismatched release-only copies slipped
        // through here while DiscoverFileNames already caught them) or, when the primary came from a
        // plain folder, every release copy — must be byte-identical to it, or this is a genuine,
        // unresolved ambiguity. Same rule DiscoverFileNames applies; the two now agree.
        foreach (var other in releaseHits.Where(r => r != primary))
        {
            if (!FilesAreByteIdentical(primary, other))
                throw new InvalidOperationException(
                    $"'{fileName}' was found in more than one location with different contents: " +
                    $"'{primary}' and '{other}'.");
        }
        return primary;
    }

    private static bool FilesAreByteIdentical(string a, string b) =>
        File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
}
