using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// Bug #491's own reproduction turned into a headless check: selecting an army whose Troops line is long
/// (five unit slots, e.g. <c>"5000x heavy_infantry, ..."</c>) no longer pushes the persistent side panel
/// (<see cref="ContextPanel"/>) past its 340&#160;px <see cref="MainGameScreen"/> floor, and every one of
/// its order buttons (T99's list: Mobilize/Disband) stays inside the viewport. Godot headless still
/// builds a real <see cref="Control"/> tree and runs real layout code (see
/// <c>godot/Screens/Checks/ScreensCheck.cs</c>'s own remarks) — only pixel readback needs a window, and
/// this check never reads a pixel. Run headless via:
/// <code>
/// Godot_..._console.exe --headless --path godot res://Checks/ContextPanelWidthCheck.tscn --quit-after 4
/// </code>
/// </summary>
/// <remarks>
/// This check's own root <see cref="Control"/> keeps its default (all-zero, equal) anchors deliberately
/// and sets <see cref="Control.Size"/> directly instead — the same reason
/// <c>godot/Checks/ScreenshotTour.cs</c>'s own remarks give: a scene reached by an explicit
/// <c>--path</c> argument never gets its Size resolved from anchors the way a project's own
/// <c>run/main_scene</c> does. <see cref="MainGameScreen"/> is then added as this Control's own child
/// with <see cref="Control.LayoutPreset.FullRect"/>, exactly as <c>AppRoot.SwapTo</c> adds it in the real
/// app, so its own 340&#160;px context panel and body <c>HBoxContainer</c> lay out against a real
/// 1500x850 viewport (<c>project.godot</c>'s configured size), not a zero-size parent. Two settle-frame
/// waits (the same <c>SettleFrames</c> pattern <c>godot/Checks/ScreenshotTour.cs</c> and
/// <c>godot/Checks/SaveResumeScreenshotTour.cs</c> already use) give the container layout pass — which
/// Godot defers to the next frame rather than running inline — a chance to run before anything is read.
/// </remarks>
public partial class ContextPanelWidthCheck : Control
{
    private const int SettleFrames = 6;
    private const string LongTroopsArmyId = "north-army-1";

    private MainGameScreen _mainGame = null!;
    private int _frame;
    private int _step;

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;

        _mainGame = new MainGameScreen
        {
            Session = BuildSessionWithALongTroopsLine(),
            RepositoryRoot = GameDataContext.RepositoryRoot,
        };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);
    }

    public override void _Process(double delta)
    {
        _frame++;

        try
        {
            switch (_step)
            {
                case 0 when _frame >= SettleFrames:
                    _mainGame.ContextPanel.ShowArmy(LongTroopsArmyId);
                    _frame = 0;
                    _step = 1;
                    break;

                case 1 when _frame >= SettleFrames:
                    var ok = RunChecks();
                    var exitCode = ok ? 0 : 1;
                    GD.Print($"ContextPanelWidthCheck: exiting with code {exitCode}.");
                    GetTree().Quit(exitCode);
                    break;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ContextPanelWidthCheck: unhandled exception: {ex}");
            GetTree().Quit(1);
        }
    }

    private bool RunChecks()
    {
        var ok = true;
        var contextPanel = _mainGame.ContextPanel;
        var viewportSize = GetViewport().GetVisibleRect().Size;

        ok &= Check(
            Mathf.Abs(contextPanel.Size.X - 340f) < 1f,
            "the context panel keeps its 340px minimum width after selecting an army with a long "
            + $"Troops line (got {contextPanel.Size.X}px)");

        var panelRect = contextPanel.GetGlobalRect();
        var panelInsideViewport =
            panelRect.Position.X >= -0.5f && panelRect.Position.Y >= -0.5f
            && panelRect.Position.X + panelRect.Size.X <= viewportSize.X + 0.5f
            && panelRect.Position.Y + panelRect.Size.Y <= viewportSize.Y + 0.5f;
        ok &= Check(
            panelInsideViewport,
            $"the panel's rect ({panelRect}) lies inside the viewport "
            + $"(0,0)-({viewportSize.X},{viewportSize.Y})");

        // T112 Done-when 5: the panel is information only, so no Button of any kind remains in it.
        var buttons = FindButtons(contextPanel).ToList();
        ok &= Check(
            buttons.Count == 0,
            $"the panel renders no order button (found {buttons.Count}: {string.Join(", ", buttons.Select(b => b.Text))})");

        // T109 Done-when 5: the city panel's Recruit section and the army panel's "Mobilize first
        // ready slot" both moved to the Strategy menu's dialogs. The panel may carry other Labels
        // (the city's "In training here" list, the army's "In training here" caption, etc.) but no
        // Button reads "Recruit" or "Mobilize first ready slot".
        var recButtons = buttons
            .Where(button => button.Text.Contains("Recruit", StringComparison.Ordinal)
                || button.Text.Contains("Mobilize first ready slot", StringComparison.Ordinal))
            .ToList();
        ok &= Check(
            recButtons.Count == 0,
            $"the panel renders no Recruit / Mobilize-first-ready-slot button (found {recButtons.Count}: {string.Join(", ", recButtons.Select(b => b.Text))})");

        return ok;
    }

    private static IEnumerable<Button> FindButtons(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Button button)
            {
                yield return button;
            }

            foreach (var nested in FindButtons(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// The shipped toy world, with <c>north-army-1</c>'s <see cref="UnitSlot"/> list widened from its
    /// shipped single slot to five — the same repositioning-by-<c>with</c> convention
    /// <c>godot/Screens/Checks/ScreensCheck.cs</c>'s own <c>BattleReadySession</c> uses — so its Troops
    /// line (<c>ContextPanel.BuildArmyPanel</c>: <c>string.Join(", ", army.Units.Select(...))</c>) is long
    /// enough to reproduce bug #491 ("5000x heavy_infantr…"). All five unit-type ids
    /// (<c>heavy_infantry</c>, <c>light_cavalry</c>, <c>archers</c>, <c>heavy_cavalry</c>,
    /// <c>light_infantry</c>) are the same ones <see cref="ContextPanel"/>'s own <c>UnitTypes</c> table
    /// already lists, so they are guaranteed valid ruleset keys.
    /// </summary>
    private static GameSession BuildSessionWithALongTroopsLine()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var original = toy.World.StartingArmies.Single(a => a.Id == LongTroopsArmyId);

        var longTroopsArmy = original with
        {
            Units = ValueList.Of(
                new UnitSlot(MercenaryLabel: 0, UnitTypeId: "heavy_infantry", Troops: 5000, Quality: 6, Name: "1st Guards Battalion"),
                new UnitSlot(MercenaryLabel: 0, UnitTypeId: "light_cavalry", Troops: 3200, Quality: 5, Name: "2nd Lancers Battalion"),
                new UnitSlot(MercenaryLabel: 0, UnitTypeId: "archers", Troops: 2750, Quality: 4, Name: "3rd Bowmen Battalion"),
                new UnitSlot(MercenaryLabel: 0, UnitTypeId: "heavy_cavalry", Troops: 4100, Quality: 5, Name: "4th Dragoons Battalion"),
                new UnitSlot(MercenaryLabel: 0, UnitTypeId: "light_infantry", Troops: 1800, Quality: 3, Name: "5th Foot Battalion")),
        };

        var customWorld = toy.World with
        {
            StartingArmies = ValueList.From(
                toy.World.StartingArmies.Select(a => a.Id == LongTroopsArmyId ? longTroopsArmy : a)),
        };

        return new GameSession(customWorld, toy.Ruleset, toy.Scenario);
    }

    private static bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        return condition;
    }
}
