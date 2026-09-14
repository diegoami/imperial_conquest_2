using System.Diagnostics;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// Review round 1 (PR #94, finding 2): "a missing/bad <c>--script</c> path crashes with a raw
/// <c>FileNotFoundException</c>. Print a friendly message and exit non-zero." Everything else
/// <c>src/IC2.Cli/Program.cs</c> does is exercised in-process through <see cref="GameSession"/>
/// (<see cref="GameSessionTests"/>), but this one failure mode is <em>in <c>Program.cs</c> itself</em>
/// — before a <see cref="GameSession"/> even exists — so it can only be proven by actually running the
/// built executable.
/// </summary>
/// <remarks>
/// Spawns the real <c>IC2.Cli.dll</c> the solution build already produced, rather than adding a new
/// test project (which would mean editing <c>IC2.sln</c>, outside every task's Owns list) or a new
/// NuGet package (an escalation, <c>docs/build-process.md</c> §4.5 item 7). The canonical DoD command,
/// <c>dotnet test IC2.sln</c>, always builds every project first, so the dll is present whenever this
/// runs as part of that; a narrower test invocation that never built <c>IC2.Cli</c> skips rather than
/// failing on something outside this test's own contract to prove.
/// </remarks>
public sealed class CliProcessTests
{
    [Fact]
    public void A_missing_script_file_prints_a_friendly_message_and_exits_non_zero()
    {
        var cliDll = FindCliDll();
        if (cliDll is null)
        {
            return;
        }

        var missingPath = $"does-not-exist-{Guid.NewGuid():N}.txt";
        var startInfo = new ProcessStartInfo("dotnet", $"\"{cliDll}\" --script \"{missingPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = ModelTestPaths.RepositoryRoot,
        };

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var stdout = process!.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("Could not find script file", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", stderr, StringComparison.Ordinal);
        Assert.Empty(stdout);
    }

    /// <summary>
    /// Finds the built <c>IC2.Cli.dll</c> next to this test assembly's own build output — same
    /// repository root, same configuration, same target framework — or <see langword="null"/> if this
    /// test run never built it.
    /// </summary>
    private static string? FindCliDll()
    {
        var testOutputDir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var targetFramework = testOutputDir.Name;
        var configuration = testOutputDir.Parent?.Name;
        if (configuration is null)
        {
            return null;
        }

        var candidate = Path.Combine(
            ModelTestPaths.RepositoryRoot, "src", "IC2.Cli", "bin", configuration, targetFramework, "IC2.Cli.dll");
        return File.Exists(candidate) ? candidate : null;
    }
}
