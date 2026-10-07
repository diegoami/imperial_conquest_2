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
/// <strong>Rework round 1 (PR #466 review, findings 1 and 5, both blocking): this class used to print
/// every command's outcome but never judge it</strong> — <c>Run()</c> always returned
/// <see langword="true"/>, so a command that should have succeeded but was silently rejected (the
/// reviewer's own proof: prepending <c>move totally-bogus-army-id 0 0</c> to <c>success.txt</c> still
/// exited 0) would never fail this check. Every issued line's own outcome is now judged by
/// <see cref="JudgeOutcome"/> — the same rule
/// <c>tests/IC2.Engine.Tests/Presentation/SuccessScriptTests.cs</c>'s own <c>BlockLooksAccepted</c>
/// applies (the generic renderer's exact <c>"{kind} accepted."</c>, or <c>move</c>/<c>buy</c>'s own
/// bespoke wording), widened by exactly one rule this class needs that the xunit test does not: a
/// rejection is still correct, not a failure, when the line's own kind is one of
/// <see cref="CommandVerbCatalog.ConfirmedUnreachable"/> (the three verbs this class deliberately issues
/// expecting a rejection — see below). Any other non-acceptance (a genuine rejection, a
/// <c>Usage:</c> line, <c>Unknown command</c>, or any shape that is not the accepted text) now fails the
/// whole run, printed via <see cref="GD.PrintErr"/> and reflected in the process exit code.
/// </para>
/// <para>
/// <strong>Finding 5 (non-blocking, fixed anyway): reflection coverage.</strong> This class used to get
/// its "one order of each type" list purely from two hand-written fixture scripts plus three hardcoded
/// calls — nothing made it notice if a newly added <c>ICommand</c> type were never issued by either.
/// <see cref="Run"/> now closes that the same way
/// <c>tests/IC2.Engine.Tests/Presentation/CommandCoverageTests.cs</c> does: enumerate every
/// <c>ICommand</c>-implementing type by reflection (<see cref="CommandVerbCatalog.AllCommandTypes"/>),
/// and fail, naming it, if a type outside <see cref="CommandVerbCatalog.NoCliVerbByDesign"/> was never
/// issued during this run.
/// </para>
/// <para>
/// <strong>Three verbs this run issues but expects rejected</strong> — <c>hire-mercenary</c>,
/// <c>embark-army</c>, <c>disembark-army</c> — matching <see cref="CommandVerbCatalog.ConfirmedUnreachable"/>:
/// no shipped scenario can ever make any of the three succeed (<c>hire-mercenary</c>: the mercenary pool
/// is always empty, keyed to #229/#457; <c>embark-army</c>/<c>disembark-army</c>: no cell is ever both a
/// legal army position and a legal fleet position, keyed to #453). <c>docs/tasks/T24.md</c> itself says
/// the Done-when only asks that the run "issue each order and exit 0, not that every order is accepted"
/// — so this run issues all three anyway, through the same command layer, and reports (and now judges
/// correct) the rejection each one actually produces.
/// </para>
/// <para>
/// <strong>Three command types this run cannot issue at all.</strong> <c>diplomacy.ai-form-alliance</c>,
/// <c>diplomacy.ai-form-trade</c> and <c>diplomacy.ai-swap-trade-partner</c> have no CLI verb anywhere in
/// <see cref="GameSession"/>'s own <c>Submit</c> switch — they are the AI's own direct, no-consent
/// writes an AI seat's diplomacy phase dispatches internally, never routed through any player-facing
/// verb (<see cref="CommandVerbCatalog.NoCliVerbByDesign"/>, confirmed by reading
/// <c>GameSession.cs</c>/<c>GameSession.Commands.cs</c> directly). There is no missing capability to add
/// here: a screen cannot "issue" a command that has no verb without inventing one, which is a
/// <c>src/</c> change outside this task's Owns list — reported here rather than worked around.
/// </para>
/// </remarks>
public partial class OrderCoverageCheck : Node
{
    private readonly List<string> _failures = new();
    private readonly HashSet<string> _issuedKinds = new(StringComparer.Ordinal);

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

        CheckEveryReflectedCommandTypeWasIssued();

        GD.Print();
        if (_failures.Count == 0)
        {
            GD.Print("OrderCoverageCheck: every issued line produced its expected outcome.");
            return true;
        }

        GD.PrintErr($"OrderCoverageCheck: {_failures.Count} failure(s):");
        foreach (var failure in _failures)
        {
            GD.PrintErr(" - " + failure);
        }

