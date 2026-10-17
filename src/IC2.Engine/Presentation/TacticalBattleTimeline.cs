using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;

namespace IC2.Engine.Presentation;

/// <summary>The mutation boundary represented by a presentation frame. Draws and write-back promotions are never frames.</summary>
public enum TacticalFrameKind
{
    Initial, Placement, Movement, Target, Projectile, Shot, Melee, Rout, Cascade,
    Removal, Morale, Setup, HalfRound, Controller, CapWithdrawal, Terminal, Reconciliation,
}

/// <summary>An explicit boundary for mutations with no conformance event (for example projectile consumption).</summary>
public sealed record TacticalCaptureBoundary(TacticalFrameKind? Kind = null, int Actor = -1, int Target = -1, ValueList<int>? PendingPlacementSlots = null);

/// <summary>Stable identity within a battle; names are display data and never keys. TypeId selects audio/rules.</summary>
public sealed record TacticalUnitIdentity(int Slot, int Side, string ArmyId, string NationId, string TypeId);

/// <summary>
/// A complete immutable board snapshot, including runtime controller, phase and general claim state.
/// PendingPlacementSlots are temporarily off-grid while the general rebuilds its formation; their coordinates
/// retain their prior values until placed. All other live slots occupy their coordinates; stacked cells retain
/// the authoritative icon of the last painted slot. Grid is always the actual working grid, never repainted.
/// </summary>
public sealed record TacticalPresentationSnapshot(
    TacticalBattleView View, int Width, int Height, bool Capped, ValueList<int> GeneralClaims,
    ValueList<int> PendingPlacementSlots);

/// <summary>
/// One ordered mutation. Before and After are coherent event-local snapshots, not reconstructed from a final
/// board or parsed text. Event supplies typed numeric exchange evidence; Actor and Target have distinct roles.
/// FrameId increases across commands in this battle. CommandId zero identifies opening/reopening automation.
/// A terminal frame may precede final half-round housekeeping; Reconciliation is the authoritative handoff.
/// </summary>
public sealed record TacticalBattleFrame(
    string BattleId, long CommandId, long FrameId, TacticalFrameKind Kind,
    TacticalUnitIdentity? Actor, TacticalUnitIdentity? Target, TacticalEvent? Event,
    TacticalPresentationSnapshot Before, TacticalPresentationSnapshot After);

/// <summary>
/// Optional transient collector. Attach Observe to a context before invoking the ordinary tactical routines;
/// it never runs rules, draws, changes a state, or adds trace entries. Drain transfers pending frames exactly
/// once, including the initial batch. Consumers retain batches; a later drain never repeats history.
/// </summary>
public sealed class TacticalBattleTimeline
{
    private readonly TacticalContext _context;
    private readonly string _battleId;
    private readonly string[] _nations;
    private readonly List<TacticalBattleFrame> _pending = new();
    private TacticalPresentationSnapshot? _last;
    private long _command;
    private long _frame;

    /// <summary>Creates a collector for one battle's stable identity and its two nations.</summary>
    public TacticalBattleTimeline(TacticalContext context, string battleId, string attackerNationId, string defenderNationId)
    {
        _context = context;
        _battleId = battleId;
        _nations = new[] { attackerNationId, defenderNationId };
    }

    /// <summary>Starts the next submitted tactical order, before invoking it. Refused orders emit no frames.</summary>
    public void BeginCommand() => _command++;

