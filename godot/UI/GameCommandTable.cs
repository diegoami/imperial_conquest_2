namespace IC2.Slice.UI;

/// <summary>
/// One row of <see cref="GameCommandTable"/> — the original's one command, wherever the UI offers it.
/// </summary>
/// <remarks>
/// The row carries <em>no</em> handler: this file is deliberately Godot-free (linked into
/// <c>tests/IC2.Engine.Tests</c> through one <c>&lt;Compile Include&gt;</c> line), and the handler that
/// submits to the engine lives in <see cref="MainGameScreen"/>. Menu, submenu and caption are the
/// audit's own inventory names (<c>docs/investigations/original-ui-command-audit.md</c> §1), not
/// re-worded, so a menu entry and its toolbar icon can both look the row up and run the same code
/// (the audit's §1 opening: <c>StrategicDecision</c> pairs one menu item with one speed button).
/// </remarks>
/// <param name="Id">The stable command id, dotted by menu group (<c>file.save</c>, <c>unit_map.army_split</c>).</param>
/// <param name="Menu">The inventory's top-level menu this entry lives under.</param>
/// <param name="Submenu">
/// The inventory's submenu, or <see langword="null"/> for a direct entry. <c>Unit map → Army/Fleet/City</c>
/// (audit §1.6) and <c>Area map → Show mercenaries</c> (audit §1.5) are the only two.
/// </param>
/// <param name="Caption">The inventory's own caption, exactly (<c>End turn</c>, not <c>End Turn</c>).</param>
/// <param name="IconKey">
/// The command's <c>ui.command.&lt;id&gt;.icon</c> key from T101, or <see langword="null"/> when the
/// command has no icon (the File commands New/Save As/Close, the 17 nations, and the three Help
/// entries — T101 grants keys only to the toolbar's pictorial buttons).
/// </param>
/// <param name="Shortcut">
/// The observed keyboard shortcut, or <see langword="null"/>. Only <c>Shift+X</c> is confirmed (audit
/// §1.6); every other menu shortcut lives in the EXE's unread form stream (audit §5, gap 2) and is
/// deliberately left unset rather than invented (T100's hazard note).
/// </param>
/// <param name="Wired">Whether this task wires the row to a working handler; an unwired row is shown disabled.</param>
public sealed record GameCommandRow(
    string Id,
    string Menu,
    string? Submenu,
    string Caption,
    string? IconKey,
    string? Shortcut,
    bool Wired);

/// <summary>
/// The one table of commands the main game screen's menu bar and main toolbar are built from —
/// <c>docs/game-design.md</c> §"User interface" item 2: "One command table drives the menus and the
/// toolbars, so a menu entry and its toolbar icon run the same code". It lists every entry of the UI
/// command audit's §1 except <strong>Game → New player, New nation and Abdicate</strong>, the three
/// seat commands the user decided to leave out for now (T100's Scope).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Godot-free by construction</strong>, like <c>RulesetPresets</c>, <c>CommandOutcomeText</c>
/// and <c>MapClickRules</c> before it: the static <see cref="Rows"/> and the per-row metadata are
/// asserted by a plain xunit test in CI (<c>tests/IC2.Engine.Tests/Ui/GameCommandTableTests.cs</c>),
/// and the Godot layer binds the handlers and picks the enabled/disabled state. The 16 nation rows
/// are the shipped <c>classical-mediterranean</c> world's own ids and names in the world's order —
/// the original's own 16 coloured speed buttons (audit §1.4, §3.1). A custom world with a different
/// nation set would need its own table; that is a later task's concern, not silently half-supported
/// here.
/// </para>
/// <para>
/// The icon keys are the exact strings T101 names, written out as literals here so neither task has to
/// wait for the other: <c>ui.command.&lt;id&gt;.icon</c> for the 36 pictorial buttons (9 main-toolbar,
/// 12 Area-map, 15 Unit-map). T101's <see cref="IC2.Engine.Assets.AssetKeys"/> group is what makes
/// them resolvable; until it merges, <c>GameCommandTableTests</c>'s AllKeys assertion fails by design
/// (T100's brief records this).
/// </para>
/// </remarks>
public sealed class GameCommandTable
{
    /// <summary>
    /// The shipped <c>classical-mediterranean</c> world's 16 nations in its own order (audit §1.4's
    /// "Rome … Thracia", and <c>rome-city-recruitment-and-nations.md</c>'s left-to-right pairing of the
    /// coloured speed buttons with the menu entries). Kept here rather than read from the live world at
    /// menu-build time so the command table itself is static and testable; the toolbar still draws each
    /// swatch from the world's own colour pair. Declared before <see cref="Rows"/> so it is initialised
    /// before <see cref="BuildRows"/> reads it (static field initialisers run in textual order).
    /// </summary>
    private static readonly (string Id, string Name)[] NationRows =
    {
        ("rome", "Rome"),
        ("carthage", "Carthage"),
        ("seleucid", "Seleucid"),
        ("ptolemaic", "Ptolemaic"),
        ("macedonia", "Macedonia"),
        ("numidia", "Numidia"),
        ("gaul", "Gaul"),
        ("greece", "Greece"),
        ("celtiberia", "Celtiberia"),
        ("illyria", "Illyria"),
        ("dacia", "Dacia"),
        ("bithynia", "Bithynia"),
        ("galatia", "Galatia"),
        ("armenia", "Armenia"),
        ("media", "Media"),
        ("thracia", "Thracia"),
    };

