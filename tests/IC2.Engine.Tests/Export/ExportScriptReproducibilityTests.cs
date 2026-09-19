using System.Diagnostics;
using System.Security.Cryptography;
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
/// Uses <c>Xunit.SkippableFact</c> (the package this task's widened Owns grant added to
/// <c>IC2.Engine.Tests.csproj</c>, mirroring <c>IC2.Data.Tests.csproj</c>'s own reference for the
/// identical reason): when the original files are not configured, <c>Skip.IfNot</c> reports a
/// genuine xunit <c>Skipped</c> result, naming <see cref="OriginalFilesAvailability.SkipReason"/>,
/// rather than a <c>Passed</c> that merely prints a message.
/// </remarks>
public class ExportScriptReproducibilityTests
{
    [SkippableFact]
    public void Rerunning_the_export_reproduces_the_committed_files_byte_for_byte()
    {
        Skip.IfNot(OriginalFilesAvailability.IsConfigured, OriginalFilesAvailability.SkipReason);

        var beforeWorld = Sha256(ExportedDataPaths.WorldFile);
        var beforeRuleset = Sha256(ExportedDataPaths.RulesetFile);
        var beforeScenario = Sha256(ExportedDataPaths.ScenarioFile);

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

    private static (int ExitCode, string Stdout, string Stderr) RunExportScript()
    {
        var psi = new ProcessStartInfo("dotnet", $"run \"{ExportedDataPaths.ExportScript}\" \"{ExportedDataPaths.AssetsIniPath}\"")
        {
            WorkingDirectory = ExportedDataPaths.RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet run.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
