using Godot;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// <c>docs/tasks/T24.md</c> Done-when 4: a scripted headless run that reaches New Game's own flow and
/// asserts (a) the ruleset chooser renders both <c>Classical Faithful</c> and <c>Improved</c> as
/// equally-weighted, labelled options, (b) <c>Classical Faithful</c> is pre-selected, (c) no
/// scenario/seat control exists before the chooser's own Continue fires, and (d) picking either value is
/// what the scenario bootstrap actually reads. Run headless via
/// <c>Godot_..._console.exe --headless --path godot res://Checks/RulesetFlowCheck.tscn --quit-after 4</c>
/// (see this task's own PR body for the exact captured command and output).
/// </summary>
/// <remarks>
/// This exercises the real <see cref="NewGameFlow"/>/<see cref="RulesetChooserScreen"/>/
/// <see cref="ScenarioSeatScreen"/> scene classes directly (the same ones <see cref="AppRoot"/> uses for
/// a real player) rather than a hand-maintained mirror of them — <see cref="RulesetChooserScreen.CardButtons"/>,
/// <see cref="RulesetChooserScreen.CardTitleLabels"/> and <see cref="RulesetChooserScreen.ConfirmSelection"/>
/// exist specifically so this check (and any future test) can assert on and drive the actual scene tree.
/// </remarks>
public partial class RulesetFlowCheck : Node
{
    private bool _ok = true;

    public override void _Ready()
    {
        try
        {
            CheckChooserRendersBothCardsEquallyWeighted();
            CheckDefaultIsClassicalFaithfulAndSeatScreenUnreachable();
            CheckPickingImprovedBootstrapsTheImprovedRuleset();
            CheckPickingTheDefaultBootstrapsClassicalFaithful();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"RulesetFlowCheck: unhandled exception: {ex}");
            _ok = false;
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"RulesetFlowCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    private void CheckChooserRendersBothCardsEquallyWeighted()
    {
        var flow = new NewGameFlow();
        AddChild(flow);
        var chooser = flow.Chooser;

        if (!Check(chooser is not null, "the New Game flow shows the ruleset chooser first"))
        {
            return;
        }

        Check(chooser!.CardButtons.Count == 2, $"exactly two ruleset cards render (found {chooser.CardButtons.Count})");
        Check(
            chooser.CardTitleLabels.TryGetValue(RulesetPreset.ClassicalFaithful, out var faithfulLabel)
            && faithfulLabel.Text == "Classical Faithful",
            "the Classical Faithful card is labelled 'Classical Faithful'");
        Check(
            chooser.CardTitleLabels.TryGetValue(RulesetPreset.Improved, out var improvedLabel)
            && improvedLabel.Text == "Improved",
            "the Improved card is labelled 'Improved'");

        if (chooser.CardButtons.TryGetValue(RulesetPreset.ClassicalFaithful, out var faithfulButton)
            && chooser.CardButtons.TryGetValue(RulesetPreset.Improved, out var improvedButton))
        {
            Check(
                faithfulButton.CustomMinimumSize == improvedButton.CustomMinimumSize
                && Mathf.IsEqualApprox(faithfulButton.SizeFlagsStretchRatio, improvedButton.SizeFlagsStretchRatio)
                && faithfulButton.SizeFlagsHorizontal == improvedButton.SizeFlagsHorizontal,
                "both cards are laid out with the same minimum size and stretch ratio (equally weighted)");
        }

        flow.QueueFree();
    }

    private void CheckDefaultIsClassicalFaithfulAndSeatScreenUnreachable()
    {
        var flow = new NewGameFlow();
        AddChild(flow);

        Check(
            flow.Chooser?.SelectedPreset == RulesetPreset.ClassicalFaithful,
            "Classical Faithful is the pre-selected default");
        Check(
            flow.SeatScreen is null,
            "no scenario/seat control exists before the ruleset chooser's own Continue fires");

        flow.QueueFree();
    }

    private void CheckPickingImprovedBootstrapsTheImprovedRuleset()
    {
        var flow = new NewGameFlow();
        AddChild(flow);

        flow.Chooser!.SelectPreset(RulesetPreset.Improved);
        flow.Chooser!.ConfirmSelection();

        Check(flow.Chooser is null, "the ruleset chooser is gone once the seat step is reached");
        Check(flow.SeatScreen is not null, "the seat step is reached after Continue");
        Check(
            flow.SeatScreen?.Selection.RulesetPreset == RulesetPreset.Improved,
            "the seat step's own selection carries the picked ruleset (Improved), not a cosmetic copy");

        if (flow.SeatScreen is { } seatScreen)
        {
            seatScreen.Selection.HumanNationId = "rome";
            var session = GameSessionFactory.CreateSession(GameDataContext.Repository, seatScreen.Selection);
            Check(
                session.Ruleset.Id == "improved" && session.Scenario.Id == "example-classical-improved",
                $"picking Improved bootstraps ruleset 'improved' via scenario 'example-classical-improved' "
                + $"(got ruleset '{session.Ruleset.Id}', scenario '{session.Scenario.Id}')");
        }

        flow.QueueFree();
    }

    private void CheckPickingTheDefaultBootstrapsClassicalFaithful()
    {
        var flow = new NewGameFlow();
        AddChild(flow);

        // Never touches SelectPreset -- proves the *default*, not merely a re-confirmed explicit choice.
        flow.Chooser!.ConfirmSelection();

        if (flow.SeatScreen is { } seatScreen)
        {
            seatScreen.Selection.HumanNationId = "rome";
            var session = GameSessionFactory.CreateSession(GameDataContext.Repository, seatScreen.Selection);
            Check(
                session.Ruleset.Id == "classical-faithful" && session.Scenario.Id == "classical-mediterranean",
                $"leaving the default bootstraps ruleset 'classical-faithful' via scenario "
                + $"'classical-mediterranean' (got ruleset '{session.Ruleset.Id}', scenario '{session.Scenario.Id}')");
        }
        else
        {
            Check(false, "the seat step was reached for the default selection");
        }

        flow.QueueFree();
    }

    private bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
        return condition;
    }
}
