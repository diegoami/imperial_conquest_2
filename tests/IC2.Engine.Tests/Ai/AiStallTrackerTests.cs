using IC2.Engine.Ai;
using Xunit;

namespace IC2.Engine.Tests.Ai;

/// <summary>#1290: a stall is a full round of live seats all idle, not two idle seat-turns in a row.</summary>
public sealed class AiStallTrackerTests
{
    private static readonly string[] Seats =
        { "rome", "carthage", "seleucid", "ptolemaic", "macedonia", "numidia", "gaul", "greece",
          "celtiberia", "illyria", "dacia", "bithynia", "galatia", "armenia", "media", "thracia" };

    [Fact]
    public void A_synthetic_frozen_game_all_seats_idle_for_a_full_round_is_a_full_round_stall()
    {
        var tracker = new AiStallTracker();
        foreach (var seat in Seats)
        {
            tracker.Record(seat, idle: true, liveSeats: Seats.Length);
        }

        Assert.Equal(1, tracker.FullRoundStalls);
        Assert.Equal(Seats.Length, tracker.LongestRun);
        Assert.All(tracker.IdleBySeat, r => Assert.Equal(1, r.IdleTurns));
    }

    [Fact]
    public void Each_further_frozen_round_counts_again()
    {
        var tracker = new AiStallTracker();
        for (var round = 0; round < 3; round++)
        {
            foreach (var seat in Seats)
            {
                tracker.Record(seat, idle: true, liveSeats: Seats.Length);
            }
        }

        Assert.Equal(3, tracker.FullRoundStalls);
    }

    [Fact]
    public void Issue_268s_pattern_isolated_idle_seats_in_runs_of_2_to_5_is_not_a_stall()
    {
        // Armenia, Illyria, Dacia and Numidia, with no armies after spending a seasonal payout, idle every
        // round. Runs of 2 to 5 consecutive idle seat-turns occur when idle seats are neighbours in turn order.
        var idleSeats = new HashSet<string> { "numidia", "gaul", "illyria", "dacia", "bithynia", "armenia" };
        var tracker = new AiStallTracker();
        for (var round = 0; round < 10; round++)
        {
            foreach (var seat in Seats)
            {
                tracker.Record(seat, idle: idleSeats.Contains(seat), liveSeats: Seats.Length);
            }
        }

        Assert.Equal(0, tracker.FullRoundStalls);
        Assert.InRange(tracker.LongestRun, 2, 5);
        Assert.Equal(idleSeats.Count, tracker.IdleBySeat.Count);
        Assert.All(tracker.IdleBySeat, r => Assert.Equal(10, r.IdleTurns));
    }

    [Fact]
    public void A_round_broken_by_one_active_seat_is_not_a_stall()
    {
        // 15 idle seat-turns, one active seat, 15 more: two runs one short of a full round each.
        var tracker = new AiStallTracker();
        for (var i = 0; i < (Seats.Length * 2) - 1; i++)
        {
            tracker.Record(Seats[i % Seats.Length], idle: i != Seats.Length - 1, liveSeats: Seats.Length);
        }

        Assert.Equal(0, tracker.FullRoundStalls);
        Assert.Equal(Seats.Length - 1, tracker.LongestRun);
    }

    [Fact]
    public void With_fewer_live_seats_a_shorter_run_is_a_full_round() =>
        Assert.Equal(1, Run(new[] { true, true, true }, liveSeats: 3).FullRoundStalls);

    private static AiStallTracker Run(bool[] idle, int liveSeats)
    {
        var tracker = new AiStallTracker();
        for (var i = 0; i < idle.Length; i++)
        {
            tracker.Record("seat-" + i, idle[i], liveSeats);
        }

        return tracker;
    }
}
