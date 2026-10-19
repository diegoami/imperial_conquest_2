using System.Security.Cryptography;
using System.Diagnostics;
using IC2.Engine.Tests.TestInfrastructure;
using Xunit;

namespace IC2.Engine.Tests.Export;

/// <summary>
/// Re-runs <c>scripts/export-classical-world.cs</c> against the configured original DAT and checks
/// it against <c>docs/task-catalogue.md</c> T29's Done-when lines 1, 5 and 6: the DAT-vs-NationCatalog
/// cross-check and the 15-army/2-fleet/334-city/16-nation counts are asserted by the script itself
/// (<c>export-classical-world.cs</c>'s own throwing checks, which this test proves by requiring the
/// process to exit 0); re-running produces byte-identical output to what is already committed.
/// </summary>
/// <remarks>
/// <para>
/// Uses <c>Xunit.SkippableFact</c> (the package this task's widened Owns grant added to
/// <c>IC2.Engine.Tests.csproj</c>, mirroring <c>IC2.Data.Tests.csproj</c>'s own reference for the
/// identical reason): when the original files are not configured, <c>Skip.IfNot</c> reports a
/// genuine xunit <c>Skipped</c> result, naming <see cref="OriginalFilesAvailability.SkipReason"/>,
/// rather than a <c>Passed</c> that merely prints a message.
/// </para>
/// <para>
/// T74 (bug #320): joins <see cref="DotnetRunScriptCollection"/>, the one non-parallel collection
/// every dotnet-run test now shares, and runs through the shared
/// <see cref="DotnetRunScriptRunner.Run"/> against <see cref="DotnetRunArtifactsFixture"/>'s isolated
/// build-output directory instead of a private <c>ProcessStartInfo</c> call -- see that runner's own
/// remarks for why.
/// </para>
/// </remarks>
[Collection(DotnetRunScriptCollection.Name)]
public class ExportScriptReproducibilityTests
{
    public ExportScriptReproducibilityTests(DotnetRunArtifactsFixture artifacts)
    {
        _artifacts = artifacts;
    }

    private readonly DotnetRunArtifactsFixture _artifacts;

    [SkippableFact]
    public void Rerunning_the_export_reproduces_the_committed_files_byte_for_byte()
    {
        Skip.IfNot(OriginalFilesAvailability.IsConfigured, OriginalFilesAvailability.SkipReason);

        using var restore = PreserveExportedFiles();
        var beforeWorld = CommittedSha256(ExportedDataPaths.RepositoryRoot, ExportedDataPaths.WorldFile);
        var beforeRuleset = CommittedSha256(ExportedDataPaths.RepositoryRoot, ExportedDataPaths.RulesetFile);
        var beforeScenario = CommittedSha256(ExportedDataPaths.RepositoryRoot, ExportedDataPaths.ScenarioFile);

        var (exitCode, stdout, stderr) = RunExportScript();
        Assert.True(exitCode == 0, $"export-classical-world.cs exited {exitCode}.\nstdout:\n{stdout}\nstderr:\n{stderr}");

        // DoD 1's cross-check and DoD 7's army/fleet/city/nation counts are asserted by the script
        // itself (throwing on any disagreement); a 0 exit code is that assertion having held. The
        // script's own log additionally confirms the specific counts, checked here so a script that
        // silently stopped printing them (but still exited 0) would fail this test.
        Assert.Contains("DoD 1 cross-check OK: 334 cities, 16 nations, 320x140 map, names match NationCatalog in order.", stdout, StringComparison.Ordinal);
        Assert.Contains("15 armies, 2 fleets", stdout, StringComparison.Ordinal);

        Assert.Equal(beforeWorld, Sha256(ExportedDataPaths.WorldFile));
        Assert.Equal(beforeRuleset, Sha256(ExportedDataPaths.RulesetFile));
        Assert.Equal(beforeScenario, Sha256(ExportedDataPaths.ScenarioFile));
    }

