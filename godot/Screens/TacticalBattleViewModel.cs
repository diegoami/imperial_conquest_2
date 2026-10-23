using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Assets;
using IC2.Slice.UI;

namespace IC2.Slice.Screens;

/// <summary>What the battle screen calls things: the nations' and unit types' display names and the board's size.</summary>
/// <param name="AttackerNationName">The attacker's nation name.</param>
/// <param name="DefenderNationName">The defender's nation name.</param>
/// <param name="UnitTypeNames">The unit types' display names, in ruleset order (a slot's <see cref="TacticalSlot.Type"/> indexes it).</param>
/// <param name="BoardWidth">The board's columns (<c>combat.tactical.boardWidth</c>).</param>
/// <param name="BoardHeight">The board's rows (<c>combat.tactical.boardHeight</c>).</param>
public sealed record BattleDisplay(
    string AttackerNationName,
    string DefenderNationName,
    IReadOnlyList<string> UnitTypeNames,
    int BoardWidth,
    int BoardHeight)
{
    /// <summary>Captured immutable nation metadata, never looked up in an advanced session state.</summary>
    public IReadOnlyDictionary<string, BattleAllegiance> Allegiances { get; init; } =
        FrozenDictionary<string, BattleAllegiance>.Empty;

    /// <summary>The names for <paramref name="view"/>, read from the game and the ruleset.</summary>
    /// <param name="view">The open battle.</param>
    /// <param name="state">The game the battle is fought in.</param>
    /// <param name="ruleset">The ruleset.</param>
    /// <returns>The display names.</returns>
    public static BattleDisplay For(TacticalBattleView view, GameState state, Ruleset ruleset, World? world = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        var names = new List<string>();
        foreach (var id in view.UnitTypeIds)
        {
            names.Add(ruleset.UnitTypes.FirstOrDefault(t => t.Id == id)?.Name ?? id);
        }

        return new BattleDisplay(
            state.NationById(view.AttackerNationId)?.Name ?? view.AttackerNationId,
            state.NationById(view.DefenderNationId)?.Name ?? view.DefenderNationId,
            names,
            ruleset.Combat.Tactical.BoardWidth,
            ruleset.Combat.Tactical.BoardHeight)
        {
            Allegiances = new[] { view.AttackerNationId, view.DefenderNationId }.Distinct().ToFrozenDictionary(
                id => id, id => BattleAllegiance.For(id, world is null ? state.NationById(id)?.Name ?? id : world.NationById(id)?.Name ?? id, world),
                StringComparer.Ordinal),
        };
    }
}

/// <summary>Immutable display metadata, keyed by world identity. Aliases and text contrast are designed (T197).</summary>
public sealed record BattleAllegiance(string NationId, string Caption, string ColorHex)
{
    public static BattleAllegiance For(string id, string name, World? world)
    {
        var caption = id switch { "seleucid" => "SELEUCID", "ptolemaic" => "PTOLEMEAN", _ => name.ToUpperInvariant() };
        var color = BattleIconTint.ForNation(world, id)?.First ?? (R: (byte)220, G: (byte)220, B: (byte)220);
        // Designed text-only contrast treatment: lift each dark palette channel halfway toward white.
        // Light palettes retain their original first battle colour. Icons/nation data/role cues are untouched.
        // Lifted channels are at least 128; a dark outline also separates light palettes from the panel.
        var dark = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) < 160;
        byte Lift(byte channel) => dark ? (byte)(128 + channel / 2) : channel;
        return new(id, caption, $"#{Lift(color.R):X2}{Lift(color.G):X2}{Lift(color.B):X2}");
    }
}

/// <summary>Current exchange in plain accessible text and independently coloured, escaped unit spans.</summary>
public sealed record BattleExchangePresentation(IReadOnlyList<string> PlainLines, IReadOnlyList<string> RichLines);

/// <summary>A board/minimap glyph with its shared fractional pose and opacity.</summary>
public sealed record BattleUnitView(BattleCellView Cell, double X, double Y, double Opacity);

/// <summary>One cell of the board as the screen draws it.</summary>
/// <param name="X">The column.</param>
/// <param name="Y">The row.</param>
/// <param name="Code">The engine's icon code, <see cref="TacticalIcon.Empty"/> for ground.</param>
/// <param name="Slot">The slot standing there, or −1 for ground.</param>
/// <param name="TintSide">0 when the icon is the attacker's (drawn in its nation's colours), 1 the defender's, −1 for ground.</param>
/// <param name="IconKey">The asset key of the icon (<see cref="BattleAssetKeys.UnitIcon(string, int)"/>), or <see langword="null"/> for ground.</param>
/// <param name="Selected">Whether the selection cursor is on this cell.</param>
/// <param name="Actor">A live, actually occupied actor cell; never a routed unit's former location.</param>
/// <param name="Target">A live, actually occupied target cell.</param>
public sealed record BattleCellView(int X, int Y, int Code, int Slot, int TintSide, string? IconKey, bool Selected, bool Actor = false, bool Target = false);

/// <summary>A toolbar button: its asset key, its tooltip and whether it can be pressed.</summary>
/// <param name="Id">The button's id, one of <see cref="BattleAssetKeys.ToolbarButtons"/>.</param>
/// <param name="GlyphKey">The glyph's asset key.</param>
/// <param name="Tooltip">The original's tooltip.</param>
/// <param name="Enabled">Whether the button can be pressed.</param>
public sealed record BattleToolbarButton(string Id, string GlyphKey, string Tooltip, bool Enabled);

