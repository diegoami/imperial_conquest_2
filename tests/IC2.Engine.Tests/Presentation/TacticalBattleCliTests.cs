using System.Diagnostics;
using IC2.Engine.Presentation;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// <c>docs/tasks/T125.md</c> Done-when 6: <c>IC2.Cli --human-battles tactical --script
/// tests/fixtures/cli/tactical-battle.txt</c> reproduces <c>tactical-battle.golden.txt</c> byte for byte: a human
/// attack, a placement, a shot, a melee, <c>battle general on</c>, the <em>Battle ended</em> lines and the news.
/// </summary>
/// <remarks>
/// The golden is a human army (18,500 troops, light infantry and archers) beaten by 6,000 heavy infantry: the toy
/// ruleset's matrix (T122, <c>combat.detailedResolver</c>) makes light infantry attacking heavy infantry the worst
/// matchup there is, so the transcript shows a loss, the loser's army deleted and its treasury taken. The existing
/// goldens (<c>demo</c>, <c>seat-rome</c>, <c>success</c>, <c>success-fleet</c>) are checked by their own tests, and the
/// task's diff leaves them untouched.
/// </remarks>
public sealed class TacticalBattleCliTests
{
    private static readonly string ScriptPath =
        Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "cli", "tactical-battle.txt");

    private static readonly string GoldenPath =
        Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "cli", "tactical-battle.golden.txt");

    [Fact]
    public void The_tactical_battle_script_run_in_process_matches_the_committed_golden()
    {
        var toy = TacticalSessionTestbed.Toy;
        var session = new GameSession(toy.World, toy.Ruleset, toy.Scenario, seedOverride: null, humanSeatNationId: null)
        {
            HumanBattles = HumanBattleMode.Tactical,
        };

        var transcript = new System.Text.StringBuilder();
        foreach (var line in File.ReadAllLines(ScriptPath))
        {
            var output = session.Submit(line);
            foreach (var text in output.Lines)
            {
                transcript.Append(text).Append('\n');
            }

            if (output.ShouldExit)
            {
                break;
            }
        }

        Assert.Equal(File.ReadAllText(GoldenPath), transcript.ToString());
    }

    [Fact]
    public void The_cli_process_with_human_battles_tactical_reproduces_the_golden_byte_for_byte()
    {
        var cliDll = FindCliDll();
        Assert.NotNull(cliDll);

        using var process = StartCli(cliDll!, $"--human-battles tactical --script \"{ScriptPath}\"");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(0, process.ExitCode);
        Assert.Equal(File.ReadAllText(GoldenPath), stdout);
        Assert.Contains("Loaded scenario 'toy-3city'", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void The_golden_shows_the_attack_the_placement_a_shot_a_melee_the_general_the_ending_and_the_news()
    {
        var golden = File.ReadAllText(GoldenPath);

        Assert.Contains("battle.attack-army accepted: the battle opens on the board.", golden, StringComparison.Ordinal);
        Assert.Contains("Unit 1 placed at (1,2).", golden, StringComparison.Ordinal);
        Assert.Contains("Unit 1 shoots unit 20:", golden, StringComparison.Ordinal);
        Assert.Contains("Unit 0 fights unit 20:", golden, StringComparison.Ordinal);
        Assert.Contains("Computer general is on for the attacker.", golden, StringComparison.Ordinal);
        Assert.Contains("Battle ended.", golden, StringComparison.Ordinal);
        Assert.Contains("Southern League destroys army of Northern League.", golden, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_flag_or_with_instant_the_attack_resolves_at_once_and_no_battle_opens()
    {
        var cliDll = FindCliDll();
        Assert.NotNull(cliDll);
        var script = Path.Combine(Path.GetTempPath(), $"ic2-instant-{Guid.NewGuid():N}.txt");
        File.WriteAllText(script, "move north-army-1 4 3\nattack-army north-army-1 south-army-1\nbattle show\nquit\n");
        try
        {
            var outputs = new List<string>();
            foreach (var arguments in new[] { $"--script \"{script}\"", $"--human-battles instant --script \"{script}\"" })
            {
                using var process = StartCli(cliDll!, arguments);
                outputs.Add(process.StandardOutput.ReadToEnd());
                process.WaitForExit();
                Assert.Equal(0, process.ExitCode);
            }

            Assert.Equal(outputs[0], outputs[1]);
            Assert.DoesNotContain("the battle opens on the board", outputs[0], StringComparison.Ordinal);
            Assert.Contains("battle.attack-army accepted.", outputs[0], StringComparison.Ordinal);
            Assert.Contains("There is no battle to fight.", outputs[0], StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Theory]
    [InlineData("--human-battles")]
    [InlineData("--human-battles sideways")]
    [InlineData("--human-battles 7")]
    public void A_missing_or_unknown_human_battles_value_is_refused_with_a_message(string arguments)
    {
        var cliDll = FindCliDll();
        Assert.NotNull(cliDll);

        using var process = StartCli(cliDll!, arguments);
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("--human-battles requires instant or tactical.", stderr, StringComparison.Ordinal);
        Assert.Empty(stdout);
    }

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