    /// <summary>
    /// Runs the export script against wherever <see cref="OriginalFilesAvailability"/> actually
    /// resolved the DAT -- <strong>not</strong> unconditionally against the repository-root
    /// <c>assets.local.ini</c>. CI configures the DAT through <c>IC2_FIXTURES_DIR</c>, which has no
    /// ini file at all (caught by this test's own first CI run: <c>IsConfigured</c> was correctly
    /// <see langword="true"/>, but the script was handed a non-existent
    /// <c>assets.local.ini</c> path regardless and crashed). This writes a throwaway ini pointing at
    /// the resolved DAT's directory and passes that instead, mirroring
    /// <c>IC2.Data.Tests.LocalAssets.TryLoadFixturesDir</c>'s identical trick for the identical
    /// reason -- the export script's own <c>AssetSettings.Load</c> only understands an ini file, and
    /// is outside this task's Owns list to change.
    /// </summary>
    /// <remarks>
    /// T74 (bug #320): the "export-classical-world" artifacts subdirectory is the same one
    /// <c>WorldTerrainExportReproducibilityTests</c> uses -- both run this exact, unmodified script
    /// (<see cref="ExportedDataPaths.ExportScript"/>), so sharing one build output between them
    /// reuses the same cold build both ways, exactly as the default, unisolated
    /// <c>%TEMP%\dotnet\runfile\</c> cache already did for both before this task (measured: sharing
    /// costs one ~35 s cold build across the two tests combined; not sharing would pay it twice).
    /// Only <c>ExportScriptToyGuardTests</c>' differently-sourced, same-named scratch copy of the
    /// script needs its own, separate subdirectory.
    /// </remarks>
    private (int ExitCode, string Stdout, string Stderr) RunExportScript()
    {
        var datPath = OriginalFilesAvailability.DatPath
                      ?? throw new InvalidOperationException("DatPath is null despite IsConfigured being true.");
        var assetsDirectory = Path.GetDirectoryName(datPath)
                               ?? throw new InvalidOperationException($"'{datPath}' has no directory component.");

        var tempIni = Path.Combine(Path.GetTempPath(), "ic2-export-repro-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(tempIni, $"[assets]{Environment.NewLine}directory = {assetsDirectory}{Environment.NewLine}");

            var result = DotnetRunScriptRunner.Run(
                ExportedDataPaths.ExportScript,
                Path.Combine(_artifacts.ArtifactsPath, "export-classical-world"),
                ExportedDataPaths.RepositoryRoot,
                tempIni);
            return (result.ExitCode, result.Stdout, result.Stderr);
        }
        finally
        {
            if (File.Exists(tempIni))
            {
                File.Delete(tempIni);
            }
        }
    }

    // Read bytes from HEAD, never from files a previous export may already have rewritten.
    internal static string CommittedSha256(string repositoryRoot, string path) =>
        Convert.ToHexString(SHA256.HashData(Git(repositoryRoot,
            "show", "HEAD:" + Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))));

    internal static IDisposable PreserveExportedFiles() => new ExportSnapshot(new[]
    {
        ExportedDataPaths.WorldFile, ExportedDataPaths.RulesetFile, ExportedDataPaths.ScenarioFile,
        Path.Combine(ExportedDataPaths.RepositoryRoot, "data", "worlds", "classical-mediterranean.terrain.b64"),
    });

    private sealed class ExportSnapshot : IDisposable
    {
        private readonly Dictionary<string, byte[]> _files;
        public ExportSnapshot(IEnumerable<string> paths) =>
            _files = paths.ToDictionary(path => path, File.ReadAllBytes);
        public void Dispose()
        {
            foreach (var (path, bytes) in _files) File.WriteAllBytes(path, bytes);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_stale_commit_is_detected_even_when_another_export_ran_first(bool previousExportRan)
    {
        var scratch = Directory.CreateTempSubdirectory("ic2-export-baseline-");
        try
        {
            Git(scratch.FullName, "init", "--quiet");
            var path = Path.Combine(scratch.FullName, "export.json");
            File.WriteAllText(path, "stale committed export");
            Git(scratch.FullName, "add", "export.json");
            Git(scratch.FullName, "-c", "user.name=IC2 test", "-c", "user.email=test@example.invalid",
                "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "test baseline");
            var committed = Sha256(path);
            if (previousExportRan) File.WriteAllText(path, "current exporter output");
            var expected = CommittedSha256(scratch.FullName, path);
            File.WriteAllText(path, "current exporter output");
            Assert.Equal(committed, expected);
            Assert.NotEqual(expected, Sha256(path));
        }
        finally
        {
            // Git object files are read-only on Windows; this directory belongs only to this test.
            foreach (var file in scratch.EnumerateFiles("*", SearchOption.AllDirectories))
                file.Attributes = FileAttributes.Normal;
            scratch.Delete(recursive: true);
        }
    }

    private static byte[] Git(string repositoryRoot, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start git.");
        var error = process.StandardError.ReadToEndAsync();
        using var bytes = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(bytes);
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} exited {process.ExitCode}: {error.GetAwaiter().GetResult()}");
        return bytes.ToArray();
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
