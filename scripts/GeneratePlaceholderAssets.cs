using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Generates a deterministic placeholder asset pack.
/// Run via: dotnet run --project scripts/GeneratePlaceholderAssets.csproj -- <output-path>
/// </summary>
public static class PlaceholderAssetGenerator
{
    public static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: GeneratePlaceholderAssets <output-directory>");
            Console.WriteLine("Example: GeneratePlaceholderAssets assets/packs/placeholder");
            Environment.Exit(1);
        }

        var outputPath = args[0];
        var success = GenerateAssets(outputPath);
        Environment.Exit(success ? 0 : 1);
    }

    public static bool GenerateAssets(string outputPath)
    {
        try
        {
            Console.WriteLine($"Generating placeholder asset pack to: {outputPath}");

            // Create directories
            Directory.CreateDirectory(outputPath);
            var dirs = new[] { "units", "army", "fleet", "city", "terrain", "sfx" };
            foreach (var dir in dirs)
            {
                Directory.CreateDirectory(Path.Combine(outputPath, dir));
            }

            // Define deterministic colors for each asset type
            var colors = new Dictionary<string, (byte R, byte G, byte B)>
            {
                // Unit icons
                { "unit.light_infantry", (200, 150, 100) },
                { "unit.heavy_infantry", (100, 100, 150) },
                { "unit.archers", (150, 200, 100) },
                { "unit.light_cavalry", (200, 200, 100) },
                { "unit.heavy_cavalry", (100, 150, 200) },

                // Army markers
                { "army.tier1", (180, 100, 100) },
                { "army.tier2", (200, 150, 100) },
                { "army.tier3", (220, 200, 100) },

                // Fleet markers
                { "fleet.tier1", (100, 180, 200) },
                { "fleet.tier2", (100, 200, 220) },
                { "fleet.tier3", (100, 220, 255) },

                // City markers
                { "city.tier1", (150, 150, 100) },
                { "city.tier2", (180, 180, 100) },
                { "city.tier3", (220, 220, 100) },
                { "city.capital", (255, 215, 0) },

                // Terrain
                { "terrain.plain", (144, 238, 144) },
                { "terrain.desert", (210, 180, 140) },
                { "terrain.forest", (34, 139, 34) },
                { "terrain.mountain", (128, 128, 128) },
                { "terrain.river", (64, 164, 223) },
                { "terrain.sea_coastal", (100, 149, 237) },
                { "terrain.sea_deep", (30, 100, 180) },
            };

            Console.WriteLine("Generating PNG files...");

            // Generate PNG files
            foreach (var (key, (r, g, b)) in colors)
            {
                var parts = key.Split('.');
                var filename = parts[^1] + ".png";
                var filepath = Path.Combine(outputPath, parts[0], filename);

                GeneratePNG(filepath, r, g, b);
                Console.WriteLine($"  Created {key}.icon");
            }

            // Generate audio stubs
            Console.WriteLine("Generating audio files...");
            foreach (var sfx in new[] { "city_captured", "battle", "unit_move" })
            {
                var filepath = Path.Combine(outputPath, "sfx", sfx + ".wav");
                GenerateSilentWAV(filepath);
                Console.WriteLine($"  Created sfx.{sfx}");
            }

            // Generate manifest
            Console.WriteLine("Generating manifest.json...");
            var manifest = BuildManifest();
            var manifestPath = Path.Combine(outputPath, "manifest.json");

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };

            var json = JsonSerializer.Serialize(manifest, options);
            File.WriteAllText(manifestPath, json);

            Console.WriteLine($"✓ Successfully generated placeholder asset pack at: {outputPath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ Error generating placeholder assets: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return false;
        }
    }

    private static void GeneratePNG(string filepath, byte r, byte g, byte b)
    {
        // PNG header
        var pngSig = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

        // IHDR chunk: 32x32 RGB image
        var ihdrData = new byte[] { 0, 0, 0, 32, 0, 0, 0, 32, 8, 2, 0, 0, 0 };
        var ihdr = BuildChunk("IHDR", ihdrData);

        // IDAT chunk: compressed image data
        var pixelData = GeneratePixelData(32, 32, r, g, b);
        var compressedData = CompressData(pixelData);
        var idat = BuildChunk("IDAT", compressedData);

        // IEND chunk (empty)
        var iend = BuildChunk("IEND", Array.Empty<byte>());

        // Write PNG
        using (var stream = File.Create(filepath))
        {
            stream.Write(pngSig);
            stream.Write(ihdr);
            stream.Write(idat);
            stream.Write(iend);
        }
    }

    private static byte[] GeneratePixelData(int width, int height, byte r, byte g, byte b)
    {
        var data = new byte[height * (width * 3 + 1)];
        var offset = 0;

        for (int y = 0; y < height; y++)
        {
            data[offset] = 0;  // Filter type: None
            offset += 1;

            for (int x = 0; x < width; x++)
            {
                data[offset] = r;
                offset += 1;
                data[offset] = g;
                offset += 1;
                data[offset] = b;
                offset += 1;
            }
        }

        return data;
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

    private static byte[] BuildChunk(string type, byte[] data)
    {
        var result = new MemoryStream();
        var writer = new BinaryWriter(result);

        // Write chunk length
        writer.Write((uint)data.Length);

        // Write chunk type
        writer.Write(System.Text.Encoding.ASCII.GetBytes(type));

        // Write chunk data
        writer.Write(data);

        // Calculate and write CRC
        var crc = CalculateCRC(type, data);
        writer.Write(crc);

        return result.ToArray();
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

    private static void GenerateSilentWAV(string filepath)
    {
        const int sampleRate = 44100;
        const int duration = 1;  // 1 second
        const int numSamples = sampleRate * duration;

        using (var stream = File.Create(filepath))
        using (var writer = new BinaryWriter(stream))
        {
            // RIFF header
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            var fileSizePos = stream.Position;
            writer.Write((uint)(36 + numSamples * 2));  // File size - 8

            // WAVE header
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            // fmt chunk
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write((uint)16);  // Subchunk1 size
            writer.Write((ushort)1);  // Audio format: PCM
            writer.Write((ushort)1);  // Channels: mono
            writer.Write((uint)sampleRate);  // Sample rate
            writer.Write((uint)(sampleRate * 2));  // Byte rate
            writer.Write((ushort)2);  // Block align
            writer.Write((ushort)16);  // Bits per sample

            // data chunk
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write((uint)(numSamples * 2));  // Subchunk2 size

            // Silent audio (all zeros)
            for (int i = 0; i < numSamples; i++)
            {
                writer.Write((short)0);
            }
        }
    }

    private static ManifestJson BuildManifest()
    {
        var assets = new Dictionary<string, string>();

        // Unit icons
        foreach (var unit in new[] { "light_infantry", "heavy_infantry", "archers", "light_cavalry", "heavy_cavalry" })
        {
            assets[$"unit.{unit}.icon"] = $"units/{unit}.png";
        }

        // Army markers
        for (int tier = 1; tier <= 3; tier++)
        {
            assets[$"army.tier{tier}.icon"] = $"army/tier{tier}.png";
        }

        // Fleet markers
        for (int tier = 1; tier <= 3; tier++)
        {
            assets[$"fleet.tier{tier}.icon"] = $"fleet/tier{tier}.png";
        }

        // City markers
        for (int tier = 1; tier <= 3; tier++)
        {
            assets[$"city.tier{tier}.icon"] = $"city/tier{tier}.png";
        }

        assets["city.capital.icon"] = "city/capital.png";

        // Terrain tiles
        foreach (var terrain in new[] { "plain", "desert", "forest", "mountain", "river" })
        {
            assets[$"terrain.{terrain}.tile"] = $"terrain/{terrain}.png";
        }

        foreach (var sea in new[] { "coastal", "deep" })
        {
            assets[$"terrain.sea_{sea}.tile"] = $"terrain/sea_{sea}.png";
        }

        // Sound effects
        foreach (var sfx in new[] { "city_captured", "battle", "unit_move" })
        {
            assets[$"sfx.{sfx}"] = $"sfx/{sfx}.wav";
        }

        return new ManifestJson
        {
            SchemaVersion = 1,
            Name = "Placeholder Asset Pack",
            Description = "Generated placeholder assets — flat colors, silent audio stubs, good enough for development and testing.",
            Assets = assets
        };
    }

    [JsonSerializable]
    public class ManifestJson
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public Dictionary<string, string> Assets { get; set; } = new();
    }
}
