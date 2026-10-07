using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// Fix #804 R1: the real <see cref="ChangeUnitsDialog"/>'s Disband button asks the original's
/// confirmation through the app's <see cref="ConfirmPrompt"/> and acts on <em>every</em> selected
/// unit. This check drives the dialog itself, not its Godot-free model: it selects rows in the real
/// unit list, presses the real Disband button, observes the question the prompt shows, and answers
/// through the prompt's own No and Yes buttons — so deleting the prompt, or swapping or dropping the
/// Yes/No wiring, fails it.
/// </summary>
/// <remarks>
/// The model-level prompt text and order composition stay pinned by
/// <c>tests/IC2.Engine.Tests/Ui/ArmyDialogModelsTests.cs</c>, which CI can run; this is the
/// dialog-level half CI cannot (no Godot SDK), the same split
/// <c>godot/Checks/ArmyOrdersCheck.cs</c> already uses. Run headless via:
/// <code>
/// godot --headless --path godot res://Checks/ChangeUnitsDialogTests.tscn --quit-after 300
/// </code>
/// </remarks>
public partial class ChangeUnitsDialogTests : Control
{
    private const int SettleFrames = 6;

    private const string Rome = "rome";
    private const string ArmyId = "fix804-change";

    private GameSession _session = null!;
    private readonly List<string> _submitted = new();
    private readonly List<Action> _steps = new();
    private int _stepIndex;
    private int _frame;
    private bool _ok = true;

    public override void _Ready()
    {
        _session = BuildSession();
        _steps.Add(DisbandSingleUnitPromptAndNo);
        _steps.Add(DisbandTwoUnitsPromptAndNo);
        _steps.Add(DisbandTwoUnitsYes);
        _steps.Add(Finish);
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_frame < SettleFrames || _stepIndex >= _steps.Count)
        {
            return;
        }