/// <summary>One row of a unit list: its slot and the cells of its columns.</summary>
/// <param name="Slot">The unit's slot.</param>
/// <param name="Cells">The columns, as text.</param>
public sealed record BattleListRow(int Slot, IReadOnlyList<string> Cells);

/// <summary>What a click on the board asks the engine to do. The engine decides whether it is legal.</summary>
public enum BattleClickKind
{
    /// <summary>Nothing to send.</summary>
    None = 0,

    /// <summary>Select an own unit (the screen's cursor; no order).</summary>
    Select,

    /// <summary><c>battle place</c>.</summary>
    Place,

    /// <summary><c>battle move</c>.</summary>
    Move,

    /// <summary><c>battle attack</c> (sets the melee target).</summary>
    Attack,

    /// <summary><c>battle shoot</c>.</summary>
    Shoot,

    /// <summary><c>battle clear</c>.</summary>
    Clear,
}

/// <summary>A click turned into an order.</summary>
/// <param name="Kind">The order.</param>
/// <param name="Unit">The unit it is for.</param>
/// <param name="X">The cell's column.</param>
/// <param name="Y">The cell's row.</param>
/// <param name="Enemy">The enemy slot for an attack or a shot.</param>
public sealed record BattleClick(BattleClickKind Kind, int Unit, int X, int Y, int Enemy);

/// <summary>The kind of an exchange.</summary>
public enum BattleExchangeKind
{
    /// <summary>A shot.</summary>
    Shot,

    /// <summary>A melee.</summary>
    Melee,
}

/// <summary>
/// One exchange of the battle, as the side panel prints it: <c>SHOOTS AT</c> with <c>TROOP LOSSES n</c> or
/// <c>Unit destroyed !</c>, <c>ATTACKS</c> with <c>UNIT  LOSSES</c>, each side's <c>n</c> or <c>routed</c>
/// <strong>[confirmed: code, static, report §10]</strong>.
/// </summary>
/// <param name="Kind">Shot or melee.</param>
/// <param name="ActorName">The shooter's or attacker's name.</param>
/// <param name="TargetName">The target's or defender's name.</param>
/// <param name="ActorLoss">The attacker's loss (melee only).</param>
/// <param name="TargetLoss">The target's loss.</param>
/// <param name="ActorRouted">Whether the attacker routed (melee only).</param>
/// <param name="TargetRouted">Whether the defender routed (melee only).</param>
/// <param name="TargetDestroyed">Whether a shot left the target no troops.</param>
public sealed record BattleExchange(
    BattleExchangeKind Kind,
    string ActorName,
    string TargetName,
    int ActorLoss,
    int TargetLoss,
    bool ActorRouted,
    bool TargetRouted,
    bool TargetDestroyed)
{
    /// <summary>The panel's lines for this exchange.</summary>
    public IReadOnlyList<string> Lines =>
        Kind == BattleExchangeKind.Shot
            ? new[]
            {
                $"{ActorName} SHOOTS AT {TargetName}",
                TargetDestroyed ? "Unit destroyed !" : $"TROOP LOSSES {TargetLoss.ToString(CultureInfo.InvariantCulture)}",
            }
            : new[]
            {
                $"{ActorName} ATTACKS {TargetName}",
                "UNIT  LOSSES",
                $"{ActorName}: {(ActorRouted ? "routed" : ActorLoss.ToString(CultureInfo.InvariantCulture))}",
                $"{TargetName}: {(TargetRouted ? "routed" : TargetLoss.ToString(CultureInfo.InvariantCulture))}",
            };
}

/// <summary>
/// The display-only pauses (shot delay and attack delay) of the original's <c>TBattleDelays</c> dialog: 0 to 3.0 s,
/// the shot delay in 0.1 s steps and the attack delay in 1.0 s steps, default 0 s
/// <strong>[confirmed: code]</strong>; a player setting, never in a save or a ruleset (the user's decisions of
/// 2026-10-04 on #496).
/// </summary>
public static class BattlePauses
{
    /// <summary>The longest pause, in seconds.</summary>
    public const double MaxSeconds = 3.0;

    /// <summary>The shot delay's step, in seconds.</summary>
    public const double ShotStepSeconds = 0.1;

    /// <summary>
    /// The attack delay's step, in seconds: tenths, like the shot delay ([designed], the owner's play session of
    /// 2026-10-22; whole seconds left nothing between 0 and 1).
    /// </summary>
    public const double AttackStepSeconds = 0.1;

    /// <summary>The shot delay on a snapped, clamped value.</summary>
    /// <param name="seconds">The wanted delay.</param>
    /// <returns>A value in 0 … 3.0 on the 0.1 s grid.</returns>
    public static double SnapShot(double seconds) => Snap(seconds, ShotStepSeconds);

    /// <summary>The attack delay on a snapped, clamped value.</summary>
    /// <param name="seconds">The wanted delay.</param>
    /// <returns>A value in 0 … 3.0 on the 0.1 s grid.</returns>
    public static double SnapAttack(double seconds) => Snap(seconds, AttackStepSeconds);

    private static double Snap(double seconds, double step)
    {
        if (double.IsNaN(seconds))
        {
            return 0;
        }

        var clamped = Math.Clamp(seconds, 0, MaxSeconds);
        // Divide by ten last, so a tenth is the exact double (1.6, not 1.6000000000000001).
        return Math.Round(Math.Round(clamped / step) * step * 10) / 10;
    }
}

