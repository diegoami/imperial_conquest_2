using Godot;

namespace IC2.Slice.UI;

/// <summary>
/// <c>docs/tasks/T100.md</c> Scope, "Help topics": a short in-game help page — <strong>[designed]</strong>
/// new text, because the original's help file (<c>Imperial Conquest 2.HLP</c>) is its own content and
/// cannot ship. It covers the mouse and keyboard controls, one line per menu, and where the rules come
/// from.
/// </summary>
/// <remarks>
/// <para>
/// Shown by <see cref="MainGameScreen.ShowOverlay"/> as a full-rect modal, like the other overlays. The
/// wording is this task's own: it never quotes or paraphrases the original's help file (T100's hazard
/// note), and the menu lines are the inventory's own menu names, not the original's help text.
/// </para>
/// <para>
/// The rules provenance is the user's decision recorded in the task entry: <c>classical-faithful</c> is
/// built from the original's own data and the research reports; <c>improved</c> is designed.
/// </para>
/// </remarks>
public partial class HelpPage : Control
{
    /// <summary>Raised when the player dismisses the page.</summary>
    public event Action? Closed;

    public override void _Ready()
    {
        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.65f), MouseFilter = MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.MakePanel(UiKit.PanelColorRaised);
        panel.CustomMinimumSize = new Vector2(640, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel("Help", 22, UiKit.AccentColor));

        AddSection(column, "Controls");
        AddLine(column, "Left click: select your army, fleet or city; click a target to give the selected unit an order.");
        AddLine(column, "Right click: show the clicked city's, army's or fleet's unit list.");
        AddLine(column, "Mouse wheel: zoom the map. Drag with the left button: pan.");
        AddLine(column, "Esc or Shift+X: cancel the current selection.");

        AddSection(column, "Menus");
        AddLine(column, "File: start a new game, open or save one, or leave this game.");
        AddLine(column, "Game: end the current turn.");
        AddLine(column, "Strategy: the news log and international relations (the economic dialogs are not yet available; shown disabled).");
        AddLine(column, "Nations: choose the nation you are viewing (not yet available; shown disabled).");
        AddLine(column, "Area map: highlight cities, capitals, armies, fleets or mercenaries on the overview (not yet available; shown disabled).");
        AddLine(column, "Unit map: cancel the selection; the order entries are not yet available (shown disabled).");
        AddLine(column, "Help: this page, the tooltip hints, and the about box.");

        AddSection(column, "Where the rules come from");
        AddLine(column, "The 'classical-faithful' ruleset is built from the original game's own data and the project's research reports.");
        AddLine(column, "The 'improved' preset is a designed variant of that ruleset, not a reconstruction.");
        AddLine(column, "The original game's own help file is not included or reproduced here.");

        var close = UiKit.MakeButton("Close", Close);
        close.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        column.AddChild(close);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Dismisses the page — what the Close button does, exposed for the headless check.</summary>
    public void Close() => Closed?.Invoke();

    private static void AddSection(VBoxContainer column, string text) =>
        column.AddChild(UiKit.MakeLabel(text, 16, UiKit.AccentColor));

    private static void AddLine(VBoxContainer column, string text)
    {
        var label = UiKit.MakeLabel(text, 14, UiKit.TextColor);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(label);
    }
}
