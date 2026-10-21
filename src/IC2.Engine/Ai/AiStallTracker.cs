namespace IC2.Engine.Ai;

/// <summary>One seat's idle seat-turns over a whole game, for a soak's report.</summary>
/// <param name="SeatId">The nation.</param>
/// <param name="IdleTurns">Its seat-turns that issued no command and changed nothing substantive.</param>
public sealed record AiSeatIdleRecord(string SeatId, int IdleTurns);

/// <summary>
/// Issue #1290 (the user's decision, which closed #268): the soak's stall rule. A <strong>full-round
/// stall</strong> is a complete round in which every live seat issues no command and the state does not
/// substantively change. That is, a run of consecutive idle seat-turns at least as long as the number of
/// nations still in the game. Two idle neighbours in turn order are not a stall. On the 16-nation
/// classical map a nation with no armies that spent its season's payout has no affordable action until
/// the next payout, which is legal play. So <see cref="LongestRun"/> and the per-seat idle record are
/// reported, and only <see cref="FullRoundStalls"/> fails a soak.
/// </summary>
public sealed class AiStallTracker
{
    private readonly List<AiSeatIdleRecord> _idleBySeat = new();
    private int _run;

    /// <summary>The longest run of consecutive idle seat-turns: reported, not a failure.</summary>
    public int LongestRun { get; private set; }

    /// <summary>How many times the current run reached a full round of live seats. Required to be zero.</summary>
    public int FullRoundStalls { get; private set; }

    /// <summary>Every seat with at least one idle seat-turn, in the order each first went idle.</summary>
    public IReadOnlyList<AiSeatIdleRecord> IdleBySeat => _idleBySeat;

    /// <summary>
    /// Records one seat-turn. <paramref name="idle"/> is true when the turn issued no command and left
    /// <see cref="AiSubstantiveState"/> unchanged; <paramref name="liveSeats"/> is how many nations are still
    /// in the game after it. Returns the current run length (zero for an active turn).
    /// </summary>
    public int Record(string seatId, bool idle, int liveSeats)
    {
        ArgumentNullException.ThrowIfNull(seatId);
        if (!idle)
        {
            _run = 0;
            return 0;
        }

        _run++;
        LongestRun = Math.Max(LongestRun, _run);
        var index = _idleBySeat.FindIndex(r => string.Equals(r.SeatId, seatId, StringComparison.Ordinal));
        if (index < 0)
        {
            _idleBySeat.Add(new AiSeatIdleRecord(seatId, 1));
        }
        else
        {
            _idleBySeat[index] = _idleBySeat[index] with { IdleTurns = _idleBySeat[index].IdleTurns + 1 };
        }

        // Counted once per full round of idleness, so a frozen game that idles for three rounds counts 3.
        if (liveSeats > 0 && _run % liveSeats == 0)
        {
            FullRoundStalls++;
        }

        return _run;
    }
}
