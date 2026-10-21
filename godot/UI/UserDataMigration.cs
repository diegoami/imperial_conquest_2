namespace IC2.Slice.UI;

/// <summary>
/// The user's decision of 2026-10-21: the app is named "Open Imperial Conquest" (it was "Imperial Conquest 2 Map
/// Viewer"). Godot derives <c>user://</c> from <c>application/config/name</c>, so the rename moves the player's
/// saves and settings to a new folder; on the first launch after it, this copies them over from the old one.
/// Godot-free so the tests can link it.
/// </summary>
/// <remarks>
/// Copy, never move or delete: the old folder is left exactly as it was, so a downgrade still finds its data.
/// Nothing already in the new folder is overwritten. Diagnostics logs are not copied (they describe the old runs).
/// A marker file records that the migration ran, so a save the player later deletes is not copied back.
/// </remarks>
public static class UserDataMigration
{
    /// <summary>The app's former <c>application/config/name</c>, the old <c>user://</c> folder's name.</summary>
    public const string FormerAppName = "Imperial Conquest 2 Map Viewer";

    /// <summary>Written into the new folder once the migration has run.</summary>
    public const string MarkerFileName = ".migrated-from-map-viewer";

    /// <summary>What one run did.</summary>
    /// <param name="Ran">False when the marker was already there or there was nothing to migrate from.</param>
    /// <param name="CopiedFiles">The files copied, relative to the data folder.</param>
    public sealed record Outcome(bool Ran, IReadOnlyList<string> CopiedFiles);

    /// <summary>
    /// Copies <c>saves/</c> (recursively) and <c>settings.json</c> from <paramref name="oldDirectory"/> into
    /// <paramref name="newDirectory"/> when the marker is absent, skipping any file that already exists there, then
    /// writes the marker. Never throws: a failure leaves the game starting with what the new folder has.
    /// </summary>
    public static Outcome Run(string oldDirectory, string newDirectory)
    {
        var copied = new List<string>();
        try
        {
            var marker = Path.Combine(newDirectory, MarkerFileName);
            if (File.Exists(marker))
            {
                return new Outcome(false, copied);
            }

            if (Directory.Exists(oldDirectory)
                && !string.Equals(Path.GetFullPath(oldDirectory), Path.GetFullPath(newDirectory), StringComparison.Ordinal))
            {
                CopyMissing(Path.Combine(oldDirectory, "settings.json"), Path.Combine(newDirectory, "settings.json"), "settings.json", copied);
                var oldSaves = Path.Combine(oldDirectory, "saves");
                if (Directory.Exists(oldSaves))
                {
                    foreach (var file in Directory.EnumerateFiles(oldSaves, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                    {
                        var relative = Path.GetRelativePath(oldDirectory, file);
                        CopyMissing(file, Path.Combine(newDirectory, relative), relative, copied);
                    }
                }
            }

            Directory.CreateDirectory(newDirectory);
            File.WriteAllText(marker, $"Copied {copied.Count} file(s) from {oldDirectory}.\n");
            return new Outcome(true, copied);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new Outcome(false, copied);
        }
    }

    /// <summary>The former <c>user://</c> folder: the new one's sibling under Godot's <c>app_userdata</c>.</summary>
    public static string FormerDirectoryBeside(string newDirectory) =>
        Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(newDirectory)))!, FormerAppName);

    private static void CopyMissing(string source, string destination, string relative, List<string> copied)
    {
        if (!File.Exists(source) || File.Exists(destination))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: false);
        copied.Add(relative.Replace('\\', '/'));
    }
}
