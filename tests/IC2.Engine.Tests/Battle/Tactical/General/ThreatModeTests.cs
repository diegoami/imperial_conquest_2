using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using IC2.Engine.Model;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.General.GeneralTestbed;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical.General;

/// <summary>FUN_0043A718, target/draw report §B: synthetic local rule probes, not original battle histories.</summary>
public class ThreatModeTests
{
    public static IEnumerable<object[]> Modes()
    {
        foreach (var actorType in new[] { LI, AR })
        foreach (var actorShots in new[] { 0, 1 })
        foreach (var enemyType in new[] { LI, AR })
        foreach (var enemyShots in new[] { 0, 1 })
            yield return new object[] { actorType, actorShots, enemyType, enemyShots };
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Only_the_actor_archer_with_positive_shots_selects_negative_actor_to_enemy_bound(
        int actorType, int actorShots, int enemyType, int enemyShots)
    {
        var state = Arena((0, Unit(actorType, 5, 5, 3000, quality: 1, morale: 100, shots: actorShots)),
            (D0, Unit(enemyType, 5, 6, 1000, quality: 2, morale: 80, shots: enemyShots)));
        // Actor bound: 3000*1*100*18/165000 = 327; doubled for AR distance1<2 to654, capped at target1000/2=500, +1 =501.
        // Reversed bound: 1000*2*80*18/155000 =18 (AR doubles to36), +1:19 or37.
        // Ordinary mode remains the weighted score with the enemy-to-actor matrix.
        var ranged = actorType == AR && actorShots > 0;
        var expected = ranged ? -501 : GeneralTargeting.Score(state, 0, D0, Context);
        Assert.Equal(expected, GeneralPassThree.Threat(state, 0, D0, Context));
        if (ranged)
            Assert.NotEqual(-TacticalShooting.Bound(state.Slots[D0], state.Slots[0], 1, Context), expected);
    }

    [Fact]
    public void Issue_1165_ordinary_LI_ignores_enemy_archer_shots_and_aims_at_LI21()
    {
        var state = Arena(new[] {
            (0, Unit(LI, 5, 5, 3000, quality: 1, morale: 100, moves: 1, shots: 0)),
            (D0, Unit(AR, 5, 6, 3000, quality: 1, morale: 100, shots: 1)),
            (D0 + 1, Unit(LI, 6, 6, 1000, quality: 1, morale: 100, shots: 0))
        }, attackerGeneral: true);
        Assert.Equal(300, GeneralPassThree.Threat(state, 0, D0, Context));
        Assert.Equal(150, GeneralPassThree.Threat(state, 0, D0 + 1, Context));
        Assert.Equal(new[] { D0 + 1, D0 }, GeneralPassThree.ThreatOrder(state, 0, Context));
        var journal = new List<GeneralDecision>();
        GeneralPassThree.ThreatMove(state, Context, NoDraws.Instance, journal);
        Assert.Equal(new GeneralDecision(0, GeneralStep.ThreatLine, D0 + 1, 6, 6), Assert.Single(journal));
    }

    [Theory]
    [InlineData(0, 4, 5)]
    [InlineData(1, 3, 3)]
    public void Threat_box_uses_actor_shots_and_preserves_first_dx_then_dy_tie(int shots, int x, int y)
    {
        // Equivalent minimal discriminating board for the reported exhausted-archer phase inputs:
        // historical model phase inputs are not SAV fields and are not part of the fixture checkout.
        // The diagonal is blocked at4,4. Radius1's first nearest cell is4,5 (5,4 ties);
        // radius2's unique nearest cell is3,3. Four moves differentiate the full Walk path too.
        var state = Blocked(shots);
        var journal = new List<GeneralDecision>();
        var after = GeneralPassThree.ThreatMove(state, Context, NoDraws.Instance, journal);
        Assert.Equal(new GeneralDecision(0, GeneralStep.ThreatBox, D0, x, y), Assert.Single(journal));
        Assert.Equal(shots == 0 ? new[] { (1, 1), (2, 2), (2, 3), (3, 4) }
            : new[] { (1, 1), (2, 2), (3, 3) }, Path(after, 0));
        Assert.Equal(shots == 0 ? 0 : 1, after.Slots[0].Moves);
        Assert.Empty(DrawsOn(after));
    }

    internal static TacticalBattleState Blocked(int shots) => Arena(new[] {
        (0, Unit(AR, 0, 0, 1500, moves: 4, shots: shots)),
        (1, Unit(HI, 4, 4, 3000, moves: 0)), (D0, Unit(HI, 5, 5, 4000))
    }, attackerGeneral: true);

    [Theory]
    [InlineData(0, 1u)]
    [InlineData(1, 1u)]
    [InlineData(0, 17u)]
    [InlineData(1, 17u)]
    public void Blocked_threat_has_exact_full_state_and_zero_ordered_draws_for_repeated_seeds(int shots, uint seed)
    {
        var state = Blocked(shots);
        var draws = new DelphiBattleDraws(seed);
        var journal = new List<GeneralDecision>();
        var after = GeneralPassThree.ThreatMove(state, Context, draws, journal);
        var repeatDraws = new DelphiBattleDraws(seed);
        Assert.Equal(after, GeneralPassThree.ThreatMove(state, Context, repeatDraws));
        Assert.Equal(seed, draws.RandSeed);
        Assert.Equal(seed, repeatDraws.RandSeed);
        Assert.Empty(DrawsOn(after));
        AssertGridMatchesSlots(after);
    }
}
