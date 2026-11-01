using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;

namespace IC2.Engine.Presentation;

public sealed partial class GameSession
{
    private sealed record UndoTurn(string Attacker, string Defender, int Side, int Counter, bool Placed);
    private sealed record TacticalUndoEntry(GameState Campaign, TacticalBattleCheckpoint Checkpoint, int LogCount, UndoTurn Turn);
    private readonly Stack<TacticalUndoEntry> _tacticalUndo = new();
    private UndoTurn? _tacticalUndoTurn;
    private TacticalBattleConclusion? _undoConclusion;
    private bool _tacticalRewound;
    private bool _tacticalUndoEnabled;

    /// <summary>UI-only opt-in. Existing CLI/replay callers retain immediate completion.</summary>
    public bool TacticalUndoEnabled
    {
        get => _tacticalUndoEnabled;
        set
        {
            if (_tacticalUndoEnabled == value) return;
            _tacticalUndoEnabled = value;
            _tacticalUndo.Clear();
            _tacticalUndoTurn = null;
            // A prepared result must still be acknowledged even if a host disables
            // history; discarding its cache would lose the pending continuation.
        }
    }
    /// <summary>Whether an own-turn checkpoint exists, without crossing the opponent's handoff.</summary>
    public bool CanUndoTactical => TacticalUndoEnabled && _battle is not null && _tacticalUndo.Count > 0
        && (_battle.IsOver || _tacticalUndoTurn == TurnOf(_battle.Battle));
    /// <summary>A result is provisional until the UI acknowledges it.</summary>
    public bool BattleResultAwaitingAcknowledgement => _undoConclusion is not null;
    /// <summary>Current tactical draw position for deterministic replay/undo diagnostics.</summary>
    public ulong? TacticalDrawState => _battle?.DrawState;
    /// <summary>Restore one current-turn action.</summary>
    public SessionOutput BattleUndo() => Submit("battle undo");
    /// <summary>Commit the already-computed result and continue once.</summary>
    public SessionOutput AcknowledgeBattleResult() => Submit("battle accept");

    private static UndoTurn TurnOf(TacticalBattleState battle) =>
        new(battle.AttackerArmyId, battle.DefenderArmyId, battle.SideToMove, battle.Counter, battle.Placed);

    private void ResetTacticalUndo()
    {
        _tacticalUndo.Clear();
        _tacticalUndoTurn = null;
        _undoConclusion = null;
        _tacticalRewound = false;
    }

    private TacticalUndoEntry? CaptureTacticalUndo()
    {
        if (!TacticalUndoEnabled || _battle is null || _battle.IsOver
            || _battle.Battle.ComputerControlled[_battle.Battle.SideToMove]) return null;
        var turn = TurnOf(_battle.Battle);
        if (turn != _tacticalUndoTurn)
        {
            _tacticalUndo.Clear();
            _tacticalUndoTurn = turn;
        }
        var checkpoint = _battle.CaptureCheckpoint();
        // Keep one live log, retaining only prefix lengths in history. This avoids
        // retaining a full growing log array for every action in a long battle.
        return new(State, checkpoint with { State = checkpoint.State with { Log = ValueList<TacticalEvent>.Empty } },
            checkpoint.State.Log.Count, turn);
    }

    private void RetainTacticalUndo(TacticalUndoEntry? before, TacticalBattleOrderResult result)
    {
        if (before is null || !result.Accepted || _battle is null) return;
        var crossed = result.Timeline.Any(frame =>
            (frame.Kind is TacticalFrameKind.Setup or TacticalFrameKind.Controller)
            && !frame.After.View.IsOver
            && frame.After.View.SideToMove != before.Turn.Side);
        // A live return in another phase/turn has already passed the controller.
        // Terminal old-side melee can precede Setup despite the internal counter flip.
        crossed |= !_battle.IsOver && TurnOf(_battle.Battle) != before.Turn;
        if (crossed)
        {
            _tacticalUndo.Clear();
            _tacticalUndoTurn = null;
            return;
        }
        if ((_battle.Battle with { Log = ValueList<TacticalEvent>.Empty }) == before.Checkpoint.State
            && _battle.DrawState == before.Checkpoint.DrawState) return;
        _tacticalUndo.Push(before);
    }

    private IReadOnlyList<string> UndoTacticalAction()
    {
        if (!CanUndoTactical) return new[] { "No action from your current tactical turn can be undone." };
        var entry = _tacticalUndo.Pop();
        var log = _battle!.Battle.Log;
        var checkpoint = entry.Checkpoint with { State = entry.Checkpoint.State with
            { Log = ValueList.From(log.Take(entry.LogCount)) } };
        _battle = _battle.RestoreCheckpoint(checkpoint, NextTacticalBattleIdentity());
        State = entry.Campaign;
        _tacticalUndoTurn = entry.Turn;
        _undoConclusion = null;
        _pendingTacticalReport = null;
        _pendingTacticalTimeline.Clear();
        _pendingTacticalTimeline.AddRange(_battle.DrainTimeline().Select(frame => frame with { Kind = TacticalFrameKind.Reconciliation }));
        _tacticalRewound = true;
        return new[] { "Your last tactical action was undone." };
    }

    private void PreviewTacticalConclusion(List<string> lines)
    {
        var conclusion = _battle!.Conclude(_lap?.Opened ?? State, Ruleset, world: World);
        if (conclusion.State is null || conclusion.Report is null)
        {
            lines.Add($"The battle result could not be prepared: {conclusion.Rejection}");
            return;
        }
        _undoConclusion = conclusion;
        _pendingTacticalReport = conclusion.Report;
        _pendingTacticalTimeline.AddRange(_battle.DrainTimeline());
        lines.AddRange(TacticalBattleSession.ReportLines(conclusion.Report, conclusion.State));
    }

    private IReadOnlyList<string> AcceptTacticalConclusion()
    {
        if (_undoConclusion is not { } conclusion) return new[] { "No battle result awaits acknowledgement." };
        _undoConclusion = null;
        _tacticalUndo.Clear();
        _tacticalUndoTurn = null;
        var lines = new List<string> { "Battle result accepted." };
        FinishTacticalBattle(lines, conclusion);
        return lines;
    }
}
