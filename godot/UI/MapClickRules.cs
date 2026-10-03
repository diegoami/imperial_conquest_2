namespace IC2.Slice.UI;

/// <summary>Which mouse button a map click carried — the two the original's Unit map reads.</summary>
/// <remarks>
/// The user's decision of 2026-10-01 on the audit's §2.1: <strong>the left button selects and orders, the
/// right button only opens a unit list</strong> — "a right click opens a unit's list while a left click
/// selects it". In the audit's own reading of <c>TUnitMap_SelectUnit</c> the same select unit routine runs
/// after either button, so a right click could also attack; the decision, not the audit's reading, is what
/// this table implements (the task entry's own hazard note says the same).
/// </remarks>
public enum MapClickButton
{
    /// <summary>Selects an own unit or city, and gives orders.</summary>
    Left,

    /// <summary>Only opens the clicked unit's or city's list; never selects and never orders.</summary>
    Right,
}

/// <summary>Which kind of map entity a tile carries or a selection holds.</summary>
public enum MapEntityKind
{
    /// <summary>A city (a clicked tile's occupant, or the selected entity).</summary>
    City,

    /// <summary>An army.</summary>
    Army,

    /// <summary>A fleet.</summary>
    Fleet,
}

/// <summary>What one map click resolves to, before any engine command is submitted.</summary>
/// <remarks>
/// One row per kind, in the order the audit's §2.1 tables list them: the marker table
/// (<c>TUnitMap_SelectUnit</c>) first, the terrain table (<c>TUnitMap_CheckForMove</c>) second, and the
/// right-click rule (the user's decision of 2026-10-01) last. <strong>Every kind that carries an
/// <see cref="OrderLine"/> names the engine verb the original's own routine would run</strong>, and
/// nothing here decides whether the engine accepts it — the entry's embark hazard says exactly that:
/// the composed command is what these rules assert, never the engine's acceptance.
/// </remarks>
public enum MapClickOutcomeKind
{
    /// <summary>No row matched: nothing changes — neither the selection nor any order.</summary>
    Nothing,

    /// <summary>The clicked own entity becomes the selection and its details show.</summary>
    Select,

    /// <summary>The clicked entity's unit list shows; the selection and any pending order are untouched.</summary>
    ShowUnitList,

    /// <summary><c>move &lt;army&gt; &lt;x&gt; &lt;y&gt;</c> — the army walks toward the tile, partway when moves fall short.</summary>
    MoveArmy,

    /// <summary><c>move-fleet &lt;fleet&gt; &lt;x&gt; &lt;y&gt;</c> — the fleet's twin of <see cref="MoveArmy"/>.</summary>
    MoveFleet,

    /// <summary><c>attack-army &lt;attacker&gt; &lt;target&gt;</c> — a field battle.</summary>
    AttackArmy,

    /// <summary><c>besiege-city &lt;army&gt; &lt;city&gt;</c> — a siege.</summary>
    BesiegeCity,

    /// <summary><c>attack-fleet &lt;fleet&gt; &lt;target&gt;</c> — a naval battle.</summary>
    AttackFleet,

    /// <summary><c>embark-army &lt;army&gt; &lt;fleet&gt;</c> — the army boards the fleet, and the fleet becomes the selection.</summary>
    EmbarkArmy,

    /// <summary><c>disembark-army &lt;army&gt; &lt;x&gt; &lt;y&gt;</c> — the carried army lands on the clicked tile; the selection ends and the army's details show.</summary>
    DisembarkArmy,

    /// <summary>The selection is dropped and the clicked target's details show — the original's "cannot be attacked from here" row.</summary>
    DropSelectionAndShowTarget,
}

/// <summary>The city, army or fleet marker on the clicked tile, as the click decision needs it.</summary>
/// <param name="Kind">Which kind of entity stands on the tile.</param>
/// <param name="Id">The entity's own id, for the composed order and the panel.</param>
/// <param name="Nation">The entity's owner — compared against the active seat's nation for own-versus-foreign.</param>
/// <param name="CarriedArmyId">
/// For a fleet: the one army aboard, or <see langword="null"/>. The audit's embark row gates on the clicked
/// fleet "carrying no army", and the disembark row on the <em>selected</em> fleet carrying one; the field
/// serves both (a city or army target carries <see langword="null"/>).
/// </param>
public sealed record MapClickTarget(
    MapEntityKind Kind,
    string Id,
    string Nation,
    string? CarriedArmyId = null);

