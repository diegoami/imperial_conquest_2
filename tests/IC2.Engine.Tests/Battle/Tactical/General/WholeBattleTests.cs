using System.Text.Json;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.General.GeneralTestbed;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical.General;

/// <summary>
/// T124 Done-when 5: with this general on both sides and a replayed draw source (the original's own
/// generator, <see cref="DelphiBattleDraws"/>), a battle between two mixed armies ends, one side empty,
/// within a bounded number of half-rounds, and two runs give byte-identical traces. A liveness check, not
/// a fidelity one (T129's).
/// </summary>
public class WholeBattleTests
{
    private const int MaxHalfRounds = 400;

    private static TacticalSlot[] Mixed(int scale) =>
    [
        Unit(LI, 0, 0, 6000 * scale, quality: 5),
        Unit(HI, 0, 0, 3000 * scale, quality: 6),
        Unit(AR, 0, 0, 1500 * scale, quality: 5),
        Unit(LC, 0, 0, 2500 * scale, quality: 5),
        Unit(HC, 0, 0, 1000 * scale, quality: 6),
        Unit(HI, 0, 0, 2500 * scale, quality: 5),
        Unit(LI, 0, 0, 4000 * scale, quality: 4),
        Unit(AR, 0, 0, 1200 * scale, quality: 4),
    ];

    public static TheoryData<uint> Seeds => new() { 1u, 12345u, 0xC0FFEEu, 987654321u };

    [Theory]
    [MemberData(nameof(Seeds))]
    public void A_battle_between_two_computer_generals_ends_with_one_side_empty(uint seed)
    {
        var start = FreshBattle(Mixed(1), Mixed(1));

        var (end, halfRounds) = RunBounded(start, new DelphiBattleDraws(seed), new ComputerGeneral(), MaxHalfRounds);

        Assert.True(end.IsOver, $"seed {seed}: not over after {halfRounds} half-rounds");
        Assert.True(halfRounds < MaxHalfRounds);
        Assert.False(end.HasLiveUnit(TacticalBattleState.AttackerSide) && end.HasLiveUnit(TacticalBattleState.DefenderSide));
        Assert.Single(end.Log.OfType<TacticalBattleOverEvent>());
        AssertGridMatchesSlots(end);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Two_runs_give_byte_identical_traces(uint seed)
    {
        var start = FreshBattle(Mixed(1), Mixed(2));
        var journalA = new List<GeneralDecision>();
        var journalB = new List<GeneralDecision>();

        var (a, _) = RunBounded(start, new DelphiBattleDraws(seed), new ComputerGeneral(journalA), MaxHalfRounds);
        var (b, _) = RunBounded(start, new DelphiBattleDraws(seed), new ComputerGeneral(journalB), MaxHalfRounds);

        Assert.True(a.IsOver);
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(a), JsonSerializer.SerializeToUtf8Bytes(b));
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(journalA), JsonSerializer.SerializeToUtf8Bytes(journalB));
        Assert.Equal(a, b);
    }

    [Fact]
    public void The_driver_itself_plays_the_battle_to_its_end()
    {
        // The same battle through T123's own loop: it returns only when the battle is over, since both sides
        // are computer-driven. The bounded run above has already shown this seed ends.
        var start = FreshBattle(Mixed(1), Mixed(1));
        var bounded = RunBounded(start, new DelphiBattleDraws(12345u), new ComputerGeneral(), MaxHalfRounds).State;

        var driven = TacticalDriver.Run(start, Context, new DelphiBattleDraws(12345u), new ComputerGeneral());

        Assert.True(driven.IsOver);
        Assert.Equal(bounded, driven);
    }
}