/// <summary>
/// What the battle screen shows for one <see cref="TacticalBattleView"/>, Godot-free (T127): each cell's icon key and
/// tint side, the caption, the toolbar, the selected unit's panel, the last exchange and the three unit lists. It
/// computes no rule: positions, troops, moves, targets and who is to move are the engine's, and a click is turned
/// into the order T125's session takes, which decides whether it is legal.
/// </summary>
public sealed class TacticalBattleViewModel
{
    /// <summary>The original's six morale words, from the Information window's word table <strong>[confirmed: code]</strong>.</summary>
    public static readonly IReadOnlyList<string> MoraleTierNames =
        new[] { "very low", "low", "normal", "high", "very high", "excellent" };

    /// <summary>The toolbar's tooltips, in the original's order <strong>[Wine candidate: probe, item 1]</strong>.</summary>
    public static readonly IReadOnlyList<string> ToolbarTooltips = new[]
    {
        "Unit moves", "Friendly units", "Enemy units", "Cancel selection",
        "End turn", "Change pauses", "Computer general on", "Surrender",
    };

    private static readonly Regex ShotLine = new(
        @"^Unit (\d+) shoots unit (\d+): (\d+) troops lost, (\d+) left, morale (-?\d+)\.$", RegexOptions.CultureInvariant);

    private static readonly Regex MeleeLine = new(
        @"^Unit (\d+) fights unit (\d+): attacker loses (\d+) \((\d+) left, morale (-?\d+)\), defender loses (\d+) \((\d+) left, morale (-?\d+)\)\.$",
        RegexOptions.CultureInvariant);

    private static readonly Regex RoutLine = new(@"^Unit (\d+) (?:routs|breaks and flees)\.$", RegexOptions.CultureInvariant);

    private readonly TacticalBattleView _view;
    private readonly BattleDisplay _display;
    private readonly int _perSide;
    private TacticalPresentationSnapshot? _snapshot;
    private int _perspective;
    public TacticalBattleFrame? DisplayedFrame { get; private set; }
    public TacticalBattleFrame? CurrentExchange { get; private set; }
    public TacticalBattleFrame? PreviousExchange { get; private set; }
    public bool ResolvingHalfRound { get; private set; }
    public TacticalPose? Pose { get; private set; }
    public TacticalPlaybackStage Stage { get; private set; }
    // Rout/cascade/removal are effects of the current exchange, not a reversal of its actor/target.
    // Keep those identities through housekeeping and Setup; all numbers still read the displayed snapshot.
    private TacticalBattleFrame? ParticipantFrame => CurrentExchange is not null
        && DisplayedFrame?.Kind is (TacticalFrameKind.Rout or TacticalFrameKind.Cascade or TacticalFrameKind.Removal
            or TacticalFrameKind.Morale or TacticalFrameKind.Setup or TacticalFrameKind.HalfRound
            or TacticalFrameKind.Terminal or TacticalFrameKind.Reconciliation or TacticalFrameKind.CapWithdrawal)
        ? CurrentExchange : DisplayedFrame;
    public int ActorSlot => ParticipantFrame?.Actor?.Slot ?? CurrentExchange?.Actor?.Slot ?? -1;
    public int TargetSlot => ParticipantFrame?.Actor is not null
        ? ParticipantFrame.Target?.Slot ?? -1 : CurrentExchange?.Target?.Slot ?? -1;

    /// <summary>Explicit phase/controller caption; HalfRound is not a completed next-round setup.</summary>
    public string ControllerText => ResolvingHalfRound
        ? "Finishing the previous half-round…"
        : _view.IsOver ? "Battle over"
        : $"Half-round {_view.Counter} · {(_view.SideToMove == 0 ? "Attacker" : "Defender")} · {NationName(_view.SideToMove)} · "
          + (_view.ComputerControlled[_view.SideToMove] ? "Computer nation" : _view.ComputerGeneral[_view.SideToMove] ? "Computer general" : "Human")
          + (_view.Placed ? " · movement / combat" : " · placement");

    public IReadOnlyList<string> ActorLines => ParticipantLines(ActorSlot, "○");
    public IReadOnlyList<string> TargetLines => ParticipantLines(TargetSlot, "◇");
    public IReadOnlyList<string> CurrentExchangeLines => CurrentExchangePresentation.PlainLines;
    public BattleExchangePresentation CurrentExchangePresentation
    {
        get
        {
            var exchange = PresentExchange(CurrentExchange, _display);
            return new(exchange.PlainLines.Concat(DepartureLines).ToArray(),
                exchange.RichLines.Concat(DeparturePresentation.RichLines).ToArray());
        }
    }

    /// <summary>Escape raw input in one pass; generated lb/rb tags must never be re-escaped.</summary>
    public static string EscapeRichText(string text) => string.Concat(text.Select(c => c switch
        { '[' => "[lb]", ']' => "[rb]", _ => c.ToString() }));