    /// <summary>
    /// Every row, in menu order: File · Game · Strategy · Nations · Area map · Unit map · Help — the
    /// audit's inventory order (audit §1's opening line). The toolbar's own cross-group order is
    /// separate and [open] (see <see cref="CommandToolbar"/>). Declared after <see cref="NationRows"/>
    /// because a static field initialiser runs in textual order and <see cref="BuildRows"/> reads it.
    /// </summary>
    public static IReadOnlyList<GameCommandRow> Rows { get; } = BuildRows();

    /// <summary>The row with <paramref name="id"/>, or <see langword="null"/> when the id is not a command.</summary>
    public static GameCommandRow? RowById(string id) =>
        Rows.FirstOrDefault(row => string.Equals(row.Id, id, StringComparison.Ordinal));

    private readonly Dictionary<string, Action> _handlers = new(StringComparer.Ordinal);

    /// <summary>
    /// How many times <see cref="TryInvoke"/> has invoked a bound handler — the counter a headless check
    /// reads to prove a menu item and a toolbar button reach the same handler
    /// (<c>godot/Checks/MenuBarCheck.cs</c>, T100 Done-when 3).
    /// </summary>
    public int IssuedCount { get; private set; }

    /// <summary>
    /// Binds <paramref name="id"/>'s handler. Only <see cref="GameCommandRow.Wired"/> rows are bound by
    /// <see cref="MainGameScreen"/>; binding an unwired row is a programming error the tests catch.
    /// </summary>
    public void Bind(string id, Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (RowById(id) is not { } row)
        {
            throw new ArgumentException($"'{id}' is not a row of {nameof(GameCommandTable)}.", nameof(id));
        }

        if (!row.Wired)
        {
            throw new ArgumentException($"'{id}' is not wired by T100; do not bind a handler to it.", nameof(id));
        }

        _handlers[id] = handler;
    }

    /// <summary>Whether a handler is bound for <paramref name="id"/> — false for every unwired row.</summary>
    public bool IsBound(string id) => _handlers.ContainsKey(id);

    /// <summary>
    /// Runs <paramref name="id"/>'s handler when it is bound, and counts the invocation. Returns
    /// <see langword="false"/> for an unwired (disabled) row, issuing nothing — the guard the disabled
    /// entries' Done-when line asserts (<c>MenuBarCheck</c>, T100 Done-when 7).
    /// </summary>
    public bool TryInvoke(string id)
    {
        if (!_handlers.TryGetValue(id, out var handler))
        {
            return false;
        }

        IssuedCount++;
        handler();
        return true;
    }

