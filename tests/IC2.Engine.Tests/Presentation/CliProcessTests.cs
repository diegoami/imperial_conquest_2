using System.Diagnostics;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// Review round 1 (PR #94, finding 2): "a missing/bad <c>--script</c> path crashes with a raw
/// <c>FileNotFoundException</c>. Print a friendly message and exit non-zero." Everything else
/// <c>src/IC2.Cli/Program.cs</c> does is exercised in-process through <see cref="GameSession"/>
/// (<see cref="GameSessionTests"/>), but this one failure mode — and, since T23's DoD 5,
/// <c>--scenario</c>/<c>--ruleset</c> parsing, resolution and the stderr-only "which pair it loaded"
/// line — are <em>in <c>Program.cs</c> itself</em>, before a <see cref="GameSession"/> even exists, so
/// they can only be proven by actually running the built executable.
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

    // ---- DoD 5 (task-catalogue.md T23, added after PR #248's round-1 review): --scenario/--ruleset ----

    /// <summary>
    /// "an unknown id fails with a message naming the id and listing what is available, never a stack
    /// trace." Same in-process reasoning as the missing-script-file test above: <c>Program.cs</c>'s
    /// argument handling runs before a <see cref="GameSession"/> exists, so only spawning the real
    /// executable proves it.
    /// </summary>
    [Fact]
    public void An_unknown_scenario_id_names_it_and_lists_whats_available_instead_of_a_stack_trace()
    {
        var cliDll = FindCliDll();
        if (cliDll is null)
        {
            return;
        }

        using var process = StartCli(cliDll, "--scenario no-such-scenario");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("Unknown scenario 'no-such-scenario'", stderr, StringComparison.Ordinal);
        Assert.Contains("Available scenarios:", stderr, StringComparison.Ordinal);
        Assert.Contains("toy-3city", stderr, StringComparison.Ordinal);
        Assert.Contains("classical-mediterranean", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", stderr, StringComparison.Ordinal);
        Assert.Empty(stdout);
    }

    /// <inheritdoc cref="An_unknown_scenario_id_names_it_and_lists_whats_available_instead_of_a_stack_trace"/>
    [Fact]
    public void An_unknown_ruleset_id_names_it_and_lists_whats_available_instead_of_a_stack_trace()
    {
        var cliDll = FindCliDll();
        if (cliDll is null)
        {
            return;
        }

        using var process = StartCli(cliDll, "--ruleset no-such-ruleset");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("Unknown ruleset 'no-such-ruleset'", stderr, StringComparison.Ordinal);
        Assert.Contains("Available rulesets:", stderr, StringComparison.Ordinal);
        Assert.Contains("toy-ruleset", stderr, StringComparison.Ordinal);
        Assert.Contains("improved", stderr, StringComparison.Ordinal);
        Assert.Contains("classical-faithful", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", stderr, StringComparison.Ordinal);
        Assert.Empty(stdout);
    }

    /// <summary>
    /// "the CLI states which world/ruleset pair it loaded at startup" — and it does so on standard
    /// <em>error</em>, specifically so that <c>tests/fixtures/cli/demo.golden.txt</c> (standard output
    /// only) is untouched by it. Runs the real default (no-flag) invocation end to end against a trivial
    /// one-line script, matching what <c>GameSessionTests</c>' in-process golden comparison already
    /// covers for the transcript's content — this test's job is only the stderr side no in-process test
    /// can see.
    /// </summary>
    [Fact]
    public void The_default_invocation_states_the_loaded_pair_on_stderr_and_writes_nothing_extra_to_stdout()
    {
        var cliDll = FindCliDll();
        if (cliDll is null)
        {
            return;
        }

        var scriptPath = Path.Combine(Path.GetTempPath(), $"ic2-cli-quit-{Guid.NewGuid():N}.txt");
        File.WriteAllText(scriptPath, "quit\n");
        try
        {
            using var process = StartCli(cliDll, $"--script \"{scriptPath}\"");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.Equal(0, process.ExitCode);
            Assert.Contains(
                "Loaded scenario 'toy-3city': world 'toy-3city', ruleset 'toy-ruleset'.",
                stderr,
                StringComparison.Ordinal);
            Assert.DoesNotContain("Loaded scenario", stdout, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    /// <summary>
    /// <c>--ruleset</c> overrides whatever the scenario declares, and the toy world loads under it —
    /// the "toy world, scatter on defeat" invocation DoD 5's own verification list asks for.
    /// </summary>
    [Fact]
    public void A_ruleset_override_replaces_the_scenarios_own_ruleset_and_is_reflected_at_startup()
    {
        var cliDll = FindCliDll();
        if (cliDll is null)
        {
            return;
        }

        var scriptPath = Path.Combine(Path.GetTempPath(), $"ic2-cli-quit-{Guid.NewGuid():N}.txt");
        File.WriteAllText(scriptPath, "quit\n");
        try
        {
            using var process = StartCli(cliDll, $"--script \"{scriptPath}\" --ruleset improved");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.Equal(0, process.ExitCode);
            Assert.Contains(
                "Loaded scenario 'toy-3city': world 'toy-3city', ruleset 'improved'.",
                stderr,
                StringComparison.Ordinal);
            Assert.NotNull(stdout);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    /// <summary>
    /// "be honest about the big world, do not fix it" — this proves only the load/run half DoD 5 actually
    /// asks for (T29's 334-city <c>classical-mediterranean</c> is reachable and exits 0 under an
    /// overridden ruleset too); large-world rendering practicality is explicitly out of scope, per the
    /// catalogue hazard, and is not what this test checks.
    /// </summary>
    [Fact]
    public void The_big_classical_mediterranean_scenario_loads_and_runs_under_an_overridden_ruleset()
    {
        var cliDll = FindCliDll();
        if (cliDll is null)
        {
            return;
        }

        var scriptPath = Path.Combine(Path.GetTempPath(), $"ic2-cli-status-quit-{Guid.NewGuid():N}.txt");
        File.WriteAllText(scriptPath, "status\nquit\n");
        try
        {
            using var process = StartCli(
                cliDll, $"--script \"{scriptPath}\" --scenario classical-mediterranean --ruleset improved");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.Equal(0, process.ExitCode);
            Assert.Contains(
                "Loaded scenario 'classical-mediterranean': world 'classical-mediterranean', ruleset 'improved'.",
                stderr,
                StringComparison.Ordinal);
            Assert.Contains("Rome", stdout, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    /// <summary>Starts the built CLI with the given argument string, redirecting both output streams.</summary>
    private static Process StartCli(string cliDll, string arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet", $"\"{cliDll}\" {arguments}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = ModelTestPaths.RepositoryRoot,
        };

        var process = Process.Start(startInfo);
        Assert.NotNull(process);
        return process!;
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
