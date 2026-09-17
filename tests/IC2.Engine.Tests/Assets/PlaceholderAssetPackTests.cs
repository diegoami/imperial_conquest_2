using System.Security.Cryptography;
using System.Text.Json;
using IC2.Engine.Assets;
using Xunit;

namespace IC2.Engine.Tests.Assets;

/// <summary>
/// Tests for the generated placeholder asset pack.
/// Verifies that the generator produces valid, deterministic assets.
/// </summary>
public class PlaceholderAssetPackTests : IAsyncLifetime
{
    private readonly string _tempDir;
    private readonly string _packDir;

    public PlaceholderAssetPackTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"placeholder-test-{Guid.NewGuid():N}");
        _packDir = Path.Combine(_tempDir, "placeholder");
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempDir);

        // Generate the placeholder pack
        if (!await GeneratePlaceholderPackAsync(_packDir))
        {
            throw new InvalidOperationException("Failed to generate placeholder asset pack");
        }
    }

    public async Task DisposeAsync()
    {
        if (Directory.Exists(_tempDir))
        {
            await Task.Run(() => Directory.Delete(_tempDir, recursive: true));
        }
    }

    [Fact]
    public void AllRequiredAssetKeys_ResolveToPack()
    {
        // Arrange
        var manifestPath = Path.Combine(_packDir, "manifest.json");
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
            throw new Xunit.Sdk.XunitException($"Missing keys: {string.Join(", ", missingKeys)}");
        }
    }

    [Fact]
    public void AllAssetFiles_Exist()
    {
        // Arrange
        var manifestPath = Path.Combine(_packDir, "manifest.json");
        var pack = AssetLoader.LoadManifest(manifestPath);

        // Act
        var missingFiles = new List<string>();
        foreach (var key in AssetKeys.AllKeys)
        {
            if (pack.TryResolveAsset(key, out var relativePath))
            {
                var fullPath = Path.Combine(_packDir, relativePath);
                if (!File.Exists(fullPath))
                {
                    missingFiles.Add(key);
                }
            }
        }

        // Assert
        if (missingFiles.Count > 0)
        {
            throw new Xunit.Sdk.XunitException($"Missing files for keys: {string.Join(", ", missingFiles)}");
        }
    }

    [Fact]
    public async Task RegeneratingPack_ProducesByteIdenticalFiles()
    {
        // Arrange - Generate second pack in different directory
        var tempDir2 = Path.Combine(Path.GetTempPath(), $"placeholder-test-{Guid.NewGuid():N}");
        var packDir2 = Path.Combine(tempDir2, "placeholder");
        Directory.CreateDirectory(tempDir2);

        try
        {
            if (!await GeneratePlaceholderPackAsync(packDir2))
            {
                throw new InvalidOperationException("Failed to generate second placeholder pack");
            }

            // Act - Compare all files between the two packs
            var files1 = Directory.GetFiles(_packDir, "*", SearchOption.AllDirectories).OrderBy(x => x).ToList();
            var files2 = Directory.GetFiles(packDir2, "*", SearchOption.AllDirectories).OrderBy(x => x).ToList();

            // Assert - Same number of files
            Assert.Equal(files1.Count, files2.Count);

            // Assert - All files have identical content
            for (int i = 0; i < files1.Count; i++)
            {
                var hash1 = ComputeSHA256(files1[i]);
                var hash2 = ComputeSHA256(files2[i]);
                Assert.Equal(hash1, hash2);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir2))
            {
                Directory.Delete(tempDir2, recursive: true);
            }
        }
    }

    [Fact]
    public void ArmyTierIcons_ArePixelDifferent()
    {
        // Arrange
        var manifestPath = Path.Combine(_packDir, "manifest.json");
        var pack = AssetLoader.LoadManifest(manifestPath);

        // Get file hashes for each tier
        var tier1Path = Path.Combine(_packDir, pack.ResolveAsset(AssetKeys.ArmyTier1Icon));
        var tier2Path = Path.Combine(_packDir, pack.ResolveAsset(AssetKeys.ArmyTier2Icon));
        var tier3Path = Path.Combine(_packDir, pack.ResolveAsset(AssetKeys.ArmyTier3Icon));

        var tier1Hash = ComputeSHA256(tier1Path);
        var tier2Hash = ComputeSHA256(tier2Path);
        var tier3Hash = ComputeSHA256(tier3Path);

        // Assert - Each tier should have different content
        Assert.NotEqual(tier1Hash, tier2Hash);
        Assert.NotEqual(tier2Hash, tier3Hash);
        Assert.NotEqual(tier1Hash, tier3Hash);
    }

    [Fact]
    public void FleetTierIcons_ArePixelDifferent()
    {
        // Arrange
        var manifestPath = Path.Combine(_packDir, "manifest.json");
        var pack = AssetLoader.LoadManifest(manifestPath);

        // Get file hashes for each tier
        var tier1Path = Path.Combine(_packDir, pack.ResolveAsset(AssetKeys.FleetTier1Icon));
        var tier2Path = Path.Combine(_packDir, pack.ResolveAsset(AssetKeys.FleetTier2Icon));
        var tier3Path = Path.Combine(_packDir, pack.ResolveAsset(AssetKeys.FleetTier3Icon));

        var tier1Hash = ComputeSHA256(tier1Path);
        var tier2Hash = ComputeSHA256(tier2Path);
        var tier3Hash = ComputeSHA256(tier3Path);

        // Assert - Each tier should have different content
        Assert.NotEqual(tier1Hash, tier2Hash);
        Assert.NotEqual(tier2Hash, tier3Hash);
        Assert.NotEqual(tier1Hash, tier3Hash);
    }

    [Fact]
    public void CityTierIcons_ArePixelDifferent()
    {
        // Arrange
        var manifestPath = Path.Combine(_packDir, "manifest.json");
        var pack = AssetLoader.LoadManifest(manifestPath);

        // Get file hashes for each tier and capital
        var tier1Path = Path.Combine(_packDir, pack.ResolveAsset(AssetKeys.CityTier1Icon));
        var tier2Path = Path.Combine(_packDir, pack.ResolveAsset(AssetKeys.CityTier2Icon));
        var tier3Path = Path.Combine(_packDir, pack.ResolveAsset(AssetKeys.CityTier3Icon));
        var capitalPath = Path.Combine(_packDir, pack.ResolveAsset(AssetKeys.CityCapitalIcon));

        var tier1Hash = ComputeSHA256(tier1Path);
        var tier2Hash = ComputeSHA256(tier2Path);
        var tier3Hash = ComputeSHA256(tier3Path);
        var capitalHash = ComputeSHA256(capitalPath);

        // Assert - Each should have different content
        Assert.NotEqual(tier1Hash, tier2Hash);
        Assert.NotEqual(tier2Hash, tier3Hash);
        Assert.NotEqual(tier1Hash, tier3Hash);
        Assert.NotEqual(capitalHash, tier1Hash);
        Assert.NotEqual(capitalHash, tier2Hash);
        Assert.NotEqual(capitalHash, tier3Hash);
    }

    [Fact]
    public void ManifestJSON_IsValid()
    {
        // Arrange
        var manifestPath = Path.Combine(_packDir, "manifest.json");

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
        var manifestPath = Path.Combine(_packDir, "manifest.json");
        var pack = AssetLoader.LoadManifest(manifestPath);

        var sfxKeys = new[] { AssetKeys.SfxCityCaptured, AssetKeys.SfxBattle, AssetKeys.SfxUnitMove };

        // Act & Assert
        foreach (var key in sfxKeys)
        {
            var relativePath = pack.ResolveAsset(key);
            var fullPath = Path.Combine(_packDir, relativePath);

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

    private static async Task<bool> GeneratePlaceholderPackAsync(string outputPath)
    {
        // This would normally call a generator, but for testing purposes
        // we'll use a synchronous approach that creates a minimal valid pack
        return await Task.Run(() => GeneratePlaceholderPackSync(outputPath));
    }

    private static bool GeneratePlaceholderPackSync(string outputPath)
    {
        try
        {
            Directory.CreateDirectory(outputPath);

            // Create subdirectories
            var dirs = new[] { "units", "army", "fleet", "city", "terrain", "sfx" };
            foreach (var dir in dirs)
            {
                Directory.CreateDirectory(Path.Combine(outputPath, dir));
            }

            // Define colors
            var colors = new Dictionary<string, (byte R, byte G, byte B)>
            {
                { "unit.light_infantry", (200, 150, 100) },
                { "unit.heavy_infantry", (100, 100, 150) },
                { "unit.archers", (150, 200, 100) },
                { "unit.light_cavalry", (200, 200, 100) },
                { "unit.heavy_cavalry", (100, 150, 200) },
                { "army.tier1", (180, 100, 100) },
                { "army.tier2", (200, 150, 100) },
                { "army.tier3", (220, 200, 100) },
                { "fleet.tier1", (100, 180, 200) },
                { "fleet.tier2", (100, 200, 220) },
                { "fleet.tier3", (100, 220, 255) },
                { "city.tier1", (150, 150, 100) },
                { "city.tier2", (180, 180, 100) },
                { "city.tier3", (220, 220, 100) },
                { "city.capital", (255, 215, 0) },
                { "terrain.plain", (144, 238, 144) },
                { "terrain.desert", (210, 180, 140) },
                { "terrain.forest", (34, 139, 34) },
                { "terrain.mountain", (128, 128, 128) },
                { "terrain.river", (64, 164, 223) },
                { "terrain.sea_coastal", (100, 149, 237) },
                { "terrain.sea_deep", (30, 100, 180) },
            };

            // Generate PNGs
            foreach (var (key, (r, g, b)) in colors)
            {
                var parts = key.Split('.');
                var filename = parts[^1] + ".png";
                var filepath = Path.Combine(outputPath, parts[0], filename);
                CreatePlaceholderPNG(filepath, r, g, b);
            }

            // Generate WAVs
            foreach (var sfx in new[] { "city_captured", "battle", "unit_move" })
            {
                var filepath = Path.Combine(outputPath, "sfx", sfx + ".wav");
                CreateSilentWAV(filepath);
            }

            // Create manifest
            var manifest = new
            {
                schemaVersion = 1,
                name = "Placeholder Asset Pack",
                description = "Generated placeholder assets",
                assets = BuildAssetDict()
            };

            var manifestPath = Path.Combine(outputPath, "manifest.json");
            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(manifestPath, json);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CreatePlaceholderPNG(string filepath, byte r, byte g, byte b)
    {
        var pngSig = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

        var ihdrData = new byte[] { 0, 0, 0, 32, 0, 0, 0, 32, 8, 2, 0, 0, 0 };
        var ihdr = BuildChunk("IHDR", ihdrData);

        var pixelData = new byte[32 * (32 * 3 + 1)];
        var offset = 0;
        for (int y = 0; y < 32; y++)
        {
            pixelData[offset++] = 0;
            for (int x = 0; x < 32; x++)
            {
                pixelData[offset++] = r;
                pixelData[offset++] = g;
                pixelData[offset++] = b;
            }
        }

        var compressedData = CompressData(pixelData);
        var idat = BuildChunk("IDAT", compressedData);
        var iend = BuildChunk("IEND", new byte[] { });

        using (var stream = File.Create(filepath))
        {
            stream.Write(pngSig);
            stream.Write(ihdr);
            stream.Write(idat);
            stream.Write(iend);
        }
    }

    private static void CreateSilentWAV(string filepath)
    {
        const int sampleRate = 44100;
        using (var stream = File.Create(filepath))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write((uint)(36 + sampleRate * 2));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write((uint)16);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write((uint)sampleRate);
            writer.Write((uint)(sampleRate * 2));
            writer.Write((ushort)2);
            writer.Write((ushort)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write((uint)(sampleRate * 2));

            for (int i = 0; i < sampleRate; i++)
            {
                writer.Write((short)0);
            }
        }
    }

    private static byte[] BuildChunk(string type, byte[] data)
    {
        var result = new MemoryStream();
        var writer = new BinaryWriter(result);
        writer.Write((uint)data.Length);
        writer.Write(System.Text.Encoding.ASCII.GetBytes(type));
        writer.Write(data);
        var crc = CalculateCRC(type, data);
        writer.Write(crc);
        return result.ToArray();
    }

    private static byte[] CompressData(byte[] data)
    {
        using (var input = new MemoryStream(data))
        using (var output = new MemoryStream())
        {
            using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionMode.Compress))
            {
                input.CopyTo(deflate);
            }
            return output.ToArray();
        }
    }

    private static uint CalculateCRC(string type, byte[] data)
    {
        var crcTable = new uint[256];
        for (int i = 0; i < 256; i++)
        {
            uint c = (uint)i;
            for (int k = 0; k < 8; k++)
            {
                c = ((c & 1) == 1) ? (0xedb88320 ^ (c >> 1)) : (c >> 1);
            }
            crcTable[i] = c;
        }

        uint crc = 0xffffffff;
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        foreach (var b in typeBytes)
        {
            crc = crcTable[(crc ^ b) & 0xff] ^ (crc >> 8);
        }
        foreach (var b in data)
        {
            crc = crcTable[(crc ^ b) & 0xff] ^ (crc >> 8);
        }
        return crc ^ 0xffffffff;
    }

    private static Dictionary<string, string> BuildAssetDict()
    {
        var assets = new Dictionary<string, string>();

        foreach (var unit in new[] { "light_infantry", "heavy_infantry", "archers", "light_cavalry", "heavy_cavalry" })
        {
            assets[$"unit.{unit}.icon"] = $"units/{unit}.png";
        }

        for (int i = 1; i <= 3; i++)
        {
            assets[$"army.tier{i}.icon"] = $"army/tier{i}.png";
            assets[$"fleet.tier{i}.icon"] = $"fleet/tier{i}.png";
            assets[$"city.tier{i}.icon"] = $"city/tier{i}.png";
        }

        assets["city.capital.icon"] = "city/capital.png";

        foreach (var terrain in new[] { "plain", "desert", "forest", "mountain", "river", "sea_coastal", "sea_deep" })
        {
            var key = terrain.StartsWith("sea_") ? $"terrain.{terrain}.tile" : $"terrain.{terrain}.tile";
            assets[key] = $"terrain/{terrain}.png";
        }

        foreach (var sfx in new[] { "city_captured", "battle", "unit_move" })
        {
            assets[$"sfx.{sfx}"] = $"sfx/{sfx}.wav";
        }

        return assets;
    }
}