    public static BattleExchangePresentation PresentExchange(TacticalBattleFrame? frame, BattleDisplay display)
    {
        if (frame?.Event is not (TacticalShotEvent or TacticalMeleeEvent))
            return new(Array.Empty<string>(), Array.Empty<string>());
        string Unit(TacticalUnitIdentity identity, bool rich)
        {
            var metadata = Allegiance(display, identity);
            var text = $"{frame.After.View.Slots[identity.Slot].Name} · {metadata.Caption}";
            return rich ? $"[color={metadata.ColorHex}]{EscapeRichText(text)}[/color]" : text;
        }
        string Details(TacticalUnitIdentity identity)
        {
            var unit = frame.Before.View.Slots[identity.Slot];
            var type = unit.Type >= 0 && unit.Type < display.UnitTypeNames.Count
                ? display.UnitTypeNames[unit.Type] : $"type {unit.Type}";
            return $"{type} · {unit.Troops.ToString("N0", CultureInfo.InvariantCulture)} troops";
        }
        var action = frame.Event is TacticalShotEvent ? "SHOOTS AT" : "ATTACKS";
        var losses = frame.Event is TacticalShotEvent shot
            ? new[] { $"TROOP LOSSES {shot.Loss.ToString("N0", CultureInfo.InvariantCulture)}" }
            : frame.Event is TacticalMeleeEvent melee
                ? new[] { "UNIT LOSSES", $"Attacker {melee.AttackerLoss.ToString("N0", CultureInfo.InvariantCulture)} · Defender {melee.DefenderLoss.ToString("N0", CultureInfo.InvariantCulture)}" }
                : Array.Empty<string>();
        return new(new[] { Unit(frame.Actor!, false), Details(frame.Actor!), action,
                Unit(frame.Target!, false), Details(frame.Target!) }.Concat(losses).ToArray(),
            new[] { Unit(frame.Actor!, true), EscapeRichText(Details(frame.Actor!)), action,
                Unit(frame.Target!, true), EscapeRichText(Details(frame.Target!)) }.Concat(losses.Select(EscapeRichText)).ToArray());
    }
    private static BattleAllegiance Allegiance(BattleDisplay display, TacticalUnitIdentity identity) =>
        display.Allegiances.GetValueOrDefault(identity.NationId)
        ?? BattleAllegiance.For(identity.NationId,
            identity.Side == 0 ? display.AttackerNationName : display.DefenderNationName, null);

    public IReadOnlyList<string> DepartureLines => DeparturePresentation.PlainLines;
    private BattleExchangePresentation DeparturePresentation
    {
        get
        {
            if (DisplayedFrame?.Event is not (TacticalRoutedEvent or TacticalCascadeRemovedEvent))
                return new(Array.Empty<string>(), Array.Empty<string>());
            var identity = DisplayedFrame.Actor!;
            var metadata = Allegiance(_display, identity);
            var prefix = DisplayedFrame.Event is TacticalCascadeRemovedEvent ? "ROUT CASCADE REMOVAL" : "UNIT ROUTED";
            var unit = $"{metadata.Caption} #{identity.Slot} {_view.Slots[identity.Slot].Name}";
            var role = identity.Side == 0 ? "Attacker" : "Defender";
            var status = Stage is TacticalPlaybackStage.RoutHold or TacticalPlaybackStage.RoutFade ? "departure pending" : "removed";
            return new(new[] { $"{prefix} · {unit} · {role} · {status}" },
                new[] { $"[b]{prefix}[/b] · [color={metadata.ColorHex}]{EscapeRichText(unit)}[/color] · {role} · {status}" });
        }
    }

    public string PositionText(int slot)
    {
        if (Pose is { } pose && pose.Glyph.Slot == slot)
            return $"({pose.X.ToString("0.##", CultureInfo.InvariantCulture)},{pose.Y.ToString("0.##", CultureInfo.InvariantCulture)}) · {(pose.Departing ? "routed / departing" : Stage == TacticalPlaybackStage.Arrival ? "arrived" : "moving")}";
        var unit = _view.Slots[slot];
        return $"({unit.X},{unit.Y})";
    }

    /// <summary>The same draw list feeds board and minimap, with no stationary duplicate.</summary>
    public IReadOnlyList<BattleUnitView> VisualUnits
    {
        get
        {
            var units = Cells.Where(c => c.IconKey is not null && !(Pose is { } posed && (c.Slot == posed.Glyph.Slot || c.X == posed.Glyph.X && c.Y == posed.Glyph.Y)))
                .Select(c => new BattleUnitView(c, c.X, c.Y, 1)).ToList();
            if (Pose is { } pose)
            {
                var g = pose.Glyph;
                var cell = new BattleCellView(g.X, g.Y, g.Code, g.Slot, g.Side,
                    BattleAssetKeys.UnitIcon(g.TypeId, g.Code % TacticalIcon.SizesPerType),
                    !pose.Departing && g.Slot == SelectedSlot, g.Slot == ActorSlot, g.Slot == TargetSlot);
                units.Add(new BattleUnitView(cell, pose.X, pose.Y, pose.Opacity));
            }
            return units;
        }
    }
    public IReadOnlyList<string> PreviousExchangeLines => ExchangeLines(PreviousExchange);

    /// <summary>All values come from this one displayed snapshot; identity metadata never indexes by name.</summary>
    public static TacticalBattleViewModel FromSnapshot(TacticalPresentationSnapshot snapshot, BattleDisplay display,
        int selectedSlot = -1, TacticalBattleFrame? frame = null, TacticalBattleFrame? currentExchange = null,
        TacticalBattleFrame? previousExchange = null, int perspectiveSide = -1, bool resolvingHalfRound = false,
        bool controlsEnabled = false, TacticalPose? pose = null, TacticalPlaybackStage stage = TacticalPlaybackStage.Frame)
    {
        var live = selectedSlot >= 0 && selectedSlot < snapshot.View.Slots.Count && snapshot.View.Slots[selectedSlot].IsLive
            && !(pose is { Departing: true } departure && departure.Glyph.Slot == selectedSlot);
        var model = new TacticalBattleViewModel(snapshot.View, display, live ? selectedSlot : -1, null, controlsEnabled)
        {
            _snapshot = snapshot,
            _perspective = perspectiveSide < 0 ? snapshot.View.SideToMove : perspectiveSide,
            DisplayedFrame = frame,
            CurrentExchange = currentExchange,
            PreviousExchange = previousExchange,
            ResolvingHalfRound = resolvingHalfRound,
            Pose = pose,
            Stage = stage,
        };
        // Pending general placements retain old coordinates; map only the actual producer grid.
        model.Cells = model.BuildCells();
        return model;
    }

