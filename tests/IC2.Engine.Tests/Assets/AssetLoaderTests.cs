using System.Text.Json;
using IC2.Engine.Assets;
using Xunit;

namespace IC2.Engine.Tests.Assets;

/// <summary>
/// Tests for the asset pack loader and manifest validation.
/// </summary>
public class AssetLoaderTests : IAsyncLifetime
{
    private readonly string _tempDir;

    public AssetLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"asset-test-{Guid.NewGuid():N}");
    }

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempDir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public void LoadManifest_WithValidFile_ReturnsAssetPack()
    {
        // Arrange
        var manifest = new
        {
            schemaVersion = 1,
            name = "Test Pack",
            description = "A test asset pack",
            assets = new Dictionary<string, string>
            {
                { "unit.light_infantry.icon", "units/light_infantry.png" },
                { "army.tier1.icon", "army/tier1.png" }
            }
        };

        var manifestPath = Path.Combine(_tempDir, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));

        // Act
        var pack = AssetLoader.LoadManifest(manifestPath);

        // Assert
        Assert.NotNull(pack);
        Assert.Equal("Test Pack", pack.Name);
        Assert.True(pack.TryResolveAsset("unit.light_infantry.icon", out var path));
        Assert.Equal("units/light_infantry.png", path);
    }

    [Fact]
    public void LoadManifest_WithMissingFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var manifestPath = Path.Combine(_tempDir, "nonexistent.json");

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => AssetLoader.LoadManifest(manifestPath));
    }

    [Fact]
    public void ResolveAsset_WithValidKey_ReturnsPath()
    {
        // Arrange
        var manifest = new
        {
            schemaVersion = 1,
            name = "Test Pack",
            description = "Test",
            assets = new Dictionary<string, string>
            {
                { "unit.archers.icon", "units/archers.png" }
            }
        };

        var manifestPath = Path.Combine(_tempDir, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
        var pack = AssetLoader.LoadManifest(manifestPath);

        // Act
        var path = pack.ResolveAsset("unit.archers.icon");

        // Assert
        Assert.Equal("units/archers.png", path);
    }

    [Fact]
    public void ResolveAsset_WithMissingKey_ThrowsAssetNotFoundException()
    {
        // Arrange
        var manifest = new
        {
            schemaVersion = 1,
            name = "Test Pack",
            description = "Test",
            assets = new Dictionary<string, string>()
        };

        var manifestPath = Path.Combine(_tempDir, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
        var pack = AssetLoader.LoadManifest(manifestPath);

        // Act & Assert
        var ex = Assert.Throws<AssetNotFoundException>(() => pack.ResolveAsset("missing.key"));
        Assert.Equal("missing.key", ex.AssetKey);
    }

    [Fact]
    public async Task LoadManifestAsync_WithValidFile_ReturnsAssetPackAsync()
    {
        // Arrange
        var manifest = new
        {
            schemaVersion = 1,
            name = "Async Test Pack",
            description = "A test asset pack loaded asynchronously",
            assets = new Dictionary<string, string>
            {
                { "fleet.tier1.icon", "fleet/tier1.png" }
            }
        };

        var manifestPath = Path.Combine(_tempDir, "manifest_async.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));

        // Act
        var pack = await AssetLoader.LoadManifestAsync(manifestPath);

        // Assert
        Assert.NotNull(pack);
        Assert.Equal("Async Test Pack", pack.Name);
    }

    [Fact]
    public void ValidateAssets_WithMissingKeys_ReturnsMissingList()
    {
        // Arrange
        var manifest = new
        {
            schemaVersion = 1,
            name = "Incomplete Pack",
            description = "Missing some keys",
            assets = new Dictionary<string, string>
            {
                { "unit.light_infantry.icon", "units/light_infantry.png" }
                // Missing most other keys
            }
        };

        var manifestPath = Path.Combine(_tempDir, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
        var pack = AssetLoader.LoadManifest(manifestPath);

        // Act
        var missing = AssetLoader.ValidateAssets(pack, _tempDir);

        // Assert
        Assert.NotEmpty(missing);
        // Should include at least the city markers and other missing keys
        Assert.Contains("city.tier1.icon", missing);
        Assert.Contains("army.tier1.icon", missing);
    }

    [Fact]
    public void ValidateAssets_WithAllKeysAndFiles_ReturnsEmpty()
    {
        // Arrange
        var assetDir = Path.Combine(_tempDir, "assets");
        Directory.CreateDirectory(assetDir);

        // Create all required asset files
        foreach (var key in AssetKeys.AllKeys)
        {
            var relativePath = GetTestRelativePath(key);
            var fullPath = Path.Combine(assetDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, "placeholder");
        }

        // Create manifest with all keys
        var assets = new Dictionary<string, string>();
        foreach (var key in AssetKeys.AllKeys)
        {
            assets[key] = GetTestRelativePath(key);
        }

        var manifest = new
        {
            schemaVersion = 1,
            name = "Complete Pack",
            description = "All assets present",
            assets = assets
        };

        var manifestPath = Path.Combine(assetDir, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
        var pack = AssetLoader.LoadManifest(manifestPath);

        // Act
        var missing = AssetLoader.ValidateAssets(pack, assetDir);

        // Assert
        Assert.Empty(missing);
    }

    private static string GetTestRelativePath(string assetKey)
    {
        // Convert asset.key.to.file to asset/key/to/file.ext
        var parts = assetKey.Split('.');
        var ext = parts.Length > 0 ? parts[^1] switch
        {
            "icon" => ".png",
            "tile" => ".png",
            _ when assetKey.StartsWith("sfx.") => ".wav",
            _ => ".png"
        } : ".bin";

        return Path.Combine(parts[..^1].Concat(new[] { parts[^1] + ext }).ToArray());
    }
}