/// <summary>The active seat's own selected entity, as the click decision needs it.</summary>
/// <param name="Kind">Army or Fleet — a city is never the selected <em>orderer</em> (no order moves a city).</param>
/// <param name="Id">The selected entity's own id, for the composed order.</param>
/// <param name="Moves">
/// The selection's remaining moves — the audit's marker table gates every attack, besiege, embark and
/// naval attack on "moves ≥ 1", and the terrain table's stay-selected rule ends the selection at 0.
/// </param>
/// <param name="CarriedArmyId">For a selected fleet: the army aboard, or <see langword="null"/> — the disembark row's gate.</param>
public sealed record MapClickSelection(
    MapEntityKind Kind,
    string Id,
    int Moves,
    string? CarriedArmyId = null);

/// <summary>Everything the click decision reads, gathered once by the caller.</summary>
/// <param name="Button">Which mouse button the click carried.</param>
/// <param name="X">The clicked tile's column.</param>
/// <param name="Y">The clicked tile's row.</param>
/// <param name="TileIsLand">
/// Whether the clicked terrain is one an army may stand on (the audit's "land (code 2 or more)"): the
/// engine's own <c>TileType.PassableByArmies</c>, never a second terrain code table here.
/// </param>
/// <param name="TileIsSea">
/// Whether the clicked terrain is one a fleet may sail (the audit's "sea (0 or 1)"): the engine's own
/// <c>TileType.PassableByFleets</c>.
/// </param>
/// <param name="Target">The clicked tile's city, army or fleet marker, or <see langword="null"/> for open terrain.</param>
/// <param name="Selection">The active seat's selected army or fleet, or <see langword="null"/> when nothing is selected.</param>
/// <param name="Distance">
/// The Chebyshev distance from the selection's tile to the clicked tile — the metric the audit settles
/// (<c>attack-and-siege-are-adjacency-orders.md</c>). Read only when <paramref name="Selection"/> is not
/// <see langword="null"/>.
/// </param>
/// <param name="Relation">
/// The relation state between the active seat's nation and the clicked target's nation, in
/// <see cref="IC2.Engine.Model.RelationStateCodes"/>' own encoding. Read only when
/// <paramref name="Target"/> is not <see langword="null"/>.
/// </param>
/// <param name="WarCode">
/// The ruleset's own war state code (<c>Ruleset.Diplomacy.StateCodes.War</c>) — the one code the
/// confirmation rule compares <paramref name="Relation"/> against; every other relation (peace, trade,
/// alliance, or a negative cooldown) is "not at war" and asks the prompt.
/// </param>
/// <param name="ActiveNationId">The active seat's nation — the own-versus-foreign test's other side.</param>
public sealed record MapClickContext(
    MapClickButton Button,
    int X,
    int Y,
    bool TileIsLand,
    bool TileIsSea,
    MapClickTarget? Target,
    MapClickSelection? Selection,
    int Distance,
    int Relation,
    int WarCode,
    string ActiveNationId);