    private static IReadOnlyList<GameCommandRow> BuildRows()
    {
        var rows = new List<GameCommandRow>
        {
            // ---- File (audit §1.1) ----
            new("file.new", "File", null, "New", null, null, Wired: true),
            new("file.open", "File", null, "Open", "ui.command.open.icon", null, Wired: true),
            new("file.save", "File", null, "Save", "ui.command.save.icon", null, Wired: true),
            new("file.save_as", "File", null, "Save As", null, null, Wired: true),
            new("file.close", "File", null, "Close", null, null, Wired: true),

            // ---- Game (audit §1.2); the three seat commands are out (T100 Scope) ----
            new("game.end_turn", "Game", null, "End turn", "ui.command.end_turn.icon", null, Wired: true),

            // ---- Strategy (audit §1.3); only News and International relations are wired here ----
            new("strategy.news", "Strategy", null, "News", "ui.command.news.icon", null, Wired: true),
            new("strategy.relations", "Strategy", null, "International relations", "ui.command.relations.icon", null, Wired: true),
            new("strategy.taxation", "Strategy", null, "Taxation", "ui.command.taxation.icon", null, Wired: false),
            new("strategy.balance_sheet", "Strategy", null, "Balance sheet", "ui.command.balance_sheet.icon", null, Wired: false),
            new("strategy.recruit_unit", "Strategy", null, "Recruit unit", "ui.command.recruit_unit.icon", null, Wired: false),
            new("strategy.build_fleet", "Strategy", null, "Build fleet", "ui.command.build_fleet.icon", null, Wired: false),
        };

        // ---- Nations (audit §1.4): the original's own 16, in the shipped world's order, plus All nations ----
        foreach (var (id, name) in NationRows)
        {
            rows.Add(new GameCommandRow($"nations.{id}", "Nations", null, name, null, null, Wired: false));
        }

        rows.Add(new GameCommandRow("nations.all", "Nations", null, "All nations", null, null, Wired: false));

        // ---- Area map (audit §1.5); none is wired here ----
        rows.AddRange(new[]
        {
            new GameCommandRow("area_map.show_cities", "Area map", null, "Show cities", "ui.command.show_cities.icon", null, Wired: false),
            new GameCommandRow("area_map.show_capital", "Area map", null, "Show capital", "ui.command.show_capital.icon", null, Wired: false),
            new GameCommandRow("area_map.show_armies", "Area map", null, "Show armies", "ui.command.show_armies.icon", null, Wired: false),
            new GameCommandRow("area_map.show_fleets", "Area map", null, "Show fleets", "ui.command.show_fleets.icon", null, Wired: false),
            new GameCommandRow("area_map.show_all", "Area map", null, "Show all", "ui.command.show_all.icon", null, Wired: false),
            new GameCommandRow("area_map.show_mercs_light_infantry", "Area map", "Show mercenaries", "Light infantry", "ui.command.show_mercs_light_infantry.icon", null, Wired: false),
            new GameCommandRow("area_map.show_mercs_heavy_infantry", "Area map", "Show mercenaries", "Heavy infantry", "ui.command.show_mercs_heavy_infantry.icon", null, Wired: false),
            new GameCommandRow("area_map.show_mercs_archers", "Area map", "Show mercenaries", "Archers", "ui.command.show_mercs_archers.icon", null, Wired: false),
            new GameCommandRow("area_map.show_mercs_light_cavalry", "Area map", "Show mercenaries", "Light cavalry", "ui.command.show_mercs_light_cavalry.icon", null, Wired: false),
            new GameCommandRow("area_map.show_mercs_heavy_cavalry", "Area map", "Show mercenaries", "Heavy cavalry", "ui.command.show_mercs_heavy_cavalry.icon", null, Wired: false),
            new GameCommandRow("area_map.show_mercs_all", "Area map", "Show mercenaries", "All mercenaries", "ui.command.show_mercs_all.icon", null, Wired: false),
            new GameCommandRow("area_map.find_city", "Area map", null, "Find a city", "ui.command.find_city.icon", null, Wired: false),
        });

        // ---- Unit map (audit §1.6); only Cancel selection is wired here ----
        rows.AddRange(new[]
        {
            new GameCommandRow("unit_map.army_supply", "Unit map", "Army", "Supply army", "ui.command.army_supply.icon", null, Wired: false),
            new GameCommandRow("unit_map.army_recruit_mercenaries", "Unit map", "Army", "Recruit mercenaries", "ui.command.army_recruit_mercenaries.icon", null, Wired: false),
            new GameCommandRow("unit_map.army_transfer_unit", "Unit map", "Army", "Transfer unit", "ui.command.army_transfer_unit.icon", null, Wired: false),
            new GameCommandRow("unit_map.army_split", "Unit map", "Army", "Split army", "ui.command.army_split.icon", null, Wired: false),
            new GameCommandRow("unit_map.army_join", "Unit map", "Army", "Join armies", "ui.command.army_join.icon", null, Wired: false),
            new GameCommandRow("unit_map.army_change_units", "Unit map", "Army", "Change units", "ui.command.army_change_units.icon", null, Wired: false),
            new GameCommandRow("unit_map.army_disband", "Unit map", "Army", "Disband army", "ui.command.army_disband.icon", null, Wired: false),
            new GameCommandRow("unit_map.fleet_supply", "Unit map", "Fleet", "Supply fleet", "ui.command.fleet_supply.icon", null, Wired: false),
            new GameCommandRow("unit_map.fleet_repair", "Unit map", "Fleet", "Repair fleet", "ui.command.fleet_repair.icon", null, Wired: false),
            new GameCommandRow("unit_map.fleet_transfer_ships", "Unit map", "Fleet", "Transfer ships", "ui.command.fleet_transfer_ships.icon", null, Wired: false),
            new GameCommandRow("unit_map.fleet_split", "Unit map", "Fleet", "Split fleet", "ui.command.fleet_split.icon", null, Wired: false),
            new GameCommandRow("unit_map.fleet_join", "Unit map", "Fleet", "Join fleets", "ui.command.fleet_join.icon", null, Wired: false),
            new GameCommandRow("unit_map.fleet_scuttle", "Unit map", "Fleet", "Scuttle fleet", "ui.command.fleet_scuttle.icon", null, Wired: false),
            new GameCommandRow("unit_map.city_fortify", "Unit map", "City", "Fortify city", "ui.command.city_fortify.icon", null, Wired: false),
            new GameCommandRow("unit_map.cancel_selection", "Unit map", null, "Cancel selection", "ui.command.cancel_selection.icon", "Shift+X", Wired: true),
        });

        // ---- Help (audit §1.7); all three are wired here ----
        rows.AddRange(new[]
        {
            new GameCommandRow("help.topics", "Help", null, "Help topics", null, null, Wired: true),
            new GameCommandRow("help.show_hints", "Help", null, "Show hints", null, null, Wired: true),
            new GameCommandRow("help.about", "Help", null, "About Imperial Conquest", null, null, Wired: true),
        });

        return rows;
    }
}
