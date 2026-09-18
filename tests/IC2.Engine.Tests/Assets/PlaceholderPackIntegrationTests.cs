using System.Runtime.InteropServices;
using System.Security.Cryptography;
using IC2.Engine.Assets;
using IC2.Engine.Tests.Fixtures;
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
        _placeholderPackDir = Path.Combine(FixturePaths.RepositoryRoot, "assets", "packs", "placeholder");
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

    [Fact]
    public void ImageFiles_HaveValidBMPStructure()
    {
        // Arrange
        var manifestPath = Path.Combine(_placeholderPackDir, "manifest.json");
        var pack = AssetLoader.LoadManifest(manifestPath);

        // Every image key in the pack (all 22 icon/tile keys), not just a sample - a bug in the
        // generator's shared BMP-writing code affects every file it writes, not a chosen few.
        var imageAssets = AssetKeys.AllKeys
            .Where(key => !key.StartsWith("sfx.", StringComparison.Ordinal))
            .ToArray();

        // Act & Assert
        foreach (var key in imageAssets)
        {
            var relativePath = pack.ResolveAsset(key);
            var fullPath = Path.Combine(_placeholderPackDir, relativePath);

            Assert.True(File.Exists(fullPath), $"Image file not found: {key}");
            ValidateBMPStructure(fullPath, key);
        }
    }

    [Fact]
    public void Generator_ProducesDeterministicOutput()
    {
        // DoD 3: Verify that running the generator produces byte-identical output
        // This test validates that the placeholder pack generation is deterministic
        // and can be reliably regenerated from the PowerShell script.
        // Skipped on non-Windows platforms or when PowerShell is not available.

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"ic2-placeholder-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Act - Run generator in temp directory. -ExecutionPolicy Bypass is required here:
            // this machine's default policy for a plain, non-interactive `powershell` process is
            // Restricted, which refuses to load any .ps1 file at all (independent of the
            // generator's own logic) and is exactly what silently turned this DoD line into
            // "always exit code 1" before this fix.
            var generatorScript = Path.Combine(FixturePaths.RepositoryRoot, "scripts", "generate-placeholder-assets.ps1");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{generatorScript}\" -OutputPath \"{tempDir}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            string stdout = string.Empty, stderr = string.Empty;
            using (var process = System.Diagnostics.Process.Start(psi))
            {
                stdout = process?.StandardOutput.ReadToEnd() ?? string.Empty;
                stderr = process?.StandardError.ReadToEnd() ?? string.Empty;
                var timeout = process?.WaitForExit(30000) ?? false;
                Assert.True(timeout, "Generator script timed out");
                Assert.True(0 == (process?.ExitCode ?? 1), $"Generator exited non-zero. stdout: {stdout}\nstderr: {stderr}");
            }

            // Assert - Compare every generated file (images, audio, manifest) with the committed
            // versions. A file that differs, is missing, or is extra on either side fails the test.
            var committedFiles = Directory.EnumerateFiles(_placeholderPackDir, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(_placeholderPackDir, p))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
            var generatedFiles = Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(tempDir, p))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(committedFiles, generatedFiles);

            foreach (var relativePath in committedFiles)
            {
                var committedBytes = File.ReadAllBytes(Path.Combine(_placeholderPackDir, relativePath));
                var generatedBytes = File.ReadAllBytes(Path.Combine(tempDir, relativePath));

                Assert.Equal(committedBytes.Length, generatedBytes.Length);
                Assert.Equal(committedBytes, generatedBytes);
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// Parses a BMP file's own headers and checks them against each other and against the file
    /// on disk, rather than trusting any single field: the "BM" signature; the file-header's
    /// declared total size against the file's actual length; the 40-byte BITMAPINFOHEADER's own
    /// size field, declared width/height (both positive, matching the 32x32 the generator
    /// produces), bit depth (24) and compression (0, BI_RGB - uncompressed); the DIB header's
    /// declared pixel-array size against the bytes actually remaining in the file; and that the
    /// pixel array's length matches width/height/bit-depth with 4-byte row padding accounted for.
    /// This is the BMP-format equivalent of the PNG chunk-CRC check this test used to run before
    /// the switch to BMP (see PR #96): every field the format defines is independently
    /// recomputed and compared, not read once and assumed correct.
    /// </summary>
    private static void ValidateBMPStructure(string bmpPath, string assetKey)
    {
        var bytes = File.ReadAllBytes(bmpPath);
        var actualLength = bytes.Length;

        Assert.True(actualLength >= 54, $"{assetKey}: file too short to hold a BMP file header + DIB header ({actualLength} bytes)");

        // BITMAPFILEHEADER (14 bytes)
        Assert.True(bytes[0] == (byte)'B' && bytes[1] == (byte)'M', $"{assetKey}: missing 'BM' signature");

        var declaredFileSize = BitConverter.ToUInt32(bytes, 2);
        Assert.True(declaredFileSize == (uint)actualLength,
            $"{assetKey}: file header declares size {declaredFileSize}, actual file is {actualLength} bytes");

        var pixelDataOffset = BitConverter.ToUInt32(bytes, 10);
        Assert.Equal(54u, pixelDataOffset);

        // BITMAPINFOHEADER (40 bytes, starting at offset 14)
        var dibHeaderSize = BitConverter.ToUInt32(bytes, 14);
        Assert.Equal(40u, dibHeaderSize);

        var width = BitConverter.ToInt32(bytes, 18);
        var height = BitConverter.ToInt32(bytes, 22);
        Assert.True(width > 0, $"{assetKey}: DIB header declares non-positive width {width}");
        Assert.True(height > 0, $"{assetKey}: DIB header declares non-positive height {height}");
        Assert.Equal(32, width);
        Assert.Equal(32, height);

        var bitCount = BitConverter.ToUInt16(bytes, 28);
        Assert.Equal((ushort)24, bitCount);

        var compression = BitConverter.ToUInt32(bytes, 30);
        Assert.Equal(0u, compression); // BI_RGB - uncompressed, so no codec bug can hide here either

        var declaredImageSize = BitConverter.ToUInt32(bytes, 34);

        // Recompute the expected pixel array size independently from width/height/bit depth,
        // rather than trusting the header's own declaredImageSize field.
        var bytesPerPixel = bitCount / 8;
        var rowSizeUnpadded = width * bytesPerPixel;
        var rowPadding = (4 - (rowSizeUnpadded % 4)) % 4;
        var rowSize = rowSizeUnpadded + rowPadding;
        var expectedPixelDataSize = rowSize * height;

        Assert.True(declaredImageSize == (uint)expectedPixelDataSize,
            $"{assetKey}: DIB header declares image size {declaredImageSize}, expected {expectedPixelDataSize} for a {width}x{height} 24bpp bottom-up bitmap");

        var actualPixelBytes = actualLength - (int)pixelDataOffset;
        Assert.True(actualPixelBytes == expectedPixelDataSize,
            $"{assetKey}: pixel array is {actualPixelBytes} bytes on disk, expected {expectedPixelDataSize}");
    }
}
