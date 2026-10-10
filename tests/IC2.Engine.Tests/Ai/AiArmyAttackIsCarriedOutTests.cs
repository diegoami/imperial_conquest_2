using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T156 (issue #925) Done-when 5: on a fixed hand-built state in which the tree selects <em>attack the
/// army</em> (a weaker enemy field army within 7, at war), the AI issues the attack, or the march toward
/// it, and the engine accepts it. The assertions name the command, so a tree whose army-attack branch
/// issued nothing (leaving the garrison fallback to move the army somewhere else) fails them.
/// </summary>
public sealed class AiArmyAttackIsCarriedOutTests
{
    private static GameState Build(int enemyX, int enemyY)
    {
        var state = BattleCommandTestbed.StateWith(
            [
                CaptureFixtures.Nation("north", capitalCityId: "n-cap") with { Personality = AiScriptedStates.DefaultPersonality },
                CaptureFixtures.Nation("south", capitalCityId: "s-cap") with { Personality = AiScriptedStates.DefaultPersonality },
            ],
            [
                CaptureFixtures.City("n-cap", "n-cap", 1, 1, "north", "north", 90, 100, 100, 100, 0),
                CaptureFixtures.City("s-cap", "s-cap", 6, 5, "south", "south", 90, 100, 100, 100, 0),
            ],
            [
                CaptureFixtures.Army("attacker", "north", 3, 3, 60, CaptureFixtures.Unit("light_infantry", 40_000)) with { Moves = 9 },
                CaptureFixtures.Army("target", "south", enemyX, enemyY, 60, CaptureFixtures.Unit("light_infantry", 400)) with { Moves = 5 },
            ]);
        state = state with
        {
            Relations = state.Relations.WithRelation("north", "south", AiScriptedStates.Ruleset.Diplomacy.StateCodes.War),
        };
        return AiScriptedStates.WithActiveSeat(state, "north");
    }

    [Fact]
    public void An_adjacent_weaker_enemy_army_is_attacked_and_the_engine_accepts_it()
    {
        var driven = AiScriptedStates.DriveOneTurn(Build(4, 3), 1UL);

        Assert.Equal(0, driven.Outcome.CommandsRejected);
        Assert.Equal(0, driven.Outcome.ProjectionMismatches);
        Assert.Contains("battle.attack-army", driven.IssuedKinds);
        Assert.Contains(driven.Outcome.Log, line => line.Contains("(target tree)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_weaker_enemy_army_within_seven_but_not_adjacent_is_marched_at_and_the_engine_accepts_it()
    {
        var driven = AiScriptedStates.DriveOneTurn(Build(6, 3), 1UL);

        Assert.Equal(0, driven.Outcome.CommandsRejected);
        Assert.Contains(driven.Outcome.Log, line => line.Contains("approach target army (6, 3) with attacker", StringComparison.Ordinal));
        Assert.Contains("movement.move-army", driven.IssuedKinds);
        var attacker = driven.Outcome.State.ArmyById("attacker")!;
        Assert.NotEqual((3, 3), (attacker.X, attacker.Y));
    }
}
