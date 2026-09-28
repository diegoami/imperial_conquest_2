using Godot;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// <c>docs/tasks/T24.md</c> Done-when 2: "A scripted headless Godot run loads a scenario, issues one
/// order of each type through the command layer, and ends a turn, exiting 0." Run headless via
/// <c>Godot_..._console.exe --headless --path godot res://Checks/OrderCoverageCheck.tscn --quit-after 4</c>
/// (see this task's own PR body for the exact captured command and output).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Reuses T80's own proven scripts</strong> (<c>tests/fixtures/cli/success.txt</c> and
/// <c>success-fleet.txt</c>) rather than re-deriving valid army/city/fleet ids by hand — both are
/// already golden-tested (<c>tests/IC2.Engine.Tests/Presentation/SuccessScriptTests.cs</c>) to run every
/// line through <see cref="GameSession"/>'s own command layer without error, at the exact
/// scenario/seat/seed each needs. Together they issue 25 of the engine's 29 command-layer verbs
/// successfully (every verb except <c>attack-fleet</c>, <c>hire-mercenary</c>, <c>embark-army</c>,
/// <c>disembark-army</c>); <c>success-fleet.txt</c> adds <c>attack-fleet</c>.
/// </para>
/// <para>
/// <strong>Three verbs this run issues but expects rejected</strong> — <c>hire-mercenary</c>,
/// <c>embark-army</c>, <c>disembark-army</c> — matching <c>tests/IC2.Engine.Tests/Presentation/CommandCoverageTests.cs</c>'s
/// own <c>ConfirmedUnreachable</c> set: no shipped scenario can ever make any of the three succeed
/// (<c>hire-mercenary</c>: the mercenary pool is always empty, keyed to #229/#457; <c>embark-army</c>/
/// <c>disembark-army</c>: no cell is ever both a legal army position and a legal fleet position, keyed
/// to #453). <c>docs/tasks/T24.md</c> itself says the Done-when only asks that the run "issue each order
/// and exit 0, not that every order is accepted" — so this run issues all three anyway, through the same
/// command layer, and reports the rejection each one actually produces.
/// </para>
/// <para>
/// <strong>Three command types this run cannot issue at all.</strong> <c>diplomacy.ai-form-alliance</c>,
/// <c>diplomacy.ai-form-trade</c> and <c>diplomacy.ai-swap-trade-partner</c> have no CLI verb anywhere in
/// <see cref="GameSession"/>'s own <c>Submit</c> switch — they are the AI's own direct, no-consent
/// writes an AI seat's diplomacy phase dispatches internally, never routed through any player-facing
/// verb (<c>CommandCoverageTests</c>'s own <c>NoCliVerbByDesign</c>, confirmed by reading
/// <c>GameSession.cs</c>/<c>GameSession.Commands.cs</c> directly). There is no missing capability to add
/// here: a screen cannot "issue" a command that has no verb without inventing one, which is a
/// <c>src/</c> change outside this task's Owns list — reported here rather than worked around.
/// </para>
/// </remarks>
public partial class OrderCoverageCheck : Node
{
    public override void _Ready()
    {
        var exitCode = 0;
        try
        {
            exitCode = Run() ? 0 : 1;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"OrderCoverageCheck: unhandled exception: {ex}");
            exitCode = 1;
        }

        GD.Print($"OrderCoverageCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    private bool Run()
    {
        var repositoryRoot = GameSessionFactory.RepositoryRootFromGlobalizedResPath(ProjectSettings.GlobalizePath("res://"));
        var repository = GameSessionFactory.LoadRepository(repositoryRoot);

        GD.Print("== classical-mediterranean / macedonia / seed 3 (tests/fixtures/cli/success.txt) ==");
        var classical = repository.Resolve("classical-mediterranean");
        var classicalSession = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: 3, humanSeatNationId: "macedonia");
        RunScriptFile(classicalSession, Path.Combine(repositoryRoot, "tests", "fixtures", "cli", "success.txt"));

        GD.Print();
        GD.Print("== toy-3city / north (tests/fixtures/cli/success-fleet.txt) ==");
        var toy = repository.Resolve("toy-3city");
        var toySession = new GameSession(toy.World, toy.Ruleset, toy.Scenario);
        RunScriptFile(toySession, Path.Combine(repositoryRoot, "tests", "fixtures", "cli", "success-fleet.txt"));

        GD.Print();
        GD.Print("== confirmed-unreachable verbs, issued anyway (see this class's own remarks) ==");

        // A fresh session, untouched by success-fleet.txt's own attack-fleet battle (which sinks
        // north-fleet-1) -- issued here so embark-army's own rejection is the structural cell-mismatch
        // ConfirmedUnreachable actually describes, not "fleet no longer exists".
        var freshToySession = new GameSession(toy.World, toy.Ruleset, toy.Scenario);
        IssueAndReport(freshToySession, "hire-mercenary north-army-1 0");
        IssueAndReport(freshToySession, "embark-army north-army-1 north-fleet-1");
        IssueAndReport(freshToySession, "disembark-army north-army-1");

        GD.Print();
        GD.Print(
            "NOTE: diplomacy.ai-form-alliance, diplomacy.ai-form-trade and diplomacy.ai-swap-trade-partner "
            + "have no CLI verb in GameSession.Submit at all and cannot be issued from this screen -- "
            + "AI-internal writes, confirmed against GameSession.cs/GameSession.Commands.cs (see class remarks).");

        return true;
    }

    /// <summary>Runs every non-<c>quit</c> line in <paramref name="scriptPath"/> and prints each outcome.</summary>
    private static void RunScriptFile(GameSession session, string scriptPath)
    {
        foreach (var rawLine in File.ReadAllLines(scriptPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || string.Equals(line, "quit", StringComparison.Ordinal))
            {
                continue;
            }

            var output = session.Submit(line);
            foreach (var text in output.Lines)
            {
                GD.Print(text);
            }

            if (output.ShouldExit)
            {
                GD.Print($"(session ended early at line '{line}')");
                break;
            }
        }
    }

    private static void IssueAndReport(GameSession session, string line)
    {
        var output = session.Submit(line);
        foreach (var text in output.Lines)
        {
            GD.Print(text);
        }
    }
}
