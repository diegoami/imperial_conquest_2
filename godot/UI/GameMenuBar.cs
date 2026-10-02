using Godot;

namespace IC2.Slice.UI;

/// <summary>
/// The main game screen's menu bar — the original's seven menus **File · Game · Strategy · Nations ·
/// Area map · Unit map · Help**, in the inventory's order, with the two submenus (Unit map →
/// Army/Fleet/City, Area map → Show mercenaries). Every item is one <see cref="GameCommandRow"/>;
/// pressing it runs the table's one handler, the same one the matching toolbar button runs
/// (<c>docs/investigations/original-ui-command-audit.md</c> §1; <c>docs/game-design.md</c> "User
/// interface" item 2).
/// </summary>
/// <remarks>
/// <para>
/// Built from <see cref="MenuButton"/>s rather than Godot's <c>MenuBar</c> node: a <c>MenuButton</c>'s
/// own <see cref="MenuButton.GetPopup"/> is a plain <see cref="PopupMenu"/> the headless
/// <c>godot/Checks/MenuBarCheck.cs</c> can read directly, with no native/global-menu path to vary by
/// platform (the same "assert the real tree, not a platform-specific decoration" reason the other
/// checks read real controls).
/// </para>
/// <para>
/// A row this task does not wire (<see cref="GameCommandRow.Wired"/> false) is added disabled, per
/// T100's Scope: "Entries a later task wires are shown disabled." A real click on a disabled item
/// issues nothing, and <see cref="PressItemForCheck"/> reproduces that by refusing to emit an item
/// that is disabled.
/// </para>
/// <para>
/// <strong>Show hints</strong> owns the check mark (<see cref="SetHintsEnabled"/>): the original's
/// <c>TPremierForm_ToggleHints</c> toggles the tooltips and the menu check mark together (audit §1.7),
/// and this bar sets each item's tooltip to its own caption when hints are on and clears it when off.
/// </para>
/// </remarks>
public partial class GameMenuBar : HBoxContainer
{
    /// <summary>The inventory's top-level menu order (audit §1's opening line), pinned here so a row's
    /// own position in <see cref="GameCommandTable.Rows"/> cannot reorder the menus silently.</summary>
    public static readonly IReadOnlyList<string> MenuOrder =
        new[] { "File", "Game", "Strategy", "Nations", "Area map", "Unit map", "Help" };

    public required GameCommandTable Table { get; init; }

    /// <summary>The menu titles in display order — the seven above. Exposed for the headless check.</summary>
    public IReadOnlyList<string> MenuTitles => _menuOrder;

    private readonly List<string> _menuOrder = new();

    private readonly Dictionary<string, MenuButton> _menuButtons = new(StringComparer.Ordinal);

    private readonly Dictionary<string, PopupMenu> _submenus = new(StringComparer.Ordinal);

    /// <summary>Each item's own popup, item index and item id — enough to read disabled/checked state
    /// and to emit the real <c>IdPressed</c> signal from <see cref="PressItemForCheck"/>.</summary>
    private readonly Dictionary<string, (PopupMenu Popup, int Index, int Id)> _items = new(StringComparer.Ordinal);

    private bool _hintsEnabled = true;