        _frame = 0;
        try
        {
            _steps[_stepIndex++]();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ChangeUnitsDialogTests: unhandled exception: {ex}");
            GetTree().Quit(1);
        }
    }

    /// <summary>
    /// One selected unit: the prompt asks the original's singular text, and No submits nothing and
    /// leaves the army untouched.
    /// </summary>
    private void DisbandSingleUnitPromptAndNo()
    {
        var dialog = OpenDialog();
        SelectUnits(dialog, 0);
        var before = _session.State.ArmyById(ArmyId)!.Units.Count;
        _submitted.Clear();

        var prompt = PressDisband(dialog);
        if (prompt is null)
        {
            return;
        }

        Check(
            prompt.Question == ChangeUnitsModel.DisbandPromptText(1),
            $"Disband asks the singular prompt for one unit ('{prompt.Question}')");
        Press(prompt, "No");
        Check(_submitted.Count == 0, $"No on the one-unit prompt submits nothing ({_submitted.Count})");
        Check(
            _session.State.ArmyById(ArmyId)!.Units.Count == before,
            "No on the one-unit prompt leaves every unit");
        dialog.QueueFree();
    }

    /// <summary>
    /// Two selected units: the prompt asks the original's plural text, and No submits nothing and
    /// leaves the army untouched.
    /// </summary>
    private void DisbandTwoUnitsPromptAndNo()
    {
        var dialog = OpenDialog();
        SelectUnits(dialog, 0, 1);
        var before = _session.State.ArmyById(ArmyId)!.Units.Count;
        _submitted.Clear();

        var prompt = PressDisband(dialog);
        if (prompt is null)
        {
            return;
        }

        Check(
            prompt.Question == ChangeUnitsModel.DisbandPromptText(2),
            $"Disband asks the plural prompt for two units ('{prompt.Question}')");
        Press(prompt, "No");
        Check(_submitted.Count == 0, $"No on the two-unit prompt submits nothing ({_submitted.Count})");
        Check(
            _session.State.ArmyById(ArmyId)!.Units.Count == before,
            "No on the two-unit prompt leaves every unit");
        dialog.QueueFree();
    }

    /// <summary>
    /// Two selected units and Yes: one <c>disband-unit</c> per selected unit, highest index first,
    /// and the engine removes exactly those two units.
    /// </summary>
    private void DisbandTwoUnitsYes()
    {
        var dialog = OpenDialog();
        SelectUnits(dialog, 0, 1);
        var original = _session.State.ArmyById(ArmyId)!.Units.ToList();
        _submitted.Clear();

        var prompt = PressDisband(dialog);
        if (prompt is null)
        {
            return;
        }

        Press(prompt, "Yes");

        var expected = new[] { $"disband-unit {ArmyId} 1", $"disband-unit {ArmyId} 0" };
        Check(
            _submitted.SequenceEqual(expected),
            $"Yes submits one order per selected unit, highest index first ({string.Join(", ", _submitted)})");
        var after = _session.State.ArmyById(ArmyId)!.Units;
        Check(after.Count == original.Count - 2, $"Yes removes the two selected units ({after.Count} left)");
        Check(
            !after.Contains(original[0]) && !after.Contains(original[1]),
            "Yes removes exactly the selected units");
        dialog.QueueFree();
    }

    /// <summary>Builds the dialog for the check's own army and adds it to the tree.</summary>
    private ChangeUnitsDialog OpenDialog()
    {
        var dialog = new ChangeUnitsDialog
        {
            Session = _session,
            ArmyId = ArmyId,
            Submit = line =>
            {
                _submitted.Add(line);
                return _session.Submit(line).Lines;
            },
        };
        AddChild(dialog);
        return dialog;
    }

    /// <summary>Selects the given rows in the real unit list, the way a click does.</summary>
    private static void SelectUnits(ChangeUnitsDialog dialog, params int[] indexes)
    {
        dialog.UnitListForCheck.DeselectAll();
        foreach (var index in indexes)
        {
            dialog.UnitListForCheck.Select(index, single: false);
        }
    }

    /// <summary>Presses the dialog's real Disband button and returns the prompt it opened.</summary>
    private ConfirmPrompt? PressDisband(ChangeUnitsDialog dialog)
    {
        var disband = ButtonsUnder(dialog).FirstOrDefault(button => button.Text == "Disband");
        Check(disband is not null, "the Change units dialog has a Disband button");
        disband?.EmitSignal(BaseButton.SignalName.Pressed);

        var prompt = dialog.GetChildren().OfType<ConfirmPrompt>().FirstOrDefault();
        Check(prompt is not null, "Disband opens the confirmation prompt before it acts");
        return prompt;
    }

    /// <summary>Answers the prompt through its own named button, the way a click does.</summary>
    private void Press(ConfirmPrompt prompt, string answer)
    {
        var button = ButtonsUnder(prompt).FirstOrDefault(candidate => candidate.Text == answer);
        Check(button is not null, $"the prompt has a '{answer}' button");
        button?.EmitSignal(BaseButton.SignalName.Pressed);
    }

    /// <summary>Exits with 0 only when every check passed.</summary>
    private void Finish()
    {
        GD.Print($"ChangeUnitsDialogTests: exiting with code {(_ok ? 0 : 1)}.");
        GetTree().Quit(_ok ? 0 : 1);
    }

    /// <summary>
    /// The shipped classical world with Rome as the human seat and one extra Roman army of three
    /// units at (100,100), the free cell <c>godot/Checks/ArmyOrdersCheck.cs</c> also uses.
    /// </summary>
    private static GameSession BuildSession()
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        var armies = resolved.World.StartingArmies.ToList();
        armies.Add(new StartingArmy(
            ArmyId, Rome, X: 100, Y: 100, Morale: 70, Money: 0, SupplyTons: 0, Moves: 8,
            Units: ValueList.From(new[]
            {
                new UnitSlot(0, "heavy_infantry", 5_000, 6, "1st Guards"),
                new UnitSlot(0, "archers", 3_000, 8, "2nd Bowmen"),
                new UnitSlot(0, "light_cavalry", 2_000, 7, "3rd Lancers"),
            })));

        var world = resolved.World with { StartingArmies = ValueList.From(armies) };
        return new GameSession(
            world, resolved.Ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: Rome);
    }

    private static IEnumerable<Button> ButtonsUnder(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Button button)
            {
                yield return button;
            }

            foreach (var nested in ButtonsUnder(child))
            {
                yield return nested;
            }
        }
    }

    private void Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
    }
}
