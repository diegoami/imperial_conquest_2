using IC2.Data;

namespace IC2.Inspect;

/// <summary>
/// Locates corpus files by name only, independent of which of the three save folders currently
/// holds them. This is the point of the path-independent corpus fixture (bug #57, T34): a save
/// moved from <c>saves/</c> to <c>saves-processed/</c> — what <c>/process-evidence</c> does once a
/// research report cites it — must change no test outcome, because the fixture is keyed by file
/// name, not by a directory-relative path.
/// </summary>
public static class CorpusFileLocator
{
    public const string DatFileName = "Imperial Conquest 2.dat";

    /// <summary>The three save folders, in the order the game/evidence pipeline moves a file through
    /// them: freshly captured, then cited by a report, then folded into a numbered batch.</summary>
    public static readonly IReadOnlyList<string> SaveDirs =
        new[] { "saves", "saves-processed", Path.Combine("saves-processed", "processed") };

    /// <summary>Every <c>.sav</c> file name in the configured corpus, plus the DAT (if present), each
    /// mapped to the folder(s) — relative to the asset directory, using forward slashes, and <c>""</c>
    /// for the DAT — it was found in. More than one folder for a name means an ambiguous, duplicated
    /// file name: a corpus fixture keyed by name alone cannot cope with that.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> DiscoverFileNames(AssetSettings settings)
    {
        var byName = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void Add(string name, string folder)
        {
            if (!byName.TryGetValue(name, out var folders))
                byName[name] = folders = new List<string>();
            folders.Add(folder);
        }

        foreach (var dir in SaveDirs)
        {
            var full = Path.Combine(settings.DirectoryPath, dir);
            if (!Directory.Exists(full)) continue;
            foreach (var file in Directory.GetFiles(full, "*.sav"))
                Add(Path.GetFileName(file), dir.Replace('\\', '/'));
        }
        if (File.Exists(settings.DatPath)) Add(DatFileName, "");

        return byName.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);
    }

    /// <summary>Resolves one file name to its on-disk path across the three save folders (or, for the
    /// DAT's own fixed name, <see cref="AssetSettings.DatPath"/>), or <c>null</c> when it is present
    /// in none of them.</summary>
    /// <exception cref="InvalidOperationException">The name is present in more than one save
    /// folder.</exception>
    public static string? TryResolve(AssetSettings settings, string fileName)
    {
        if (fileName == DatFileName)
            return File.Exists(settings.DatPath) ? settings.DatPath : null;

        string? found = null;
        foreach (var dir in SaveDirs)
        {
            var candidate = Path.Combine(settings.DirectoryPath, dir, fileName);
            if (!File.Exists(candidate)) continue;
            if (found is not null)
                throw new InvalidOperationException(
                    $"'{fileName}' was found in more than one save folder: '{found}' and '{candidate}'.");
            found = candidate;
        }
        return found;
    }
}