/// <summary>
/// What one resolved map click means: the <see cref="MapClickOutcomeKind">row</see>, the order the row
/// composes, the entity the panel ends up showing, and whether a war declaration must be confirmed first.
/// </summary>
/// <param name="Kind">Which row of the audit's §2.1 tables the click matched.</param>
/// <param name="FocusKind">
/// The entity the UI focuses after this outcome, where a row names one: the clicked target for
/// <see cref="MapClickOutcomeKind.Select"/> (it becomes the selection),
/// <see cref="MapClickOutcomeKind.ShowUnitList"/> (its list shows) and
/// <see cref="MapClickOutcomeKind.DropSelectionAndShowTarget"/> (its details show); the clicked fleet for
/// <see cref="MapClickOutcomeKind.EmbarkArmy"/> (it becomes the selection); the carried army for
/// <see cref="MapClickOutcomeKind.DisembarkArmy"/> (its details show). <see langword="null"/> otherwise.
/// </param>
/// <param name="FocusId"><see cref="FocusKind"/>'s own id, or <see langword="null"/> with it.</param>
/// <param name="OrderLine">
/// The composed engine command, for every order kind — <c>move</c>, <c>move-fleet</c>,
/// <c>attack-army</c>, <c>besiege-city</c>, <c>attack-fleet</c>, <c>embark-army</c>, <c>disembark-army</c>
/// — and <see langword="null"/> for every non-order kind.
/// </param>
/// <param name="KeepSelectionWhileMovesRemain">
/// The terrain table's stay-selected rule (<c>TUnitMap_MoveHumanArmy</c>): the moved unit stays selected
/// while it still has moves left, and the selection ends when they reach 0. Only
/// <see cref="MapClickOutcomeKind.MoveArmy"/> and <see cref="MapClickOutcomeKind.MoveFleet"/> carry it —
/// whether the moves actually fell to 0 is known only after the engine runs the order, so the caller
/// re-selects from the post-order state, never from here.
/// </param>
/// <param name="RequiresWarConfirmation">
/// Whether this attack, besiege or naval attack is against a nation the active seat is <em>not</em> at war
/// with — every relation except the war code itself — and so must pass the original's
/// "Are you sure you want to attack this … ?" prompt before its order is submitted. Always
/// <see langword="false"/> for the non-attack kinds.
/// </param>
/// <param name="ConfirmationText">
/// The prompt's exact text when <paramref name="RequiresWarConfirmation"/> is true — the original's own
/// spacing included (a space before the question mark) — and <see langword="null"/> otherwise.
/// </param>
public sealed record MapClickOutcome(
    MapClickOutcomeKind Kind,
    MapEntityKind? FocusKind = null,
    string? FocusId = null,
    string? OrderLine = null,
    bool KeepSelectionWhileMovesRemain = false,
    bool RequiresWarConfirmation = false,
    string? ConfirmationText = null)
{
    /// <summary>The "no row matched" outcome: nothing changes at all.</summary>
    public static MapClickOutcome Nothing() => new(MapClickOutcomeKind.Nothing);

    /// <summary>The "it becomes the selection" outcome for an own entity the left button clicked.</summary>
    public static MapClickOutcome Select(MapClickTarget target) =>
        new(MapClickOutcomeKind.Select, target.Kind, target.Id);

    /// <summary>The right button's one outcome: the clicked entity's unit list, and nothing else changes.</summary>
    public static MapClickOutcome ShowUnitList(MapClickTarget target) =>
        new(MapClickOutcomeKind.ShowUnitList, target.Kind, target.Id);

    /// <summary>The "cannot be attacked from here" outcome: the selection drops, the target's details show.</summary>
    public static MapClickOutcome DropSelectionAndShow(MapClickTarget target) =>
        new(MapClickOutcomeKind.DropSelectionAndShowTarget, target.Kind, target.Id);

    /// <summary>The terrain table's army row: <c>move</c>, staying selected while any moves remain.</summary>
    public static MapClickOutcome MoveArmy(string armyId, int x, int y) =>
        new(MapClickOutcomeKind.MoveArmy, OrderLine: $"move {armyId} {x} {y}", KeepSelectionWhileMovesRemain: true);

    /// <summary>The terrain table's fleet row: <c>move-fleet</c>, the same stay-selected rule.</summary>
    public static MapClickOutcome MoveFleet(string fleetId, int x, int y) =>
        new(MapClickOutcomeKind.MoveFleet, OrderLine: $"move-fleet {fleetId} {x} {y}", KeepSelectionWhileMovesRemain: true);

    /// <summary>The terrain table's disembark row: the carried army lands on the clicked tile, and its details show.</summary>
    public static MapClickOutcome Disembark(string carriedArmyId, int x, int y) =>
        new(MapClickOutcomeKind.DisembarkArmy, MapEntityKind.Army, carriedArmyId, $"disembark-army {carriedArmyId} {x} {y}");

    /// <summary>The marker table's embark row: the army boards, and the clicked fleet becomes the selection.</summary>
    public static MapClickOutcome Embark(string armyId, MapClickTarget clickedFleet) =>
        new(MapClickOutcomeKind.EmbarkArmy, clickedFleet.Kind, clickedFleet.Id, $"embark-army {armyId} {clickedFleet.Id}");

    /// <summary>An attack, besiege or naval attack — composed with its own order, and confirmed first when not at war.</summary>
    public static MapClickOutcome Attack(
        MapClickOutcomeKind kind,
        string orderLine,
        string targetNoun,
        bool requiresWarConfirmation) =>
        new(
            kind,
            OrderLine: orderLine,
            RequiresWarConfirmation: requiresWarConfirmation,
            ConfirmationText: requiresWarConfirmation ? $"Are you sure you want to attack this {targetNoun} ?" : null);
}

