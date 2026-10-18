using IC2.Engine.Battle.Tactical;
using IC2.Engine.Presentation;

namespace IC2.Slice.Screens;

public enum TacticalPlaybackStage { Frame, Announcement, Travel, Arrival, RoutHold, RoutFade }

/// <summary>A previously displayed glyph, never a new authoritative unit.</summary>
public sealed record TacticalGlyph(int Slot, int X, int Y, int Code, int Side, string TypeId);
public sealed record TacticalPose(TacticalGlyph Glyph, double X, double Y, double Opacity, bool Departing);

/// <summary>Display-only traversal of immutable producer frames; never submits gameplay orders.</summary>
public sealed class TacticalPlayback
{
    private readonly Queue<TacticalBattleFrame> _pending = new();
    private readonly HashSet<(string Battle, long Frame)> _seen = new();
    private readonly Dictionary<int, TacticalGlyph> _glyphs = new();
    private Action? _completed;
    private string? _battle;
    private long _command = -1;
    private string? _controller;
    private double _elapsed;
    private double _duration;
    private bool _interiorShown;
    private (int X, int Y)? _destination;

    public event Action<TacticalBattleFrame>? FrameEntered;
    /// <summary>T183's typed exactly-once exchange-entry seam, including zero delay and skip.</summary>
    public event Action<TacticalBattleFrame>? ExchangeEntered;
    /// <summary>Progress changes presentation only, never re-entering a frame or exchange.</summary>
    public event Action? PresentationChanged;

    public TacticalBattleFrame? CurrentFrame { get; private set; }
    public TacticalBattleFrame? CurrentExchange { get; private set; }
    public TacticalBattleFrame? PreviousExchange { get; private set; }
    public TacticalPresentationSnapshot? Snapshot { get; private set; }
    public TacticalPose? Pose { get; private set; }
    public TacticalPlaybackStage Stage { get; private set; }
    public double Progress => _duration > 0 ? Math.Clamp(_elapsed / _duration, 0, 1) : 1;
    public bool IsPlaying { get; private set; }
    /// <summary>HalfRound precedes old-side melee; only Setup releases the new controller.</summary>
    public bool ResolvingHalfRound { get; private set; }
    public double ShotDelay { get; set; }
    public double AttackDelay { get; set; }

    // [designed] Display-only bounds, not original-game timing claims. Any nonzero preference
    // gives travel/fade 0.2..0.6s and notices/arrival 0.1..0.4s. No new setting or gameplay delay.
    private double Pace => Math.Max(BattlePauses.SnapShot(ShotDelay), BattlePauses.SnapAttack(AttackDelay));
    private double Notice => Pace == 0 ? 0 : Math.Clamp(Pace * 0.4, 0.1, 0.4);
    private double Animation => Pace == 0 ? 0 : Math.Clamp(Pace * 0.6, 0.2, 0.6);