    public IReadOnlyList<string> ParticipantLines(int slot, string role)
    {
        if (slot < 0 || slot >= _view.Slots.Count) return Array.Empty<string>();
        var unit = _view.Slots[slot];
        var side = SideOf(slot);
        var gridIndex = unit.X * BoardHeight + unit.Y;
        var removalPending = !unit.IsLive && gridIndex >= 0 && gridIndex < _view.Grid.Count
            && _view.Grid[gridIndex] != TacticalIcon.Empty;
        var lines = new List<string>
        {
            // Before the battle a unit is named only; once it starts, by its side's strategic role.
            _view.Placed ? $"{role} {(side == 0 ? "Attacker" : "Defender")} · {NationName(side)} · #{slot}" : $"{role} {unit.Name}",
            (_view.Placed ? $"{unit.Name} · " : "") + $"{TypeName(unit.Type)} · {unit.Troops} troops" + (Pose?.Glyph.Slot == slot || unit.IsLive ? $" · {PositionText(slot)}" : removalPending ? " · removal pending" : " · removed"),
        };
        if (side == _perspective)
            lines.Add($"Morale: {unit.Morale} ({MoraleTier(unit.Morale)}) · moves {unit.Moves} · shots {unit.Shots}");
        return lines;
    }

    /// <summary>
    /// #1292: the hover text of the unit drawn at a board cell — name, nation, type and troops, plus morale,
    /// moves and shots for the perspective side's own units (what <see cref="ParticipantLines"/> shows) —
    /// or <see langword="null"/> when no unit is drawn there.
    /// </summary>
    /// <param name="x">The cell's column.</param>
    /// <param name="engineY">The cell's row in engine coordinates.</param>
    public string? HoverText(int x, int engineY)
    {
        var cell = Cells.FirstOrDefault(c => c.X == x && c.Y == engineY && c.IconKey is not null && c.Slot >= 0 && c.Slot < _view.Slots.Count);
        if (cell is null) return null;
        var unit = _view.Slots[cell.Slot];
        var side = SideOf(cell.Slot);
        var text = $"{unit.Name} ({NationName(side)})\n{TypeName(unit.Type)} · {unit.Troops} troops";
        return side == _perspective ? $"{text}\nMorale: {MoraleTier(unit.Morale)} · moves {unit.Moves} · shots {unit.Shots}" : text;
    }

    public IReadOnlyList<BattleListRow> RosterRows(int side) => LiveOf(side).Concat(Pose is { Departing: true } departing && SideOf(departing.Glyph.Slot) == side
        ? new[] { departing.Glyph.Slot } : Array.Empty<int>()).Distinct().Select(slot =>
    {
        var unit = _view.Slots[slot];
        var roles = (Pose is { Departing: true } departure && departure.Glyph.Slot == slot ? "ROUTED / DEPARTING " : "") + (slot == ActorSlot ? "○ " : "") + (slot == TargetSlot ? "◇ " : "") + (slot == SelectedSlot ? "□ SELECTED " : "");
        var cells = new List<string> { $"{roles}#{slot} {unit.Name}", TypeName(unit.Type), unit.Troops.ToString(CultureInfo.InvariantCulture) };
        if (Pose?.Glyph.Slot == slot) cells.Add(PositionText(slot));
        if (side == _perspective) cells.Add($"{ArmyDialogModels.QualityCaption(unit.Quality)} / {unit.Morale} ({MoraleTier(unit.Morale)})");
        return new BattleListRow(slot, cells);
    }).ToList();

    public string RosterTitle(int side) => $"{(side == 0 ? "Attacker" : "Defender")} — {NationName(side)}";
    public bool CanSeeDetails(int side) => side == _perspective;

    public static IReadOnlyList<string> ExchangeLines(TacticalBattleFrame? frame)
    {
        if (frame is null) return Array.Empty<string>();
        string Name(int slot) => $"#{slot} {frame.After.View.Slots[slot].Name}";
        return frame.Event switch
        {
            TacticalShotEvent shot => new[] { $"{Name(shot.Shooter)} SHOOTS AT {Name(shot.Target)}", $"TROOP LOSSES {shot.Loss}" },
            TacticalMeleeEvent melee => new[] { $"{Name(melee.Attacker)} ATTACKS {Name(melee.Defender)}", $"UNIT LOSSES: actor {melee.AttackerLoss} · target {melee.DefenderLoss}" },
            _ => Array.Empty<string>(),
        };
    }

    private TacticalBattleViewModel(
        TacticalBattleView view, BattleDisplay display, int selectedSlot, BattleExchange? lastExchange, bool controlsEnabled)
    {
        _view = view;
        _display = display;
        _perSide = Math.Max(1, view.Slots.Count / 2);
        _perspective = view.SideToMove;
        SelectedSlot = selectedSlot;
        LastExchange = lastExchange;
        Cells = BuildCells();
        Buttons = BuildButtons(controlsEnabled);
    }

