using System.Text.Json;
using System.Text.Json.Serialization;

namespace IC2.Engine.Assets;

/// <summary>
/// Loads asset pack manifests from JSON files and provides access to resolved asset paths.
/// </summary>
public static class AssetLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Loads an asset pack manifest from the given JSON file path.
    /// </summary>
    /// <param name="manifestPath">Path to the manifest JSON file.</param>
    /// <returns>The loaded asset pack.</returns>
    /// <exception cref="FileNotFoundException">If the manifest file does not exist.</exception>
    /// <exception cref="InvalidOperationException">If the manifest is malformed.</exception>
    public static AssetPack LoadManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Asset pack manifest not found: {manifestPath}", manifestPath);
        }

        var json = File.ReadAllText(manifestPath);
        var packJson = JsonSerializer.Deserialize<AssetPackJson>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize asset pack manifest: {manifestPath}");

        return packJson.ToAssetPack();
    }

    /// <summary>
    /// Loads an asset pack manifest asynchronously from the given JSON file path.
    /// </summary>
    public static async Task<AssetPack> LoadManifestAsync(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Asset pack manifest not found: {manifestPath}", manifestPath);
        }

        var json = await File.ReadAllTextAsync(manifestPath);
        var packJson = JsonSerializer.Deserialize<AssetPackJson>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize asset pack manifest: {manifestPath}");

        return packJson.ToAssetPack();
    }

    /// <summary>
    /// Validates that every required asset key resolves to an existing file.
    /// </summary>
    /// <param name="pack">The asset pack to validate.</param>
    /// <param name="packDirectory">The base directory containing the asset files.</param>
    /// <returns>A list of missing assets (empty if all are present).</returns>
    public static List<string> ValidateAssets(AssetPack pack, string packDirectory)
    {
        var missing = new List<string>();

        foreach (var key in AssetKeys.AllKeys)
        {
            if (!pack.TryResolveAsset(key, out var relativePath))
            {
                missing.Add(key);
                continue;
            }

            var fullPath = Path.Combine(packDirectory, relativePath);
            if (!File.Exists(fullPath))
            {
                missing.Add(key);
            }
        }

        return missing;
    }
}