    public void Begin(IEnumerable<TacticalBattleFrame> frames, Action? completed = null)
    {
        if (IsPlaying) throw new InvalidOperationException("Finish the displayed command before starting another.");
        var supplied = false;
        foreach (var frame in frames)
        {
            supplied = true;
            if (_seen.Add((frame.BattleId, frame.FrameId))) _pending.Enqueue(frame);
        }
        if (supplied && _pending.Count == 0) return;
        _completed = completed;
        IsPlaying = true;
        _duration = _elapsed = 0;
        Advance(0);
    }

    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        Traverse(seconds, false);
    }

    public void Finish() => Traverse(0, true);

    private void Traverse(double seconds, bool skip)
    {
        while (IsPlaying)
        {
            if (_duration > 0 && !skip)
            {
                if (seconds == 0) return;
                var remaining = _duration - _elapsed;
                // Protect a rendered interior even with an oversized delta. Carrying an entire lagged
                // frame through travel/arrival/fade would erase the required visible stages.
                if (Stage is TacticalPlaybackStage.Travel or TacticalPlaybackStage.RoutFade && !_interiorShown)
                {
                    _elapsed = Math.Min(_duration * 0.5, _elapsed + seconds);
                    _interiorShown = true;
                    UpdatePose();
                    Publish();
                    return;
                }
                _elapsed += Math.Min(seconds, remaining);
                seconds = Math.Max(0, seconds - remaining);
                UpdatePose();
                if (_elapsed < _duration) { Publish(); return; }
            }
            var protectedStage = Stage != TacticalPlaybackStage.Frame && _duration > 0;
            if (CurrentFrame is not null && CompleteStage())
            {
                Publish();
                if (!skip && protectedStage) return;
                continue;
            }
            if (!_pending.TryDequeue(out var frame))
            {
                IsPlaying = false;
                Pose = null;
                Publish();
                var completed = _completed;
                _completed = null;
                completed?.Invoke();
                return;
            }
            Enter(frame);
            if (!skip && _duration > 0 && Stage != TacticalPlaybackStage.Frame) return;
        }
    }

    private void Enter(TacticalBattleFrame frame)
    {
        if (_battle != frame.BattleId || _command != frame.CommandId)
        {
            CurrentExchange = PreviousExchange = null;
            Pose = null;
            if (_battle != frame.BattleId)
            {
                ResolvingHalfRound = false;
                _glyphs.Clear();
                _controller = null;
            }
            _battle = frame.BattleId;
            _command = frame.CommandId;
        }
        CurrentFrame = frame;
        Pose = null;
        _destination = null;
        if (frame.Kind == TacticalFrameKind.HalfRound) ResolvingHalfRound = true;
        if (frame.Kind is TacticalFrameKind.Setup or TacticalFrameKind.Reconciliation or TacticalFrameKind.Terminal)
            ResolvingHalfRound = false;
        var exchange = frame.Event is TacticalShotEvent or TacticalMeleeEvent;
        if (exchange) { PreviousExchange = CurrentExchange; CurrentExchange = frame; }

        Snapshot = frame.After;
        SetStage(TacticalPlaybackStage.Frame, Delay(frame));
        if (Travel(frame) is { } travel && GlyphAt(frame.Before, travel.Slot) is { } glyph)
        {
            Snapshot = frame.Before;
            Pose = new TacticalPose(glyph, travel.X, travel.Y, 1, false);
            _destination = (travel.ToX, travel.ToY);
            SetStage(TacticalPlaybackStage.Travel, Animation);
        }
        else if (frame.Event is TacticalRoutedEvent or TacticalCascadeRemovedEvent)
        {
            Snapshot = frame.Before;
            var slot = frame.Actor!.Slot;
            if (_glyphs.TryGetValue(slot, out var previous))
                Pose = new TacticalPose(previous, previous.X, previous.Y, 1, true);
            SetStage(TacticalPlaybackStage.RoutHold, Notice);
        }
        else if (frame.Kind is TacticalFrameKind.Initial or TacticalFrameKind.Setup or TacticalFrameKind.Controller)
        {
            var view = frame.After.View;
            var side = view.SideToMove;
            var controller = $"{side}/{view.Counter}/{view.Placed}/{view.ComputerControlled[side]}/{view.ComputerGeneral[side]}";
            if (!view.IsOver && !ResolvingHalfRound && controller != _controller)
            {
                _controller = controller;
                SetStage(TacticalPlaybackStage.Announcement, Notice);
            }
        }
        Publish();
        FrameEntered?.Invoke(frame);
        if (exchange) ExchangeEntered?.Invoke(frame);
    }

    private bool CompleteStage()
    {
        switch (Stage)
        {
            case TacticalPlaybackStage.Travel:
                Snapshot = CurrentFrame!.After;
                if (Pose is { } moving && _destination is { } end) Pose = moving with { X = end.X, Y = end.Y };
                SetStage(TacticalPlaybackStage.Arrival, Notice);
                return true;
            case TacticalPlaybackStage.RoutHold:
                SetStage(TacticalPlaybackStage.RoutFade, Pose is null ? 0 : Animation);
                return true;
            case TacticalPlaybackStage.RoutFade:
                Snapshot = CurrentFrame!.After;
                Pose = null;
                SetStage(TacticalPlaybackStage.Frame, 0);
                return true;
            case TacticalPlaybackStage.Arrival:
            case TacticalPlaybackStage.Announcement:
                Pose = null;
                SetStage(TacticalPlaybackStage.Frame, 0);
                return true;
            default:
                return false;
        }
    }

    private void SetStage(TacticalPlaybackStage stage, double duration)
    {
        Stage = stage;
        _duration = duration;
        _elapsed = 0;
        _interiorShown = false;
    }

    private void UpdatePose()
    {
        if (Pose is not { } pose) return;
        if (Stage == TacticalPlaybackStage.Travel && _destination is { } end)
            Pose = pose with { X = pose.Glyph.X + (end.X - pose.Glyph.X) * Progress, Y = pose.Glyph.Y + (end.Y - pose.Glyph.Y) * Progress };
        else if (Stage == TacticalPlaybackStage.RoutFade) Pose = pose with { Opacity = 1 - Progress };
    }

    private void Publish()
    {
        if (Snapshot is { } snapshot)
        {
            foreach (var slot in _glyphs.Keys.ToArray())
            {
                var glyph = _glyphs[slot];
                var unit = snapshot.View.Slots[slot];
                if (snapshot.PendingPlacementSlots.Contains(slot) || unit.X != glyph.X || unit.Y != glyph.Y
                    || snapshot.View.Grid[glyph.X * snapshot.Height + glyph.Y] == TacticalIcon.Empty) _glyphs.Remove(slot);
            }
            for (var slot = 0; slot < snapshot.View.Slots.Count; slot++)
                if (GlyphAt(snapshot, slot) is { } glyph) _glyphs[slot] = glyph;
        }
        PresentationChanged?.Invoke();
    }

    private static TacticalGlyph? GlyphAt(TacticalPresentationSnapshot snapshot, int slot)
    {
        if (slot < 0 || slot >= snapshot.View.Slots.Count || snapshot.PendingPlacementSlots.Contains(slot)) return null;
        var unit = snapshot.View.Slots[slot];
        if (!unit.IsLive || unit.X < 0 || unit.Y < 0 || unit.X >= snapshot.Width || unit.Y >= snapshot.Height) return null;
        var code = snapshot.View.Grid[unit.X * snapshot.Height + unit.Y];
        if (code < 0) return null;
        var type = code % TacticalIcon.DefenderOffset / TacticalIcon.SizesPerType;
        if (type >= snapshot.View.UnitTypeIds.Count) return null;
        return new TacticalGlyph(slot, unit.X, unit.Y, code, slot / Math.Max(1, snapshot.View.Slots.Count / 2), snapshot.View.UnitTypeIds[type]);
    }

    private static (int Slot, int X, int Y, int ToX, int ToY)? Travel(TacticalBattleFrame frame)
    {
        var endpoint = frame.Event switch
        {
            TacticalMovedEvent moved => (moved.Slot, moved.FromX, moved.FromY, moved.ToX, moved.ToY),
            TacticalPlacedEvent placed => (placed.Slot, placed.FromX, placed.FromY, placed.ToX, placed.ToY),
            _ => ((int Slot, int X, int Y, int ToX, int ToY)?)null,
        };
        if (endpoint is not { } e || e.X == e.ToX && e.Y == e.ToY) return null;
        bool OnBoard(int x, int y) => x >= 0 && y >= 0 && x < frame.Before.Width && y < frame.Before.Height;
        if (!OnBoard(e.X, e.Y) || !OnBoard(e.ToX, e.ToY)) return null;
        var before = frame.Before.View.Slots[e.Slot];
        var after = frame.After.View.Slots[e.Slot];
        return before.X == e.X && before.Y == e.Y && after.X == e.ToX && after.Y == e.ToY ? e : null;
    }

    private double Delay(TacticalBattleFrame frame) => frame.Kind switch
    {
        TacticalFrameKind.Shot => BattlePauses.SnapShot(ShotDelay),
        TacticalFrameKind.Melee => BattlePauses.SnapAttack(AttackDelay),
        // [designed] Brief housekeeping holds are capped, keeping high exchange pauses usable.
        TacticalFrameKind.Setup or TacticalFrameKind.Controller or TacticalFrameKind.Terminal or TacticalFrameKind.Reconciliation
            or TacticalFrameKind.Removal => Math.Min(0.5, Pace),
        _ => 0,
    };
}
