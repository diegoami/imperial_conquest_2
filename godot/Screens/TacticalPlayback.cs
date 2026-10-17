using IC2.Engine.Battle.Tactical;
using IC2.Engine.Presentation;

namespace IC2.Slice.Screens;

/// <summary>Display-only traversal of the producer's immutable frames. Never submits gameplay orders.</summary>
public sealed class TacticalPlayback
{
    private readonly Queue<TacticalBattleFrame> _pending = new();
    private readonly HashSet<(string Battle, long Frame)> _seen = new();
    private Action? _completed;
    private double _remaining;
    private string? _battle;
    private long _command = -1;

    /// <summary>Every accepted frame, including zero-delay and skipped waits, enters through this event once.</summary>
    public event Action<TacticalBattleFrame>? FrameEntered;

    /// <summary>T183's typed exchange-entry seam. One notification per Shot/Melee frame; no audio here.</summary>
    public event Action<TacticalBattleFrame>? ExchangeEntered;

    public TacticalBattleFrame? CurrentFrame { get; private set; }
    public TacticalBattleFrame? CurrentExchange { get; private set; }
    public TacticalBattleFrame? PreviousExchange { get; private set; }
    public bool IsPlaying { get; private set; }

    /// <summary>The engine's HalfRound event precedes its melee. Setup, not that event, releases the next controller.</summary>
    public bool ResolvingHalfRound { get; private set; }
    public double ShotDelay { get; set; }
    public double AttackDelay { get; set; }

    /// <summary>Accepts one completed command or opening peek. Repeated peek/Last/output frames are ignored.</summary>
    public void Begin(IEnumerable<TacticalBattleFrame> frames, Action? completed = null)
    {
        if (IsPlaying) throw new InvalidOperationException("Finish the displayed command before starting another.");
        foreach (var frame in frames)
        {
            if (_seen.Add((frame.BattleId, frame.FrameId))) _pending.Enqueue(frame);
        }

        _completed = completed;
        IsPlaying = true;
        _remaining = 0;
        Advance(0);
    }

    public void Advance(double seconds)
    {
        if (!IsPlaying) return;
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        _remaining -= seconds;
        Traverse(false);
    }

    /// <summary>Skips waits, never frames or notifications. Completion runs after final reconciliation.</summary>
    public void Finish() => Traverse(true);

    private void Traverse(bool skipWaits)
    {
        while (IsPlaying && (skipWaits || _remaining <= 0))
        {
            if (!_pending.TryDequeue(out var frame))
            {
                IsPlaying = false;
                var completed = _completed;
                _completed = null;
                completed?.Invoke();
                return;
            }

            if (_battle != frame.BattleId || _command != frame.CommandId)
            {
                CurrentExchange = PreviousExchange = null;
                if (_battle != frame.BattleId) ResolvingHalfRound = false;
                _battle = frame.BattleId;
                _command = frame.CommandId;
            }

            CurrentFrame = frame;
            if (frame.Kind == TacticalFrameKind.HalfRound) ResolvingHalfRound = true;
            if (frame.Kind is TacticalFrameKind.Setup or TacticalFrameKind.Reconciliation or TacticalFrameKind.Terminal)
                ResolvingHalfRound = false;
            var exchange = frame.Event is TacticalShotEvent or TacticalMeleeEvent;
            if (exchange)
            {
                PreviousExchange = CurrentExchange;
                CurrentExchange = frame;
            }

            FrameEntered?.Invoke(frame);
            if (exchange) ExchangeEntered?.Invoke(frame);
            _remaining += Delay(frame);
        }
    }

    private double Delay(TacticalBattleFrame frame)
    {
        var shot = BattlePauses.SnapShot(ShotDelay);
        var attack = BattlePauses.SnapAttack(AttackDelay);
        return frame.Kind switch
        {
            TacticalFrameKind.Shot => shot,
            TacticalFrameKind.Melee => attack,
            TacticalFrameKind.Movement or TacticalFrameKind.Placement => Math.Min(0.15, Math.Max(shot, attack)),
            TacticalFrameKind.Setup or TacticalFrameKind.Controller or TacticalFrameKind.Terminal or TacticalFrameKind.Reconciliation => Math.Min(0.5, Math.Max(shot, attack)),
            _ => 0,
        };
    }
}
