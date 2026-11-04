using IC2.Engine.Model;

namespace IC2.Engine.Presentation;

public sealed partial class GameSession
{
    /// <summary>An all-AI session: inspection and saving remain available, player orders do not.</summary>
    public bool IsObserverMode => _isWatchMode
        && !State.Nations.Any(n => n.Control == SeatControl.Human && !n.Eliminated);

    private IReadOnlyList<string> HandleObserve()
    {
        if (_battle is not null || _lap is not null)
            return ["Finish the tactical battle before entering observer mode."];
        if (_pendingPeaceTreatyOffers.Count > 0 || State.PendingOffer is not null)
            return ["Resolve pending offers before entering observer mode."];
        if (IsObserverMode) return ["Observer mode is already active."];
        State = State with { Nations = ValueList.From(State.Nations.Select(n => n with { Control = SeatControl.Ai })) };
        _humanSeatNationId = null;
        _isWatchMode = true;
        _seatLost = false;
        _pendingPrelude = null;
        _pendingNewsBaseline = null;
        return ["Observer mode: all nations are computer controlled. Observation starts paused."];
    }

    private IReadOnlyList<string> HandleObserverStep()
    {
        if (!IsObserverMode) return ["Observer stepping requires an all-AI observer campaign."];
        var lines = new List<string>();
        var before = State;
        PlaySeatTurn(lines, new AiLap(State.ActiveNationId, new HashSet<string>(StringComparer.Ordinal), null, null));
        if (State.Calendar.TurnIndex != before.Calendar.TurnIndex)
            AppendRoundFooter(lines, before.NewsLog.Slots);
        return lines;
    }

    private IReadOnlyList<string> HandleTakeControl(string[] tokens)
    {
        if (!IsObserverMode) return ["Take control is available only in observer mode."];
        if (tokens.Length != 2 || State.NationById(tokens[1]) is not { Eliminated: false } nation
            || nation.Id == Scenario.Barbarians?.NationId)
            return ["Choose a surviving playable nation to control."];
        State = State with { Nations = ValueList.From(State.Nations.Select(n => n with {
            Control = n.Id == nation.Id ? SeatControl.Human : SeatControl.Ai })) };
        _humanSeatNationId = nation.Id;
        _isWatchMode = false;
        _seatLost = false;
        _pendingNewsBaseline = State.NewsLog.Slots;
        var lines = new List<string> { $"Taking control of {nation.Name} at its next turn." };
        if (AdvanceToHumanSeat() is { } advance) lines.AddRange(advance);
        return lines;
    }
}