/// <summary>
/// The Unit map's click decision table, in one pure, Godot-free place — the T99 task entry's own
/// requirement, so that a correction from the original exploration in flight (requested 2026-10-01) lands
/// here and nowhere else.
/// </summary>
/// <remarks>
/// <para>
/// Every row is the audit's §2.1 (<c>docs/investigations/original-ui-command-audit.md</c>), read from
/// <c>TUnitMap_UnitMapClick</c>, <c>TUnitMap_SelectUnit</c> and <c>TUnitMap_CheckForMove</c>
/// <strong>[derived: code]</strong>, with the war-confirmation behaviour confirmed by
/// <c>decompiled-diplomacy-peace-terms-and-instant-battles.md</c>, and the two-button split the user's
/// decision of 2026-10-01. The engine's own gates (adjacency again, passability again, the fleet-capacity
/// rule, "a fleet docked at its own city") all stay in the engine: this table only decides
/// <em>which order the click means</em>, and the composed line is what its tests assert.
/// </para>
/// <para>
/// <strong>The table, in the order the audit lists it.</strong> A click on a marker (city, army or fleet):
/// the right button shows the unit list, whatever is selected; the left button on an <em>own</em> marker
/// makes it the selection — except the one embark row, an adjacent own empty fleet clicked by an army with
/// moves left, which boards the army and selects the fleet; the left button on a <em>foreign</em> marker
/// attacks it when the selection is the matching kind with moves left at distance exactly 1 — army
/// attacks army, army besieges city, fleet attacks fleet — and otherwise drops the selection and shows
/// the target. A click on open terrain: an army moves to land, a fleet sails to sea (both at any
/// distance; the engine walks partway), a fleet carrying an army lands it on an adjacent land tile, and
/// every other pair does nothing at all.
/// </para>
/// <para>
/// <strong>A fleet never besieges a city or attacks an army, and no order moves an army onto a city
/// tile</strong> — the audit's own two "consequences, all [derived: code]": the city and army branches
/// test only the selected <em>army</em>, and clicking an own city with an army selected deselects the
/// army and selects the city, exactly as clicking any own marker does.
/// </para>
/// </remarks>
public static class MapClickRules
{
    /// <summary>The one entry point: resolves one click to its outcome row.</summary>
    public static MapClickOutcome Resolve(MapClickContext click)
    {
        ArgumentNullException.ThrowIfNull(click);

        // The user's two-button decision: the right button opens the clicked marker's unit list — any
        // marker, own or foreign, whatever is selected — and changes neither the selection nor any order.
        // The audit's own reading runs SelectUnit after either button; the decision overrides that here.
        if (click.Target is { } target)
        {
            if (click.Button == MapClickButton.Right)
            {
                return MapClickOutcome.ShowUnitList(target);
            }

            return click.Button == MapClickButton.Left
                ? ResolveLeftClickOnMarker(click, target)
                : MapClickOutcome.Nothing();
        }

        if (click.Button != MapClickButton.Left || click.Selection is not { } selection)
        {
            // A right click on open terrain opens nothing (there is no list to show), and a terrain
            // click with nothing selected matches no row of TUnitMap_CheckForMove either.
            return MapClickOutcome.Nothing();
        }

        // TUnitMap_CheckForMove's three rows, in the audit's order: army to land, fleet to sea, then the
        // carrying fleet's land-at-distance-1 disembark.
        if (selection.Kind == MapEntityKind.Army)
        {
            return click.TileIsLand
                ? MapClickOutcome.MoveArmy(selection.Id, click.X, click.Y)
                : MapClickOutcome.Nothing();
        }

        if (click.TileIsSea)
        {
            return MapClickOutcome.MoveFleet(selection.Id, click.X, click.Y);
        }

        return click.TileIsLand && click.Distance == 1 && selection.CarriedArmyId is { } carried
            ? MapClickOutcome.Disembark(carried, click.X, click.Y)
            : MapClickOutcome.Nothing();
    }