        return false;
    }

    /// <summary>Runs every non-<c>quit</c> line in <paramref name="scriptPath"/>, prints each outcome,
    /// and judges it (see this class's own remarks).</summary>
    private void RunScriptFile(GameSession session, string scriptPath)
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

            JudgeLine(line, output.Lines);

            if (output.ShouldExit)
            {
                GD.Print($"(session ended early at line '{line}')");
                break;
            }
        }
    }

    private void IssueAndReport(GameSession session, string line)
    {
        var output = session.Submit(line);
        foreach (var text in output.Lines)
        {
            GD.Print(text);
        }

        JudgeLine(line, output.Lines);
    }

    /// <summary>
    /// Judges one submitted <paramref name="line"/>'s own <paramref name="outputLines"/> — the rule
    /// this class's own remarks describe: accepted, or a rejection whose own kind is
    /// <see cref="CommandVerbCatalog.ConfirmedUnreachable"/>, or no judgement at all for a read-only
    /// verb. Anything else is appended to <see cref="_failures"/>. Also records the kind as issued
    /// (<see cref="_issuedKinds"/>) whenever the verb resolves to one, regardless of accepted/rejected —
    /// "issued" is not "accepted" (<c>docs/tasks/T24.md</c>'s own relaxed Done-when 2 wording).
    /// </summary>
    private void JudgeLine(string line, IReadOnlyList<string> outputLines)
    {
        var verb = line.Split(' ', 2)[0];

        if (CommandVerbCatalog.ReadOnlyVerbs.Contains(verb))
        {
            return;
        }

        if (!CommandVerbCatalog.KindByVerb.TryGetValue(verb, out var kind))
        {
            _failures.Add($"line '{line}': verb '{verb}' is not a known mutating verb (typo, or CommandVerbCatalog is stale).");
            return;
        }

        _issuedKinds.Add(kind);

        if (IsAcceptedOutcome(verb, kind, outputLines))
        {
            return;
        }

        if (CommandVerbCatalog.ConfirmedUnreachable.Contains(kind))
        {
            // Expected: this exact kind can never succeed against any shipped scenario -- a rejection
            // here is the correct, proven outcome, not a failure.
            return;
        }

        _failures.Add(
            $"line '{line}' (kind '{kind}') did not produce an accepted outcome: "
            + string.Join(" | ", outputLines));
    }

    /// <summary>
    /// Whether <paramref name="outputLines"/> reads as an accepted outcome for <paramref name="verb"/>/
    /// <paramref name="kind"/> — the same rule <c>SuccessScriptTests.BlockLooksAccepted</c> applies:
    /// <c>move</c>/<c>buy</c> keep their own bespoke wording (T41's contract) and <c>transfer-money</c>
    /// its own since fix #710; every other kind shares the generic renderer's exact
    /// <c>"{kind} accepted."</c> line. A plain <c>Contains</c> check, not a
    /// <c>" rejected"</c> short-circuit: that also catches a <c>Usage:</c> line or an <c>Unknown
    /// command</c> reply, neither of which contains the word "rejected" at all but is not an accepted
    /// outcome either.
    /// </summary>
    private static bool IsAcceptedOutcome(string verb, string kind, IReadOnlyList<string> outputLines)
    {
        var outcome = string.Join('\n', outputLines);

        return verb switch
        {
            "move" => outcome.Contains("moved from (", StringComparison.Ordinal),
            "buy" => outcome.Contains("bought ", StringComparison.Ordinal)
                && outcome.Contains(" tons of supply", StringComparison.Ordinal),
            "transfer-money" => outcome.Contains(" talents from ", StringComparison.Ordinal),
            _ => outcome.Contains($"{kind} accepted.", StringComparison.Ordinal),
        };
    }

    /// <summary>Finding 5: fails, naming it, if a reflected command type outside
    /// <see cref="CommandVerbCatalog.NoCliVerbByDesign"/> was never issued by this run.</summary>
    private void CheckEveryReflectedCommandTypeWasIssued()
    {
        foreach (var commandType in CommandVerbCatalog.AllCommandTypes)
        {
            var kind = CommandVerbCatalog.KindOf(commandType);
            if (CommandVerbCatalog.NoCliVerbByDesign.Contains(kind))
            {
                continue;
            }

            if (!_issuedKinds.Contains(kind))
            {
                _failures.Add(
                    $"{commandType.FullName} (kind '{kind}') was never issued by this run -- add a line "
                    + "for it (or, if it belongs in CommandVerbCatalog.NoCliVerbByDesign/ConfirmedUnreachable, "
                    + "add it there).");
            }
        }
    }
}