    /// <summary>Copies a mutation boundary. Null evidence records only a changed state (or the initial state).</summary>
    public void Observe(TacticalBattleState state, TacticalEvent? evidence, TacticalCaptureBoundary? boundary = null)
    {
        var snapshot = new TacticalPresentationSnapshot(
            new TacticalBattleView(state.AttackerArmyId, state.DefenderArmyId, _nations[0], _nations[1],
                state.SideToMove, state.Placed, state.Counter, state.IsOver,
                ValueList.From(_context.Ruleset.UnitTypes.Select(t => t.Id)), state.Slots, state.Grid,
                state.ComputerControlled, state.ComputerGeneral, state.Selected),
            _context.Rules.BoardWidth, _context.Rules.BoardHeight, state.Capped, state.GeneralClaims,
            boundary?.PendingPlacementSlots ?? ValueList<int>.Empty);
        if (evidence is TacticalDrawEvent or TacticalPromotionEvent || (evidence is null && boundary?.Kind is null && snapshot == _last)) return;
        var kind = _last is null ? TacticalFrameKind.Initial : boundary?.Kind ?? evidence switch
        {
            TacticalCopiedInEvent or TacticalPlacedEvent => TacticalFrameKind.Placement,
            TacticalMovedEvent or TacticalMoveForfeitedEvent => TacticalFrameKind.Movement,
            TacticalTargetSetEvent or TacticalTargetClearedEvent or TacticalSelectedEvent => TacticalFrameKind.Target,
            TacticalShotEvent => TacticalFrameKind.Shot,
            TacticalMeleeEvent => TacticalFrameKind.Melee,
            TacticalRoutedEvent => TacticalFrameKind.Rout,
            TacticalCascadeRemovedEvent => TacticalFrameKind.Cascade,
            TacticalSetUpEvent => TacticalFrameKind.Setup,
            TacticalHalfRoundEndedEvent => TacticalFrameKind.HalfRound,
            TacticalComputerGeneralEvent => TacticalFrameKind.Controller,
            TacticalCapReachedEvent => TacticalFrameKind.CapWithdrawal,
            TacticalBattleOverEvent => TacticalFrameKind.Terminal,
            TacticalSurrenderedEvent => TacticalFrameKind.Removal,
            _ => TacticalFrameKind.Reconciliation,
        };
        var (actor, target) = evidence switch
        {
            TacticalCopiedInEvent e => (e.Slot, -1),
            TacticalPlacedEvent e => (e.Slot, -1),
            TacticalMovedEvent e => (e.Slot, -1),
            TacticalMoveForfeitedEvent e => (e.Slot, -1),
            TacticalSelectedEvent e => (e.Slot, -1),
            TacticalTargetSetEvent e => (e.Slot, e.Target),
            TacticalTargetClearedEvent e => (e.Slot, -1),
            TacticalShotEvent e => (e.Shooter, e.Target),
            TacticalMeleeEvent e => (e.Attacker, e.Defender),
            TacticalRoutedEvent e => (e.Slot, -1),
            TacticalCascadeRemovedEvent e => (e.Slot, -1),
            _ => (boundary?.Actor ?? -1, boundary?.Target ?? -1),
        };
        _pending.Add(new TacticalBattleFrame(_battleId, _command, ++_frame, kind,
            Identity(actor), Identity(target), evidence, _last ?? snapshot, snapshot));
        _last = snapshot;

        TacticalUnitIdentity? Identity(int slot)
        {
            if (slot < 0) return null;
            var side = state.SideOf(slot);
            return new TacticalUnitIdentity(slot, side, side == 0 ? state.AttackerArmyId : state.DefenderArmyId,
                _nations[side], _context.TypeOf(state.Slots[slot].Type).Id);
        }
    }

    /// <summary>
    /// Copies opening/reopening frames still awaiting their first delivery, without consuming them. Only command
    /// zero belongs to this startup handoff; later orders never become initial history. Drain remains authoritative.
    /// </summary>
    internal ValueList<TacticalBattleFrame> PendingOpeningFrames => ValueList.From(_pending.Where(f => f.CommandId == 0));

    /// <summary>Transfers the pending immutable batch exactly once. Empty means no new tactical mutations.</summary>
    public ValueList<TacticalBattleFrame> Drain()
    {
        var batch = ValueList.From(_pending);
        _pending.Clear();
        return batch;
    }
}