    /// <summary>The selected slot, or −1.</summary>
    public int SelectedSlot { get; }

    /// <summary>The last exchange, or <see langword="null"/>.</summary>
    public BattleExchange? LastExchange { get; }

    /// <summary>The board's cells, indexed <c>x × boardHeight + y</c> like the engine's grid.</summary>
    public IReadOnlyList<BattleCellView> Cells { get; private set; }

    /// <summary>The eight toolbar buttons.</summary>
    public IReadOnlyList<BattleToolbarButton> Buttons { get; }

    /// <summary>The board's columns.</summary>
    public int BoardWidth => _display.BoardWidth;

    /// <summary>The board's rows.</summary>
    public int BoardHeight => _display.BoardHeight;

    /// <summary>The view this model reads.</summary>
    public TacticalBattleView View => _view;

    /// <summary>The nation whose side is to move.</summary>
    public string SideToMoveName => NationName(_view.SideToMove);

    /// <summary>
    /// The window caption: <c>A  v  D          N to place units.</c> or <c>… to move units.</c>
    /// <strong>[Wine candidate: probe, item 1]</strong>.
    /// </summary>
    public string Caption => $"{_display.AttackerNationName}  v  {_display.DefenderNationName}";

    /// <summary>
    /// The phase line under <see cref="Caption"/>, shown smaller (the owner's play session of 2026-10-22; the
    /// original puts both on one title bar, which wrapped badly here).
    /// </summary>
    public string CaptionPhase =>
        _view.IsOver ? "The battle is over." : $"{SideToMoveName} to {(_view.Placed ? "move" : "place")} units.";

    /// <summary>
    /// Builds the model.
    /// </summary>
    /// <param name="view">The engine's view of the open battle.</param>
    /// <param name="display">The names.</param>
    /// <param name="selectedSlot">The screen's selected slot, or −1.</param>
    /// <param name="lastExchange">The last exchange to print, if any.</param>
    /// <param name="controlsEnabled">Whether the toolbar can be pressed (false while a played-back exchange is being shown).</param>
    /// <returns>The model.</returns>
    public static TacticalBattleViewModel From(
        TacticalBattleView view, BattleDisplay display, int selectedSlot = -1, BattleExchange? lastExchange = null, bool controlsEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(display);
        var live = selectedSlot >= 0 && selectedSlot < view.Slots.Count && view.Slots[selectedSlot].IsLive;
        return new TacticalBattleViewModel(view, display, live ? selectedSlot : -1, lastExchange, controlsEnabled);
    }

    /// <summary>The morale tier name: <c>(morale − 27) div 8</c>, on the original's six words.</summary>
    /// <remarks>
    /// <strong>[confirmed: code]</strong> for the index; a tier past the table (morale 75 and over, which the battle can
    /// reach) prints the last word and one below it the first, <strong>[designed]</strong>: the research reports read
    /// the table's six words and no seventh.
    /// </remarks>
    /// <param name="morale">The unit's battle morale.</param>
    /// <returns>The tier's name.</returns>
    public static string MoraleTier(int morale)
    {
        var index = (int)Math.Floor((morale - 27) / 8.0);
        return MoraleTierNames[Math.Clamp(index, 0, MoraleTierNames.Count - 1)];
    }

    /// <summary>The unit-icon cell grid size is the engine's; a slot's side is <c>slot div 20</c>.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>0 attacker, 1 defender.</returns>
    public int SideOf(int slot) => slot / _perSide;

    /// <summary>The live slot standing on a cell, or −1.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The slot.</returns>
    public int SlotAt(int x, int y)
    {
        // The engine's contract: when several live units share a cell, the last slot wins.
        var found = -1;
        for (var s = 0; s < _view.Slots.Count; s++)
        {
            var unit = _view.Slots[s];
            if (Pose is { Departing: true } departure && departure.Glyph.Slot == s) continue;
            if (unit.IsLive && unit.X == x && unit.Y == y && !(_snapshot?.PendingPlacementSlots.Contains(s) ?? false))
            {
                found = s;
            }
        }

        return found;
    }

    /// <summary>The nation id whose colours a cell's icon is tinted with.</summary>
    /// <param name="cell">The cell.</param>
    /// <returns>The nation id, or <see langword="null"/> for ground.</returns>
    public string? TintNationId(BattleCellView cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return cell.TintSide switch
        {
            0 => _view.AttackerNationId,
            1 => _view.DefenderNationId,
            _ => null,
        };
    }

    /// <summary>The selected unit's lines in the side panel; empty when no unit is selected.</summary>
    public IReadOnlyList<string> PanelLines
    {
        get
        {
            if (SelectedSlot < 0)
            {
                return Array.Empty<string>();
            }

            var unit = _view.Slots[SelectedSlot];
            var side = SideOf(SelectedSlot);
            var lines = new List<string>
            {
                NationName(side),
                unit.Name + (unit.Origin != 0 ? " mercenaries" : string.Empty),

                TypeName(unit.Type),
                $"{unit.Troops.ToString(CultureInfo.InvariantCulture)} troops",
            };

            if (Pose?.Glyph.Slot == SelectedSlot) lines.Add(PositionText(SelectedSlot));
            // The side to move sees its own unit's quality, morale, moves, shots and target; an enemy's stay hidden.
            if (side == _perspective)
            {
                lines.Add($"Quality: {ArmyDialogModels.QualityCaption(unit.Quality)}");
                lines.Add($"Morale: {MoraleTier(unit.Morale)}");
                lines.Add($"Moves: {unit.Moves.ToString(CultureInfo.InvariantCulture)}");
                lines.Add($"Shots: {unit.Shots.ToString(CultureInfo.InvariantCulture)}");
                lines.Add($"Attacks: {(unit.Target >= 0 && unit.Target < _view.Slots.Count ? _view.Slots[unit.Target].Name : "nobody")}");
                lines.AddRange(PlannedExchangeLines);
            }

            return lines;
        }
    }

