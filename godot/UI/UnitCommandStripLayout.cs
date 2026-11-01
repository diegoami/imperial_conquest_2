namespace IC2.Slice.UI;

/// <summary>
/// What the Unit-map command strip shows for the current map selection — the same groups the original's
/// <c>TUnitMap_AllButtonsOff</c> / <c>ArmyButtonsOn</c> / <c>FleetButtonsOn</c> / <c>CityButtonsOn</c>
/// switch on <c>[derived: code; docs/investigations/original-ui-command-audit.md §3.3]</c>.
/// </summary>
public enum UnitStripSelection
{
    /// <summary>No selection, or a foreign unit — the strip shows nothing.</summary>
    None,

    /// <summary>An own army: original Army buttons, Quick Split, Set travel target and Cancel selection.</summary>
    OwnArmy,

    /// <summary>An own fleet: original Fleet buttons, Quick Split and Cancel selection.</summary>
    OwnFleet,

    /// <summary>An own city: the 1 City button plus Cancel selection.</summary>
    OwnCity,

    /// <summary>An own fleet carrying an army: Fleet buttons, then Army buttons, plus Cancel.</summary>
    OwnFleetCarryingArmy,
}

/// <summary>
/// Which buttons the Unit-map command strip shows, in which order, for a selection — Godot-free, so the
/// test project can pin the exact set without the Godot SDK. The Godot control
/// (<see cref="UnitCommandStrip"/>) builds one button per id and runs the row's own table handler, so the
/// strip and the menu share one handler (<c>docs/game-design.md</c> "User interface" item 2).
/// </summary>
/// <remarks>
/// <para>
/// The shape is <c>[derived: code, TUnitMap_AllButtonsOff, ArmyButtonsOn, FleetButtonsOn,
/// CityButtonsOn]</c>: <strong>7 original army buttons</strong>, plus Quick Split and Set travel target,
/// <strong>6 fleet buttons</strong> and
/// <strong>1 city button</strong>, one per Unit-map submenu entry; only the selected unit's group is
/// shown; and a fleet carrying an army shows both groups, the fleet buttons first. Nothing is shown for
/// a foreign unit. The extra trailing button is <strong>Cancel selection</strong>
/// <c>[designed: audit §3.3; Wine candidate 2026-10-02-fleet-orders-live.md]</c>.
/// </para>
/// <para>
/// The ids are the <see cref="GameCommandTable"/> rows' ids; the test project compiles this file
/// directly, so it must never reference <c>Godot.*</c>.
/// </para>
/// </remarks>
public static class UnitCommandStripLayout
{
    /// <summary>The original Army rows and the designed Quick Split and Set travel target actions.</summary>
    public static readonly IReadOnlyList<string> ArmyCommands = new[]
    {
        "unit_map.army_supply",
        "unit_map.army_recruit_mercenaries",
        "unit_map.army_transfer_unit",
        "unit_map.army_split",
        "unit_map.army_split_two",
        "unit_map.army_set_target",
        "unit_map.army_join",
        "unit_map.army_change_units",
        "unit_map.army_disband",
    };

    /// <summary>Original fleet actions plus the owner-requested Split in Two convenience.</summary>
    public static readonly IReadOnlyList<string> FleetCommands = new[]
    {
        "unit_map.fleet_supply",
        "unit_map.fleet_repair",
        "unit_map.fleet_transfer_ships",
        "unit_map.fleet_split",
        "unit_map.fleet_split_two",
        "unit_map.fleet_join",
        "unit_map.fleet_scuttle",
    };

    /// <summary>The Unit map → City submenu's 1 row (audit §1.6).</summary>
    public static readonly IReadOnlyList<string> CityCommands = new[]
    {
        "unit_map.city_fortify",
    };

    /// <summary>The trailing Cancel selection button <c>[designed: audit §3.3]</c>.</summary>
    public const string CancelCommandId = "unit_map.cancel_selection";

    /// <summary>
    /// The command ids the strip shows for <paramref name="selection"/>, in order — the selected unit's
    /// group (or both groups for a fleet carrying an army), then Cancel selection. Empty for
    /// <see cref="UnitStripSelection.None"/>.
    /// </summary>
    public static IReadOnlyList<string> CommandsFor(UnitStripSelection selection) => selection switch
    {
        UnitStripSelection.OwnArmy => ArmyCommands.Append(CancelCommandId).ToArray(),
        UnitStripSelection.OwnFleet => FleetCommands.Append(CancelCommandId).ToArray(),
        UnitStripSelection.OwnCity => CityCommands.Append(CancelCommandId).ToArray(),
        UnitStripSelection.OwnFleetCarryingArmy =>
            FleetCommands.Concat(ArmyCommands).Append(CancelCommandId).ToArray(),
        _ => Array.Empty<string>(),
    };
}
