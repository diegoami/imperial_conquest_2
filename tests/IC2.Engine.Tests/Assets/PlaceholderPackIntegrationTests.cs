using System.Security.Cryptography;
using IC2.Engine.Assets;
using Xunit;

namespace IC2.Engine.Tests.Assets;

/// <summary>
/// Integration tests for the pre-generated placeholder asset pack.
/// Verifies that the placeholder pack in assets/packs/placeholder/ is valid and complete.
/// </summary>
public class PlaceholderPackIntegrationTests
{
    private readonly string _placeholderPackDir;

    public PlaceholderPackIntegrationTests()
    {
        // Locate the placeholder pack relative to the test project
        var testDir = Path.GetDirectoryName(typeof(PlaceholderPackIntegrationTests).Assembly.Location)!;
        var repoRoot = FindRepositoryRoot(testDir);
        _placeholderPackDir = Path.Combine(repoRoot, "assets", "packs", "placeholder");
    }

    [Fact]
    public void PlaceholderPack_Exists()
    {
        Assert.True(Directory.Exists(_placeholderPackDir), $"Placeholder pack directory not found at {_placeholderPackDir}");
        Assert.True(File.Exists(Path.Combine(_placeholderPackDir, "manifest.json")), "Manifest file not found");
    }

    [Fact]
    public void AllRequiredAssetKeys_ResolveToPack()
    {
        // Arrange
        var manifestPath = Path.Combine(_placeholderPackDir, "manifest.json");
        var pack = AssetLoader.LoadManifest(manifestPath);

        // Act
        var missingKeys = new List<string>();
        foreach (var key in AssetKeys.AllKeys)
        {
            if (!pack.TryResolveAsset(key, out _))
            {
                missingKeys.Add(key);
            }
        }

        // Assert
        if (missingKeys.Count > 0)
        {
            throw new Xunit.Sdk.XunitException($"Missing keys in placeholder pack: {string.Join(", ", missingKeys)}");
        }
    }

    [Fact]
    public void AllAssetFiles_ExistOnDisk()
    {
        // Arrange
        var manifestPath = Path.Combine(_placeholderPackDir, "manifest.json");
        var pack = AssetLoader.LoadManifest(manifestPath);

        // Act
        var missingFiles = new List<string>();
        foreach (var key in AssetKeys.AllKeys)
        {
            if (pack.TryResolveAsset(key, out var relativePath))
            {
                var fullPath = Path.Combine(_placeholderPackDir, relativePath);
                if (!File.Exists(fullPath))
                {
                    missingFiles.Add(key);
                }
            }
        }

        // Assert
        if (missingFiles.Count > 0)
        {
            throw new Xunit.Sdk.XunitException($"Asset files not found for keys: {string.Join(", ", missingFiles)}");
        }
    }

    [Fact]
    public void ArmyTierIcons_ArePixelDifferent()
    {
        // Arrange
        var manifestPath = Path.Combine(_placeholderPackDir, "manifest.json");
        var pack = AssetLoader.LoadManifest(manifestPath);

        var tier1Path = Path.Combine(_placeholderPackDir, pack.ResolveAsset(AssetKeys.ArmyTier1Icon));
        var tier2Path = Path.Combine(_placeholderPackDir, pack.ResolveAsset(AssetKeys.ArmyTier2Icon));
        var tier3Path = Path.Combine(_placeholderPackDir, pack.ResolveAsset(AssetKeys.ArmyTier3Icon));

        // Act
        var hash1 = ComputeSHA256(tier1Path);
        var hash2 = ComputeSHA256(tier2Path);
        var hash3 = ComputeSHA256(tier3Path);

        // Assert - Each tier should have different content
        Assert.NotEqual(hash1, hash2);
        Assert.NotEqual(hash2, hash3);
        Assert.NotEqual(hash1, hash3);
    }