    /// <summary>
    /// The live enemy the selected own unit has set as its melee target, or -1. The melee is resolved at End
    /// turn (<c>FUN_00439C20</c>); the board marks this unit as the target while no exchange is being shown.
    /// </summary>
    public int PlannedTargetSlot
    {
        get
        {
            if (SelectedSlot < 0 || SelectedSlot >= _view.Slots.Count || SideOf(SelectedSlot) != _perspective) return -1;
            var target = _view.Slots[SelectedSlot].Target;
            return target >= 0 && target < _view.Slots.Count && _view.Slots[target].IsLive ? target : -1;
        }
    }

    /// <summary>
    /// The planned melee of the selected own unit: who attacks whom, with nation, type and troops, and how many
    /// own units have set the same target (the exchange's focus). Empty when it has none.
    /// </summary>
    public IReadOnlyList<string> PlannedExchangeLines
    {
        get
        {
            var target = PlannedTargetSlot;
            if (target < 0) return Array.Empty<string>();
            var unit = _view.Slots[SelectedSlot];
            var enemy = _view.Slots[target];
            var focus = LiveOf(_perspective).Count(s => _view.Slots[s].Target == target);
            return new[]
            {
                "Planned melee, at End turn:",
                $"{unit.Name} ({TypeName(unit.Type)}, {unit.Troops.ToString(CultureInfo.InvariantCulture)} troops) attacks",
                $"{enemy.Name} of {NationName(SideOf(target))} ({TypeName(enemy.Type)}, {enemy.Troops.ToString(CultureInfo.InvariantCulture)} troops)",
                focus == 1 ? "No other unit attacks it." : $"{focus} of your units attack it.",
            };
        }
    }

    /// <summary>The <em>Unit moves</em> list's column headers.</summary>
    public static IReadOnlyList<string> UnitMovesHeaders { get; } = new[] { "Name", "Type", "Moves", "Shots", "Attacks" };

    /// <summary>The <em>Friendly units</em> list's column headers.</summary>
    public static IReadOnlyList<string> FriendlyHeaders { get; } = new[] { "Name", "Type", "Troops", "Quality", "Morale" };

    /// <summary>The <em>Enemy units</em> list's column headers.</summary>
    public static IReadOnlyList<string> EnemyHeaders { get; } = new[] { "Name", "Type", "Troops" };

    /// <summary>The own units' name, type, moves, shots and target.</summary>
    public IReadOnlyList<BattleListRow> UnitMovesRows =>
        OwnLive().Select(s => new BattleListRow(
            s,
            new[]
            {
                _view.Slots[s].Name,
                TypeName(_view.Slots[s].Type),
                _view.Slots[s].Moves.ToString(CultureInfo.InvariantCulture),
                _view.Slots[s].Shots.ToString(CultureInfo.InvariantCulture),
                _view.Slots[s].Target >= 0 && _view.Slots[s].Target < _view.Slots.Count ? _view.Slots[_view.Slots[s].Target].Name : "-",
            })).ToList();

    /// <summary>The own units' name, type, troops, quality tier and morale tier.</summary>
    public IReadOnlyList<BattleListRow> FriendlyRows =>
        OwnLive().Select(s => new BattleListRow(
            s,
            new[]
            {
                _view.Slots[s].Name,
                TypeName(_view.Slots[s].Type),
                _view.Slots[s].Troops.ToString(CultureInfo.InvariantCulture),
                ArmyDialogModels.QualityCaption(_view.Slots[s].Quality),
                MoraleTier(_view.Slots[s].Morale),
            })).ToList();

    /// <summary>The enemy units' name, type and troops.</summary>
    public IReadOnlyList<BattleListRow> EnemyRows =>
        LiveOf(1 - _view.SideToMove).Select(s => new BattleListRow(
            s,
            new[]
            {
                _view.Slots[s].Name,
                TypeName(_view.Slots[s].Type),
                _view.Slots[s].Troops.ToString(CultureInfo.InvariantCulture),
            })).ToList();

    /// <summary>The order a left click on a cell means: select, place, move or set a melee target.</summary>
    /// <param name="x">The cell's column.</param>
    /// <param name="y">The cell's row.</param>
    /// <returns>The click.</returns>
    public BattleClick ForLeftClick(int x, int y)
    {
        var at = SlotAt(x, y);
        if (at >= 0 && SideOf(at) == _view.SideToMove)
        {
            return new BattleClick(BattleClickKind.Select, at, x, y, -1);
        }

        if (SelectedSlot < 0 || SideOf(SelectedSlot) != _view.SideToMove)
        {
            return new BattleClick(BattleClickKind.None, -1, x, y, -1);
        }

        if (at < 0)
        {
            return new BattleClick(_view.Placed ? BattleClickKind.Move : BattleClickKind.Place, SelectedSlot, x, y, -1);
        }

        return new BattleClick(BattleClickKind.Attack, SelectedSlot, x, y, at);
    }