    /// <summary>
    /// <c>TUnitMap_SelectUnit</c>'s own table: the left button on a city, army or fleet marker, with a
    /// selection or without one.
    /// </summary>
    private static MapClickOutcome ResolveLeftClickOnMarker(MapClickContext click, MapClickTarget target)
    {
        var own = string.Equals(target.Nation, click.ActiveNationId, StringComparison.Ordinal);
        if (own)
        {
            // The embark row — an own army with moves ≥ 1 clicking an own fleet carrying no army at
            // Chebyshev distance exactly 1 — boards the army and makes the fleet the selection. The
            // audit's capacity gate ("ships ≥ troops / 500") stays in the engine's own embark command;
            // this table only composes the order. Every other own marker (city, army, or a fleet this
            // row does not fit) simply becomes the selection — including the own-city-with-army-selected
            // row the audit spells out: the city is selected, and the army gets no order.
            if (click.Selection is { Kind: MapEntityKind.Army, Moves: >= 1 } army
                && target.Kind == MapEntityKind.Fleet
                && target.CarriedArmyId is null
                && click.Distance == 1)
            {
                return MapClickOutcome.Embark(army.Id, target);
            }

            return MapClickOutcome.Select(target);
        }

        // The foreign half: only the matching selection kind attacks, and only with moves ≥ 1 at
        // Chebyshev distance exactly 1. An army attacks an army and besieges a city; a fleet attacks only
        // a fleet — never a city, never an army. Everything else, including a foreign marker with no
        // selection at all, is the "cannot be attacked from here" row: the selection drops and the
        // target's details show.
        return (click.Selection?.Kind, target.Kind, click.Selection?.Moves, click.Distance) switch
        {
            (MapEntityKind.Army, MapEntityKind.Army, >= 1, 1) => ConfirmedAttack(
                MapClickOutcomeKind.AttackArmy, click, target, "army", $"attack-army {click.Selection!.Id} {target.Id}"),
            (MapEntityKind.Army, MapEntityKind.City, >= 1, 1) => ConfirmedAttack(
                MapClickOutcomeKind.BesiegeCity, click, target, "city", $"besiege-city {click.Selection!.Id} {target.Id}"),
            (MapEntityKind.Fleet, MapEntityKind.Fleet, >= 1, 1) => ConfirmedAttack(
                MapClickOutcomeKind.AttackFleet, click, target, "fleet", $"attack-fleet {click.Selection!.Id} {target.Id}"),
            _ => MapClickOutcome.DropSelectionAndShow(target),
        };
    }

    /// <summary>
    /// The confirmed prompts' one rule: an attack against a nation the active seat is not at war with —
    /// any relation except the war code — carries the original's own question (its spacing included),
    /// and the war code itself skips the prompt. <c>GameSession</c> composes the declaration of war in
    /// front of the order when it is submitted (<c>ComposeDeclareWarIfNeeded</c>); the UI never submits a
    /// second <c>declare-war</c>.
    /// </summary>
    private static MapClickOutcome ConfirmedAttack(
        MapClickOutcomeKind kind,
        MapClickContext click,
        MapClickTarget target,
        string targetNoun,
        string orderLine) =>
        MapClickOutcome.Attack(kind, orderLine, targetNoun, click.Relation != click.WarCode);
}
