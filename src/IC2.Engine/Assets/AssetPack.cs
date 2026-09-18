using System.Collections.Frozen;
using System.Text.Json.Serialization;

namespace IC2.Engine.Assets;

/// <summary>
/// A manifest mapping stable string asset keys to file paths within a pack directory.
/// Loaded from a JSON file and cached for fast runtime lookups.
/// </summary>
public sealed record AssetPack(
    int SchemaVersion,
    string Name,
    string Description,
    FrozenDictionary<string, string> Assets)
{
    /// <summary>
    /// Resolves an asset key to its file path, or throws if the key is missing.
    /// </summary>
    /// <param name="key">The asset key to resolve (e.g., "unit.light_infantry.icon").</param>
    /// <returns>The relative file path (e.g., "units/light_infantry.png").</returns>
    /// <exception cref="AssetNotFoundException">Thrown if the key is not present in the pack.</exception>
    public string ResolveAsset(string key)
    {
        if (!Assets.TryGetValue(key, out var path))
        {
            throw new AssetNotFoundException(key);
        }

        return path;
    }

    /// <summary>
    /// Attempts to resolve an asset key, returning false if the key is not found.
    /// </summary>
    public bool TryResolveAsset(string key, out string path) => Assets.TryGetValue(key, out path!);
}

/// <summary>
/// Thrown when an asset key cannot be resolved to a file in the current pack.
/// </summary>
public sealed class AssetNotFoundException : Exception
{
    /// <summary>The asset key that was not found.</summary>
    public string AssetKey { get; }

    public AssetNotFoundException(string assetKey) : base($"Asset key not found: {assetKey}")
    {
        AssetKey = assetKey;
    }
}

/// <summary>
/// Intermediate JSON structure for deserializing asset pack manifests.
/// </summary>
internal sealed class AssetPackJson
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public Dictionary<string, string> Assets { get; set; } = new();

    public AssetPack ToAssetPack()
    {
        return new AssetPack(
            SchemaVersion,
            Name,
            Description,
            Assets.ToFrozenDictionary());
    }
}