    /// <summary>Whether tooltips are currently on (default: on, as the original's checked state was —
    /// audit §1.7).</summary>
    public bool HintsEnabled => _hintsEnabled;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 2);

        foreach (var menuTitle in MenuOrder)
        {
            var button = new MenuButton { Text = menuTitle, SwitchOnHover = true };
            _menuButtons[menuTitle] = button;
            _menuOrder.Add(menuTitle);
            AddChild(button);

            var popup = button.GetPopup();
            BuildMenu(popup, GameCommandTable.Rows.Where(row => string.Equals(row.Menu, menuTitle, StringComparison.Ordinal)));
        }

        ApplyHints();
    }

    /// <summary>The <see cref="PopupMenu"/> behind <paramref name="menuTitle"/>, or <see langword="null"/> for an unknown title.</summary>
    public PopupMenu? MenuForCheck(string menuTitle) =>
        _menuButtons.TryGetValue(menuTitle, out var button) ? button.GetPopup() : null;

    /// <summary>
    /// The popup behind a submenu — <c>("Unit map", "Army")</c>, <c>("Area map", "Show mercenaries")</c>
    /// — or <see langword="null"/> when no row creates it. Exposed for
    /// <c>godot/Checks/MenuBarCheck.cs</c> (T100 Done-when 2).
    /// </summary>
    public PopupMenu? SubmenuForCheck(string menuTitle, string submenuTitle) =>
        _submenus.TryGetValue(SubmenuKey(menuTitle, submenuTitle), out var popup) ? popup : null;

    /// <summary>
    /// Presses the item that runs <paramref name="commandId"/>, the way a click does — it emits the
    /// item's real <see cref="PopupMenu.IdPressed"/> signal — and returns <see langword="false"/>
    /// without emitting anything when the item is disabled (an unwired row), so a check can prove a
    /// disabled entry issues no command (T100 Done-when 7).
    /// </summary>
    public bool PressItemForCheck(string commandId)
    {
        if (!_items.TryGetValue(commandId, out var item) || item.Popup.IsItemDisabled(item.Index))
        {
            return false;
        }

        item.Popup.EmitSignal(PopupMenu.SignalName.IdPressed, item.Id);
        return true;
    }

    /// <summary>
    /// Turns every item tooltip and every menu-button tooltip on or off, and moves the **Show hints**
    /// check mark with them (T100 Done-when 6; the original toggles all three together, audit §1.7).
    /// </summary>
    public void SetHintsEnabled(bool enabled)
    {
        if (_hintsEnabled == enabled)
        {
            return;
        }

        _hintsEnabled = enabled;
        ApplyHints();
    }

    private void BuildMenu(PopupMenu popup, IEnumerable<GameCommandRow> rows)
    {
        foreach (var row in rows)
        {
            if (row.Submenu is null)
            {
                AddItem(popup, row);
                continue;
            }

            var submenu = EnsureSubmenu(popup, row.Menu, row.Submenu);
            AddItem(submenu, row);
        }
    }

    private PopupMenu EnsureSubmenu(PopupMenu popup, string menuTitle, string submenuTitle)
    {
        var key = SubmenuKey(menuTitle, submenuTitle);
        if (!_submenus.TryGetValue(key, out var submenu))
        {
            // The submenu must be a child of the popup whose item names it (Godot's own
            // PopupMenu.add_submenu_item contract), named uniquely under that parent.
            submenu = new PopupMenu { Name = $"Sub_{menuTitle.Replace(' ', '_')}_{submenuTitle.Replace(' ', '_')}" };
            popup.AddChild(submenu);
            popup.AddSubmenuNodeItem(submenuTitle, submenu, (int)popup.ItemCount);
            _submenus[key] = submenu;
        }

        return submenu;
    }

    private void AddItem(PopupMenu popup, GameCommandRow row)
    {
        var index = popup.ItemCount;
        var id = index;
        popup.AddItem(row.Caption, id);
        popup.SetItemDisabled(index, !row.Wired);
        popup.SetItemChecked(index, string.Equals(row.Id, "help.show_hints", StringComparison.Ordinal) && _hintsEnabled);
        if (row.Shortcut is { } shortcut && AcceleratorFor(shortcut) is { } accelerator)
        {
            popup.SetItemAccelerator(index, accelerator);
        }

        var commandId = row.Id;
        var itemId = id;
        popup.IdPressed += pressedId =>
        {
            if (pressedId == itemId)
            {
                Table.TryInvoke(commandId);
            }
        };

        _items[row.Id] = (popup, index, id);
    }

    /// <summary>
    /// Godot's accelerator for a row's <see cref="GameCommandRow.Shortcut"/> string, or
    /// <see langword="null"/> when the table names no shortcut (or names one whose modifier this parser
    /// does not know). Only <c>Shift+X</c> is confirmed (audit §1.6); every other menu shortcut lives in
    /// the EXE's unread form stream (audit §5, gap 2), so this deliberately knows the one form rather
    /// than inventing a shortcut grammar from a stream nobody has read.
    /// </summary>
    public static Key? AcceleratorFor(string shortcut) => shortcut switch
    {
        "Shift+X" => (Key)((long)Key.X | (long)KeyModifierMask.MaskShift),
        _ => null,
    };

    private void ApplyHints()
    {
        foreach (var (popup, index, _) in _items.Values)
        {
            popup.SetItemTooltip(index, _hintsEnabled ? popup.GetItemText(index) : string.Empty);
        }

        foreach (var button in _menuButtons.Values)
        {
            button.TooltipText = _hintsEnabled ? button.Text : string.Empty;
        }

        // The check mark follows the same toggle (audit §1.7's TPremierForm_ToggleHints).
        if (_items.TryGetValue("help.show_hints", out var showHints))
        {
            showHints.Popup.SetItemChecked(showHints.Index, _hintsEnabled);
        }
    }

    private static string SubmenuKey(string menuTitle, string submenuTitle) =>
        $"{menuTitle}\u0000{submenuTitle}";
}
