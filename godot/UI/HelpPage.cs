using Godot;

namespace IC2.Slice.UI;

/// <summary>
/// <c>docs/tasks/T100.md</c> Scope, "Help topics": a short in-game help page — <strong>[designed]</strong>
/// new text, because the original's help file (<c>Imperial Conquest 2.HLP</c>) is its own content and
/// cannot ship. Two tabs: #1289's "How to play" (<see cref="HowToPlay"/>: the goal, a turn, moving, attacking,
/// battles, supply and money, diplomacy, how a reign ends) and the controls, one line per menu, and where the
/// rules come from.
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

    /// <summary>#1289: the game's ruleset, for the "How to play" goal line; <see langword="null"/> on the main menu.</summary>
    public IC2.Engine.Model.Ruleset? Ruleset { get; init; }

    /// <summary>#1289: the two tabs, "How to play" first; exposed for the headless check.</summary>
    public TabContainer Tabs { get; private set; } = null!;

    public override void _Ready()
    {
        var backdrop = new ColorRect { Color = AppTheme.Scrim, MouseFilter = MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.MakePanel(UiKit.PanelColorRaised);
        panel.CustomMinimumSize = new Vector2(720, 0);
        center.AddChild(panel);

        var outer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        outer.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        panel.AddChild(outer);

        outer.AddChild(UiKit.MakeLabel("Help", AppTheme.Header, UiKit.AccentColor));

        // #1289: "How to play" first (a new player's question), then the original controls-and-menus page.
        Tabs = new TabContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        outer.AddChild(Tabs);
        var tabHeight = Mathf.Max(260f, GetViewportRect().Size.Y * 0.62f);

        var howTo = Page(Tabs, "How to play", tabHeight);
        foreach (var section in HowToPlay.Sections(Ruleset))
        {
            AddSection(howTo, section.Title);
            foreach (var line in section.Lines)
            {
                AddLine(howTo, line);
            }
        }

        var column = Page(Tabs, "Controls and menus", tabHeight);

        AddSection(column, "Controls");
        AddLine(column, "Left click: select your army, fleet or city; click a target to give the selected unit an order.");
        AddLine(column, "Right click: show the clicked city's, army's or fleet's unit list.");
        AddLine(column, "Mouse wheel: zoom the map. Drag with the left button: pan. Hover a marker to see what it is.");
        AddLine(column, "Esc or Shift+X: cancel the current selection. Esc also closes dialogs and reports.");

        AddSection(column, "Menus");
        AddLine(column, "File: start a new game, open or save one, or leave this game.");
        AddLine(column, "Game: end the current turn, or open Settings.");
        AddLine(column, "Strategy: news, international relations, taxation, the balance sheet, unit recruitment and fleet building.");
        AddLine(column, "Nations: choose a nation to view its status, or view all nations.");
        AddLine(column, "Area map: show cities, capitals, armies, fleets or mercenaries on the overview, or find a city.");
        AddLine(column, "Unit map: give army, fleet or city orders, or cancel the selection.");
        AddLine(column, "Help: this page, the tooltip hints, and the about box.");

        AddSection(column, "Where the rules come from");
        AddLine(column, "The 'classical-faithful' ruleset is built from the original game's own data and the project's research reports.");
        AddLine(column, "The 'improved' preset is a designed variant of that ruleset, not a reconstruction.");
        AddLine(column, "The original game's own help file is not included or reproduced here.");

        var close = UiKit.MakeButton("Close", Close);
        close.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        outer.AddChild(close);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    /// <summary>Dismisses the page — what the Close button does, exposed for the headless check.</summary>
    public void Close() => Closed?.Invoke();

    /// <summary>One scrolling tab page; returns its column.</summary>
    private static VBoxContainer Page(TabContainer tabs, string title, float height)
    {
        var scroll = new ScrollContainer
        {
            Name = title,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, height),
        };
        tabs.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        scroll.AddChild(column);
        return column;
    }

    private static void AddSection(VBoxContainer column, string text) =>
        column.AddChild(UiKit.MakeLabel(text, AppTheme.Header, UiKit.AccentColor));

    private static void AddLine(VBoxContainer column, string text)
    {
        var label = UiKit.MakeLabel(text, AppTheme.Body, UiKit.TextColor);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(label);
    }
}
