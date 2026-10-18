using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical.General;

/// <summary>FUN_0043A718 report §C: synthetic rule probes, not original runtime histories.</summary>
public class ThreatSelectionTests
{
    [Theory]
    [InlineData(1u)]
    [InlineData(17u)]
    public void Blocked_first_candidate_traverses_to_the_selection_sort_equal_key_candidate(uint seed)
    {
        // Corner23 is boxed in by20/21/22. After skipping it, one-swap sorting visits21;
        // immediate exchanges visit22. Both have key350 but different aims.
        var state = Arena((0, Unit(HI, 13, 0, 3000)),
            (20, Unit(HI, 13, 10, 3000)), (21, Unit(HI, 12, 10, 2000)),
            (22, Unit(HI, 12, 11, 2000)), (23, Unit(HI, 13, 11, 1000)));
        var journal = new List<GeneralDecision>();
        var after = GeneralPassThree.ThreatMove(state, Context, new DelphiBattleDraws(seed), journal);
        Assert.Equal(new GeneralDecision(0, GeneralStep.ThreatLine, 21, 12, 10), Assert.Single(journal));
        Assert.Equal(new[] { (13, 1), (13, 2) }, GeneralTestbed.Path(after, 0));
        Assert.Empty(DrawsOn(after));
        var repeatedJournal = new List<GeneralDecision>();
        Assert.Equal(after, GeneralPassThree.ThreatMove(state, Context, new DelphiBattleDraws(seed), repeatedJournal));
        Assert.Equal(journal, repeatedJournal);
    }

    [Theory]
    [InlineData(new[] { 3000, 2000, 2000, 1000 }, new[] { 23, 21, 22, 20 })]
    [InlineData(new[] { 4000, 2000, 4000, 2000 }, new[] { 21, 23, 22, 20 })]
    [InlineData(new[] { 1000, 3000, 3000, 2000, 1000 }, new[] { 20, 24, 23, 22, 21 })]
    [InlineData(new[] { 2000, 2000, 2000, 2000 }, new[] { 20, 21, 22, 23 })]
    public void First_strict_minimum_swaps_once_per_suffix(int[] troops, int[] expected)
    {
        var units = troops.Select((n, i) => (D0 + i, Unit(HI, 9 + i, 10, n))).ToList();
        units.Add((0, Unit(HI, 0, 0, 3000)));
        Assert.Equal(expected, GeneralPassThree.ThreatOrder(Arena(units), 0, Context));
    }

    [Fact]
    public void Dead_first_entry_participates_in_swaps_before_actionable_prefix_is_returned()
    {
        // Full table: dead,a=350,b=350,c=175 -> c,a,b,dead. Prefiltering gives c,b,a.
        var state = Arena((0, Unit(HI, 0, 0, 3000)),
            (21, Unit(HI, 10, 10, 2000)), (22, Unit(HI, 11, 10, 2000)),
            (23, Unit(HI, 12, 10, 1000)));
        Assert.Equal(new[] { 23, 21, 22 }, GeneralPassThree.ThreatOrder(state, 0, Context));
    }

    private static TacticalContext Wide()
    {
        var r = ToyRuleset;
        return TacticalContext.From(r with { Combat = r.Combat with {
            Tactical = r.Combat.Tactical with { WideArithmetic = true } } });
    }

    [Theory]
    [InlineData(999999, true)]
    [InlineData(1000000, false)]
    [InlineData(1000001, false)]
    public void Live_threshold_and_dead_sentinels_control_actual_movement(int key, bool actionable)
    {
        // HI->HI matrix5; quality2000, morale1, divisor10000 makes the wide score exactly troops.
        var context = Wide();
        var state = Arena((0, Unit(HI, 0, 0, 3000, moves: 1)),
            (39, Unit(HI, 10, 10, key, quality: 2000, morale: 1)));
        Assert.Equal(key, GeneralPassThree.Threat(state, 0, 39, context));
        Assert.Equal(actionable ? new[] { 39 } : Array.Empty<int>(), GeneralPassThree.ThreatOrder(state, 0, context));
        var journal = new List<GeneralDecision>();
        var after = GeneralPassThree.ThreatMove(state, context, NoDraws.Instance, journal);
        if (actionable)
            Assert.Equal(39, Assert.Single(journal).Enemy);
        else
        {
            Assert.Empty(journal);
            Assert.Equal(state, after);
        }
    }

    [Fact]
    public void Empty_enemy_table_cannot_move_toward_dead_slots()
    {
        var state = Arena((0, Unit(HI, 0, 0, 3000)));
        Assert.Empty(GeneralPassThree.ThreatOrder(state, 0, Context));
        Assert.Equal(state, GeneralPassThree.ThreatMove(state, Context, NoDraws.Instance));
    }

    [Fact]
    public void Ranged_negative_keys_are_actionable()
    {
        var state = Arena((0, Unit(AR, 0, 0, 3000, shots: 1)), (39, Unit(HI, 10, 10, 1000)));
        Assert.True(GeneralPassThree.Threat(state, 0, 39, Context) < 0);
        Assert.Equal(new[] { 39 }, GeneralPassThree.ThreatOrder(state, 0, Context));
        var journal = new List<GeneralDecision>();
        GeneralPassThree.ThreatMove(state, Context, NoDraws.Instance, journal);
        Assert.Equal(39, Assert.Single(journal).Enemy);
    }
}
