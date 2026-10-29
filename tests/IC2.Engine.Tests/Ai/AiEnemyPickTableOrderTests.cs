using IC2.Engine.Ai;
using IC2.Engine.Armies;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;
using Fx = IC2.Engine.Tests.Ai.AiReachableFallbackTests;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T243 (#1216, the owner's decision of 2026-10-30): the enemy-army pick (<c>FUN_0044ee60</c>) replaces its choice only
/// on a strictly greater score, so under the original AI a tie goes to the first army in table order, which T240 makes
/// <see cref="GameState.Armies"/>. The designed AI keeps T156's ID tie-break.
/// </summary>
public sealed class AiEnemyPickTableOrderTests
{
    private const string Us = "north";
    private const string Them = "south";

    private static Ruleset WithAi(AiEconomyModel model)
    {
        var toy = AiScriptedStates.Ruleset;
        return toy with { Flags = toy.Flags with { AiEconomy = model } };
    }

    /// <summary>
    /// Our army at (10, 10) and two equal enemy armies three tiles away on either side, so their scores tie; the
    /// enemies are listed in <paramref name="enemies"/>' order, after ours.
    /// </summary>
    private static (GameState State, ArmyState Army) TwoEqualEnemies(params string[] enemies)
    {
        var army = Fx.Army(10, 10, moves: 5, troops: 40_000);
        var spots = new[] { (7, 10), (10, 7), (13, 10) };
        var armies = new List<ArmyState> { army };
        for (var i = 0; i < enemies.Length; i++)
        {
            armies.Add(CaptureFixtures.Army(enemies[i], Them, spots[i].Item1, spots[i].Item2, 60,
                CaptureFixtures.Unit("light_infantry", 10_000)));
        }

        var state = AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.StateWith(
                [
                    CaptureFixtures.Nation(Us, treasury: 1_000, capitalCityId: "n-cap")
                        with { Personality = AiScriptedStates.DefaultPersonality },
                    CaptureFixtures.Nation(Them, capitalCityId: "s-cap")
                        with { Personality = AiScriptedStates.DefaultPersonality },
                ],
                [Fx.City("n-cap", Us, 0, 0, supply: 5_000), Fx.City("s-cap", Them, 30, 30)],
                armies.ToArray()),
            Us);
        state = state with
        {
            Relations = state.Relations.WithRelation(Us, Them, AiScriptedStates.Ruleset.Diplomacy.StateCodes.War),
        };
        return (state, army);
    }

    private static AiArmyTargetTree.ArmyTarget Pick(GameState state, ArmyState army, AiEconomyModel model) =>
        AiArmyTargetTree.ScoreArmyTarget(new AiView(state, WithAi(model), Fx.Terrain(), Us), army);

    [Fact]
    public void The_two_enemies_tie()
    {
        var (state, army) = TwoEqualEnemies("army-9", "army-10");
        var view = new AiView(state, WithAi(AiEconomyModel.Original), Fx.Terrain(), Us);
        var first = AiArmyTargetTree.ScoreArmyTarget(view, army);
        var swapped = TwoEqualEnemies("army-10", "army-9");
        var second = AiArmyTargetTree.ScoreArmyTarget(
            new AiView(swapped.State, WithAi(AiEconomyModel.Original), Fx.Terrain(), Us), swapped.Army);

        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.Distance, second.Distance);
        Assert.NotEqual(first.Army!.Id, second.Army!.Id);
    }

    [Fact]
    public void A_tie_goes_to_the_first_in_table_order_under_the_original_ai_and_to_the_lower_id_under_the_designed_ai()
    {
        // army-9 comes first in the table; ordinally "army-10" < "army-9".
        var (state, army) = TwoEqualEnemies("army-9", "army-10");

        Assert.Equal("army-9", Pick(state, army, AiEconomyModel.Original).Army!.Id);
        Assert.Equal("army-10", Pick(state, army, AiEconomyModel.Designed).Army!.Id);
    }

    [Fact]
    public void When_table_order_and_id_order_agree_both_ais_pick_the_same_army()
    {
        var (state, army) = TwoEqualEnemies("army-10", "army-9");

        Assert.Equal("army-10", Pick(state, army, AiEconomyModel.Original).Army!.Id);
        Assert.Equal("army-10", Pick(state, army, AiEconomyModel.Designed).Army!.Id);
    }

    [Fact]
    public void After_a_sweep_moves_the_last_army_forward_the_original_ai_follows_the_new_table_order()
    {
        // Table: ours, e-a (dead), e-b, e-c. The sweep moves e-c into e-a's slot: ours, e-c, e-b. e-b and e-c tie.
        var (state, army) = TwoEqualEnemies("e-b", "e-c");
        state = state with { Armies = ValueList.From(state.Armies), DeadArmySlots = ValueList.Of(1) };
        Assert.Equal(new[] { army.Id, "e-b", "e-c" }, state.Armies.Select(a => a.Id));

        Assert.Equal("e-b", Pick(state, army, AiEconomyModel.Original).Army!.Id);

        var swept = ArmyTable.Sweep(state);
        Assert.Equal(new[] { army.Id, "e-c", "e-b" }, swept.Armies.Select(a => a.Id));

        Assert.Equal("e-c", Pick(swept, army, AiEconomyModel.Original).Army!.Id);
        Assert.Equal("e-b", Pick(swept, army, AiEconomyModel.Designed).Army!.Id);
    }
}
