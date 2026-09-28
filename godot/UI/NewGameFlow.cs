using Godot;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// New Game's own two-step flow (<c>docs/tasks/T24.md</c> Scope): the ruleset chooser
/// (<see cref="RulesetChooserScreen"/>), then — only once that screen's own Continue fires — the
/// scenario/seat step (<see cref="ScenarioSeatScreen"/>). Owns the swap between the two so
/// <c>docs/tasks/T24.md</c> Done-when 4 ("before any scenario/seat control is reachable") is a fact
/// about this class's own child list, not merely about visibility: the seat screen is never
/// instantiated at all until <see cref="ShowSeatStep"/> runs.
/// </summary>
public partial class NewGameFlow : Control
{
    /// <summary>Raised once "Start Game" produces a real, playable session.</summary>
    public event Action<GameSession>? GameStarted;

    /// <summary>Raised by either step's own "Back" button on the ruleset step — returns to the main menu.</summary>
    public event Action? BackToMenuRequested;

    private readonly NewGameSelection _selection = new();

    /// <summary>Exposed for <c>godot/Checks/RulesetFlowCheck.cs</c> and any test that drives this flow
    /// directly rather than through simulated clicks — see <see cref="RulesetChooserScreen"/>'s own
    /// remarks for why its members are public for the same reason.</summary>
    public RulesetChooserScreen? Chooser { get; private set; }

    public ScenarioSeatScreen? SeatScreen { get; private set; }

    public override void _Ready()
    {
        ShowRulesetStep();
    }

    private void ShowRulesetStep()
    {
        ClearChildren();
        SeatScreen = null;

        var chooser = new RulesetChooserScreen();
        chooser.SetAnchorsPreset(LayoutPreset.FullRect);
        Chooser = chooser;
        AddChild(chooser);

        chooser.Continued += preset =>
        {
            _selection.RulesetPreset = preset;
            ShowSeatStep();
        };
        chooser.BackRequested += () => BackToMenuRequested?.Invoke();
    }

    private void ShowSeatStep()
    {
        ClearChildren();
        Chooser = null;

        var seatScreen = new ScenarioSeatScreen { Selection = _selection };
        seatScreen.SetAnchorsPreset(LayoutPreset.FullRect);
        SeatScreen = seatScreen;
        AddChild(seatScreen);

        seatScreen.Started += () =>
        {
            var session = GameSessionFactory.CreateSession(GameDataContext.Repository, _selection);
            GameStarted?.Invoke(session);
        };
        seatScreen.BackRequested += ShowRulesetStep;
    }

    private void ClearChildren()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
    }
}
