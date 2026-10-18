using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using IC2.Engine.Assets;
using IC2.Engine.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace IC2.Engine.Tests.Assets;

public sealed class TacticalAudioAssetTests(ITestOutputHelper output)
{
    // Existing authored inputs, pinned before T183 at reviewed main a4c6c1f. T183 reuses them,
    // never synthesizes or changes authored bytes. PCM/hash checks are not human hearing proof.
    private static readonly (string Name, string Sha256)[] Sounds =
    [
        ("battle_arrows", "2aebf5e782f128a0666f69af92bb483371cc775f9a106096291f31315ac88726"),
        ("battle_javelin", "d1e1fbd9d7447b57cefc34afdf6335004ca4a1b4b33441d686c530613890e9dd"),
        ("battle_melee", "8b75fa49868ce64758b7ce0a1a30bd81c530ddb1fa560c749299dcc1443a213d"),
    ];
    private const string Description = "Generated placeholder assets - flat colors and silent audio stubs; battle_arrows, battle_javelin and battle_melee reuse existing locally synthesized authored cues byte-for-byte from assets/packs/authored/sfx/. Good enough for development and testing.";
    private static string Root => FixturePaths.RepositoryRoot;
    private static string Authored => Path.Combine(Root, "assets", "packs", "authored");
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    [Fact]
    public void Both_packs_resolve_exact_unchanged_authored_nonzero_PCM_and_placeholder_provenance()
    {
        CheckPack(Authored, placeholder: false);
        CheckPack(Path.Combine(Root, "assets", "packs", "placeholder"), placeholder: true);
    }

    [Fact]
    public async Task Scratch_regeneration_preserves_exact_reuse_provenance_and_all_output_determinism()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "ic2-t183-assets-" + Guid.NewGuid().ToString("N"));
        var first = Path.Combine(scratch, "first"); var second = Path.Combine(scratch, "second");
        var script = Path.Combine(Root, "scripts", "generate-placeholder-assets.ps1");
        try
        {
            foreach (var destination in new[] { first, second })
            {
                var result = await Generate(script, destination);
                Assert.True(result.Code == 0, result.Error);
                CheckPack(destination, placeholder: true);
            }
            static Dictionary<string, string> Files(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .ToDictionary(p => Path.GetRelativePath(directory, p), Hash, StringComparer.Ordinal);
            var baseline = Files(first);
            Assert.NotEmpty(baseline);
            Assert.Equal(baseline.OrderBy(p => p.Key, StringComparer.Ordinal), Files(second).OrderBy(p => p.Key, StringComparer.Ordinal));
            // A rerun repairs stale/silent copies instead of retaining them or re-silencing reuse.
            foreach (var (name, _) in Sounds) File.WriteAllBytes(Path.Combine(first, "sfx", name + ".wav"), new byte[44]);
            var rerun = await Generate(script, first);
            Assert.True(rerun.Code == 0, rerun.Error);
            CheckPack(first, placeholder: true);
            Assert.Equal(baseline.OrderBy(p => p.Key, StringComparer.Ordinal), Files(first).OrderBy(p => p.Key, StringComparer.Ordinal));
            // Deliberately compare scratch generations with each other, not all committed glyphs.
            // The known strategic glyph discrepancy is outside T183's asset ownership.
        }
        finally { DeleteScratch(scratch); }
    }

    [Theory]
    [InlineData("battle_arrows")]
    [InlineData("battle_javelin")]
    [InlineData("battle_melee")]
    public async Task Missing_readonly_input_fails_clearly_before_output_is_created(string missing)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "ic2-t183-missing-" + Guid.NewGuid().ToString("N"));
        var scripts = Path.Combine(scratch, "scripts");
        var source = Path.Combine(scratch, "assets", "packs", "authored", "sfx");
        var destination = Path.Combine(scratch, "output");
        try
        {
            Directory.CreateDirectory(scripts); Directory.CreateDirectory(source);
            var script = Path.Combine(scripts, "generate-placeholder-assets.ps1");
            File.Copy(Path.Combine(Root, "scripts", "generate-placeholder-assets.ps1"), script);
            foreach (var (name, _) in Sounds.Where(s => s.Name != missing))
                File.Copy(Path.Combine(Authored, "sfx", name + ".wav"), Path.Combine(source, name + ".wav"));
            var result = await Generate(script, destination);
            Assert.NotEqual(0, result.Code);
            Assert.Contains("T183 required authored tactical WAV missing", result.Error);
            Assert.Contains(missing + ".wav", result.Error);
            Assert.False(Directory.Exists(destination));
        }
        finally { DeleteScratch(scratch); }
    }

    private void CheckPack(string directory, bool placeholder)
    {
        var manifest = Path.Combine(directory, "manifest.json");
        var pack = AssetLoader.LoadManifest(manifest);
        foreach (var (name, hash) in Sounds)
        {
            Assert.True(pack.TryResolveAsset("sfx." + name, out var relative));
            Assert.Equal("sfx/" + name + ".wav", relative);
            var path = Path.Combine(directory, relative);
            output.WriteLine($"SHA256 source={Path.Combine(Authored, "sfx", name + ".wav")} {Hash(Path.Combine(Authored, "sfx", name + ".wav"))} destination={path} {Hash(path)}");
            Assert.Equal(hash, Hash(path));
            Assert.Equal(File.ReadAllBytes(Path.Combine(Authored, "sfx", name + ".wav")), File.ReadAllBytes(path));
            CheckPcm(path);
        }
        if (placeholder)
        {
            using var json = JsonDocument.Parse(File.ReadAllText(manifest));
            Assert.Equal(Description, json.RootElement.GetProperty("description").GetString());
        }
    }

    private static void CheckPcm(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.Equal(bytes.Length - 8, BitConverter.ToInt32(bytes, 4));
        byte[]? samples = null; var format = false;
        for (var offset = 12; offset + 8 <= bytes.Length;)
        {
            var size = BitConverter.ToInt32(bytes, offset + 4);
            Assert.True(size >= 0 && offset + 8 + size <= bytes.Length);
            var id = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
            if (id == "fmt ")
            {
                Assert.True(size >= 16);
                Assert.Equal(1, BitConverter.ToInt16(bytes, offset + 8));
                Assert.Equal(1, BitConverter.ToInt16(bytes, offset + 10));
                Assert.Equal(44100, BitConverter.ToInt32(bytes, offset + 12));
                Assert.Equal(16, BitConverter.ToInt16(bytes, offset + 22));
                format = true;
            }
            if (id == "data") samples = bytes.AsSpan(offset + 8, size).ToArray();
            offset += 8 + size + size % 2;
        }
        Assert.True(format);
        Assert.NotNull(samples); Assert.NotEmpty(samples);
        Assert.Equal(0, samples.Length % 2);
        Assert.Contains(samples, sample => sample != 0);
    }

    private static async Task<(int Code, string Error)> Generate(string script, string destination)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-OutputPath", destination })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, (await output) + "\n" + (await error));
    }

    private static void DeleteScratch(string path)
    {
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(path));
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