    /// <summary>The order a right click on a cell means: shoot an enemy, or clear an own unit's target.</summary>
    /// <param name="x">The cell's column.</param>
    /// <param name="y">The cell's row.</param>
    /// <returns>The click.</returns>
    public BattleClick ForRightClick(int x, int y)
    {
        var at = SlotAt(x, y);
        if (at < 0)
        {
            return new BattleClick(BattleClickKind.None, -1, x, y, -1);
        }

        if (SideOf(at) == _view.SideToMove)
        {
            return new BattleClick(BattleClickKind.Clear, at, x, y, -1);
        }

        return SelectedSlot >= 0 && SideOf(SelectedSlot) == _view.SideToMove
            ? new BattleClick(BattleClickKind.Shoot, SelectedSlot, x, y, at)
            : new BattleClick(BattleClickKind.None, -1, x, y, -1);
    }

    /// <summary>
    /// The exchanges an order's output lines describe, in order, with the names read from the view before the order.
    /// The engine has already played them all; the screen replays them for the pauses.
    /// </summary>
    /// <param name="lines">The lines of one <c>Submit</c> call.</param>
    /// <param name="before">The view before the order.</param>
    /// <returns>The exchanges.</returns>
    public static IReadOnlyList<BattleExchange> ParseExchanges(IReadOnlyList<string> lines, TacticalBattleView before)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(before);
        string NameOf(int slot) =>
            slot >= 0 && slot < before.Slots.Count && before.Slots[slot].Name.Length > 0 ? before.Slots[slot].Name : $"Unit {slot}";

        var result = new List<BattleExchange>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (ShotLine.Match(lines[i]) is { Success: true } shot)
            {
                var target = Number(shot, 2);
                var left = Number(shot, 4);
                result.Add(new BattleExchange(
                    BattleExchangeKind.Shot, NameOf(Number(shot, 1)), NameOf(target), 0, Number(shot, 3), false, false, left <= 0));
            }
            else if (MeleeLine.Match(lines[i]) is { Success: true } melee)
            {
                var actor = Number(melee, 1);
                var defender = Number(melee, 2);
                var actorRouted = false;
                var defenderRouted = false;
                for (var j = i + 1; j < lines.Count && !ShotLine.IsMatch(lines[j]) && !MeleeLine.IsMatch(lines[j]); j++)
                {
                    if (RoutLine.Match(lines[j]) is { Success: true } rout)
                    {
                        var slot = Number(rout, 1);
                        actorRouted |= slot == actor;
                        defenderRouted |= slot == defender;
                    }
                }

                result.Add(new BattleExchange(
                    BattleExchangeKind.Melee, NameOf(actor), NameOf(defender), Number(melee, 3), Number(melee, 6), actorRouted, defenderRouted, false));
            }
        }

        return result;
    }

    private static int Number(Match match, int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    private string NationName(int side) => side == 0 ? _display.AttackerNationName : _display.DefenderNationName;

    private string TypeName(int type) =>
        type >= 0 && type < _display.UnitTypeNames.Count ? _display.UnitTypeNames[type] : $"type {type}";

    private IEnumerable<int> LiveOf(int side)
    {
        for (var s = side * _perSide; s < (side + 1) * _perSide && s < _view.Slots.Count; s++)
        {
            if (_view.Slots[s].IsLive)
            {
                yield return s;
            }
        }
    }

    private IEnumerable<int> OwnLive() => LiveOf(_view.SideToMove);

    private IReadOnlyList<BattleCellView> BuildCells()
    {
        var cells = new List<BattleCellView>(_display.BoardWidth * _display.BoardHeight);
        for (var x = 0; x < _display.BoardWidth; x++)
        {
            for (var y = 0; y < _display.BoardHeight; y++)
            {
                var index = (x * _display.BoardHeight) + y;
                var code = index < _view.Grid.Count ? _view.Grid[index] : TacticalIcon.Empty;
                if (code == TacticalIcon.Empty)
                {
                    cells.Add(new BattleCellView(x, y, code, -1, -1, null, false));
                    continue;
                }

                var side = code >= TacticalIcon.DefenderOffset ? 1 : 0;
                var type = (code % TacticalIcon.DefenderOffset) / TacticalIcon.SizesPerType;
                var size = code % TacticalIcon.SizesPerType;
                // Exchange frames can precede rout removal: raw icon arithmetic can be negative for an
                // overkilled unit. Preserve the actual code/grid; only valid image-list entries have sprites.
                var validIcon = type >= 0 && type < _view.UnitTypeIds.Count && size >= 0;
                var typeId = validIcon ? _view.UnitTypeIds[type] : string.Empty;
                var slot = SlotAt(x, y);
                cells.Add(new BattleCellView(
                    x, y, code, slot, side, validIcon ? BattleAssetKeys.UnitIcon(typeId, size) : null, slot >= 0 && slot == SelectedSlot,
                    slot >= 0 && slot == ActorSlot, slot >= 0 && (slot == TargetSlot || (TargetSlot < 0 && slot == PlannedTargetSlot))));
            }
        }

        return cells;
    }

    private IReadOnlyList<BattleToolbarButton> BuildButtons(bool controlsEnabled) =>
        BattleAssetKeys.ToolbarButtons
            .Select((id, i) => new BattleToolbarButton(id, BattleAssetKeys.ToolbarGlyph(id), ToolbarTooltips[i], controlsEnabled && !_view.IsOver))
            .ToList();
}
