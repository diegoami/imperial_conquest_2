using Godot;
using IC2.Engine.Assets;
using IC2.Slice.UI;

namespace IC2.Slice.Assets;

/// <summary>
/// T292d: where the local original-assets cache lives (Godot's per-user data folder, never the repository)
/// and the three actions Settings offers on it. Holds no original bytes itself.
/// </summary>
public static class OriginalAssetCache
{
    /// <summary>Directory of the <c>original</c> pack inside the user-data folder.</summary>
    public static string CacheDirectory { get; set; } =
        Path.Combine(ProjectSettings.GlobalizePath("user://"), "original-cache", OriginalAssetPackBuilder.PackId);

    public static OriginalCacheStatus Status(UserSettings settings) =>
        OriginalCacheStatus.Read(settings.OriginalInstallPath, CacheDirectory);

    /// <summary>The cache directory when the player chose the original artwork and the cache can supply it, otherwise null.</summary>
    public static string? ActiveDirectory(UserSettings settings) =>
        settings.UseOriginalAssets && Status(settings).InUse ? CacheDirectory : null;

    /// <summary>Rebuilds the cache from the chosen folder. Never throws; failure becomes the returned message.</summary>
    public static string Rebuild(UserSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OriginalInstallPath)) return "Choose your installation folder first.";
        try
        {
            OriginalAssetPackBuilder.Build(settings.OriginalInstallPath, CacheDirectory);
            return Status(settings).Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Original assets: cache build failed ({ex.GetType().Name}).");
            return "The cache could not be built. Check that the folder can be read and that there is disk space.";
        }
    }

    public static string Clear()
    {
        try
        {
            if (Directory.Exists(CacheDirectory)) Directory.Delete(CacheDirectory, recursive: true);
            return "Cache cleared. Using the authored art and sounds.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "The cache could not be cleared. Close anything using it and try again.";
        }
    }
}
