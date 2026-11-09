namespace IC2.Engine.Assets;

/// <summary>A key resolved to a file, and which pack supplied it.</summary>
public sealed record ResolvedAsset(string FullPath, string PackId);

/// <summary>
/// T292c: per-key fallback. Asks the local <c>original</c> cache first and the selected base pack (normally
/// <c>authored</c>) for everything the cache lacks, so a partial or lost installation never leaves a hole.
/// </summary>
public sealed class LayeredAssetPack
{
    private readonly AssetPack _base;
    private readonly string _baseDirectory;
    private readonly string _baseId;
    private readonly AssetPack? _overlay;
    private readonly string? _overlayDirectory;
    private readonly string _overlayId;

    public LayeredAssetPack(AssetPack basePack, string baseDirectory, string baseId,
        AssetPack? overlay = null, string? overlayDirectory = null, string overlayId = OriginalAssetPackBuilder.PackId)
    {
        _base = basePack; _baseDirectory = baseDirectory; _baseId = baseId;
        _overlay = overlay; _overlayDirectory = overlayDirectory; _overlayId = overlayId;
    }

    public bool TryResolve(string key, out ResolvedAsset asset)
    {
        if (_overlay is not null && _overlayDirectory is not null
            && TryFile(_overlay, _overlayDirectory, key, out var original))
        {
            asset = new ResolvedAsset(original, _overlayId);
            return true;
        }
        if (TryFile(_base, _baseDirectory, key, out var fallback))
        {
            asset = new ResolvedAsset(fallback, _baseId);
            return true;
        }
        asset = default!;
        return false;
    }

    /// <summary>Number of the given keys the overlay supplies right now.</summary>
    public int OverlayCount(IEnumerable<string> keys) =>
        keys.Count(k => _overlay is not null && _overlayDirectory is not null && TryFile(_overlay, _overlayDirectory, k, out _));

    /// <summary>A manifest path must stay inside its pack directory and name an existing file.</summary>
    private static bool TryFile(AssetPack pack, string directory, string key, out string full)
    {
        full = "";
        if (!pack.TryResolveAsset(key, out var relative)) return false;
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(directory, relative));
        if (!candidate.StartsWith(root, StringComparison.Ordinal) || !File.Exists(candidate)) return false;
        full = candidate;
        return true;
    }
}

public enum OriginalCacheState { NotSet, SourceMissing, NoCache, Empty, Partial, Ready }

/// <summary>What the Settings screen shows about the original-assets cache. Holds counts and words only.</summary>
public sealed record OriginalCacheStatus(OriginalCacheState State, int Keys, int Wanted, string Message)
{
    /// <summary>True when the cache should be layered over the base pack.</summary>
    public bool InUse => State is OriginalCacheState.Partial or OriginalCacheState.Ready;

    /// <summary>
    /// Works out the state from the chosen source folder (null when none) and the cache pack directory.
    /// A source that has gone away reverts to the authored art and says so, even if a cache still exists.
    /// </summary>
    public static OriginalCacheStatus Read(string? sourceDirectory, string cacheDirectory)
    {
        var wanted = AssetKeys.AllKeys.Count();
        if (string.IsNullOrWhiteSpace(sourceDirectory))
            return new(OriginalCacheState.NotSet, 0, wanted, "No original installation chosen. Using the authored art and sounds.");
        if (!Directory.Exists(sourceDirectory))
            return new(OriginalCacheState.SourceMissing, 0, wanted,
                "The chosen installation folder is gone. Using the authored art and sounds until you choose it again.");

        var manifest = Path.Combine(cacheDirectory, "manifest.json");
        if (!File.Exists(manifest))
            return new(OriginalCacheState.NoCache, 0, wanted, "Not built yet. Use Rebuild cache.");

        AssetPack pack;
        try { pack = AssetLoader.LoadManifest(manifest); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            return new(OriginalCacheState.NoCache, 0, wanted, "The cache is unreadable. Use Rebuild cache.");
        }

        var layered = new LayeredAssetPack(pack, cacheDirectory, OriginalAssetPackBuilder.PackId, pack, cacheDirectory);
        var keys = layered.OverlayCount(AssetKeys.AllKeys);
        if (keys == 0)
            return new(OriginalCacheState.Empty, 0, wanted, "Nothing usable was found in that folder. Using the authored art and sounds.");
        return keys >= wanted
            ? new(OriginalCacheState.Ready, keys, wanted, $"Using original assets for all {keys} items.")
            : new(OriginalCacheState.Partial, keys, wanted, $"Using original assets for {keys} of {wanted} items; the rest use the authored art and sounds.");
    }
}