    [Fact]
    public void FleetTierIcons_ArePixelDifferent()
    {
        // Arrange
        var manifestPath = Path.Combine(_placeholderPackDir, "manifest.json");
        var pack = AssetLoader.LoadManifest(manifestPath);

        var tier1Path = Path.Combine(_placeholderPackDir, pack.ResolveAsset(AssetKeys.FleetTier1Icon));
        var tier2Path = Path.Combine(_placeholderPackDir, pack.ResolveAsset(AssetKeys.FleetTier2Icon));
        var tier3Path = Path.Combine(_placeholderPackDir, pack.ResolveAsset(AssetKeys.FleetTier3Icon));

        // Act
        var hash1 = ComputeSHA256(tier1Path);
        var hash2 = ComputeSHA256(tier2Path);
        var hash3 = ComputeSHA256(tier3Path);

        // Assert - Each tier should have different content
        Assert.NotEqual(hash1, hash2);
        Assert.NotEqual(hash2, hash3);
        Assert.NotEqual(hash1, hash3);
    }

    [Fact]
    public void CityTierIcons_ArePixelDifferent()
    {
        // Arrange
        var manifestPath = Path.Combine(_placeholderPackDir, "manifest.json");
        var pack = AssetLoader.LoadManifest(manifestPath);

        var tier1Path = Path.Combine(_placeholderPackDir, pack.ResolveAsset(AssetKeys.CityTier1Icon));
        var tier2Path = Path.Combine(_placeholderPackDir, pack.ResolveAsset(AssetKeys.CityTier2Icon));
        var tier3Path = Path.Combine(_placeholderPackDir, pack.ResolveAsset(AssetKeys.CityTier3Icon));
        var capitalPath = Path.Combine(_placeholderPackDir, pack.ResolveAsset(AssetKeys.CityCapitalIcon));

        // Act
        var hash1 = ComputeSHA256(tier1Path);
        var hash2 = ComputeSHA256(tier2Path);
        var hash3 = ComputeSHA256(tier3Path);
        var hashCapital = ComputeSHA256(capitalPath);

        // Assert - Each should have different content
        Assert.NotEqual(hash1, hash2);
        Assert.NotEqual(hash2, hash3);
        Assert.NotEqual(hash1, hash3);
        Assert.NotEqual(hashCapital, hash1);
        Assert.NotEqual(hashCapital, hash2);
        Assert.NotEqual(hashCapital, hash3);
    }

    [Fact]
    public void ManifestJSON_IsValid()
    {
        // Arrange
        var manifestPath = Path.Combine(_placeholderPackDir, "manifest.json");

        // Act
        var pack = AssetLoader.LoadManifest(manifestPath);

        // Assert
        Assert.NotNull(pack);
        Assert.Equal(1, pack.SchemaVersion);
        Assert.NotEmpty(pack.Name);
        Assert.NotEmpty(pack.Description);
        Assert.NotEmpty(pack.Assets);
    }

    [Fact]
    public void AudioFiles_AreSilentWAVFormat()
    {
        // Arrange
        var manifestPath = Path.Combine(_placeholderPackDir, "manifest.json");
        var pack = AssetLoader.LoadManifest(manifestPath);

        var sfxKeys = new[] { AssetKeys.SfxCityCaptured, AssetKeys.SfxBattle, AssetKeys.SfxUnitMove };

        // Act & Assert
        foreach (var key in sfxKeys)
        {
            var relativePath = pack.ResolveAsset(key);
            var fullPath = Path.Combine(_placeholderPackDir, relativePath);

            Assert.True(File.Exists(fullPath), $"Audio file not found: {key}");

            // Verify it's a valid WAV file
            var header = new byte[4];
            using (var stream = File.OpenRead(fullPath))
            {
                var bytesRead = stream.Read(header, 0, header.Length);
                Assert.Equal(4, bytesRead);
            }

            // WAV files start with "RIFF"
            var riffHeader = System.Text.Encoding.ASCII.GetString(header);
            Assert.Equal("RIFF", riffHeader);
        }
    }

    private static string ComputeSHA256(string filePath)
    {
        using (var sha256 = SHA256.Create())
        using (var stream = File.OpenRead(filePath))
        {
            var hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }
    }

    private static string FindRepositoryRoot(string startPath)
    {
        var current = startPath;
        while (current != null)
        {
            if (File.Exists(Path.Combine(current, "IC2.sln")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current);
        }

        throw new InvalidOperationException("Could not find repository root (IC2.sln)");
    }
}
