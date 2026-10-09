using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// T25 (plan #474): <see cref="GameSession.Submit"/>'s own new <see cref="SessionOutput.Battles"/> and
/// <see cref="GameSession.LastBattles"/> — every <see cref="Battle.BattleResult"/> a
/// <see cref="Battle.BattleResolved"/> event published while that one <c>Submit</c> call ran, exposed
/// without changing what the engine computes or prints (the CLI goldens stay byte-identical — see
/// <c>SuccessScriptTests</c>/<c>GoldenTranscriptBlocks</c>, untouched by this task).
/// </summary>
public sealed class GameSessionBattleResultsTests
{
    [Fact]
    public void Submit_exposes_the_BattleResult_a_human_attack_resolved()
    {
        var session = BattleFixture();

        var output = session.Submit("attack-army north-army-1 south-army-1");

        var battle = Assert.Single(output.Battles);
        Assert.Equal("north-army-1", battle.AttackerId);
        Assert.Equal("south-army-1", battle.DefenderId);
        Assert.Equal("north", battle.AttackerNationId);
        Assert.Equal("south", battle.DefenderNationId);
    }

    [Fact]
    public void LastBattles_mirrors_the_same_Submit_calls_own_Battles()
    {
        var session = BattleFixture();

        var output = session.Submit("attack-army north-army-1 south-army-1");

        Assert.Same(output.Battles[0], session.LastBattles[0]);
        Assert.Single(session.LastBattles);
    }

    [Fact]
    public void An_ordinary_command_that_resolves_no_battle_reports_none()
    {
        var session = BattleFixture();

        var output = session.Submit("status");

        Assert.Empty(output.Battles);
        Assert.Empty(session.LastBattles);
    }

    [Fact]
    public void LastBattles_does_not_leak_into_a_later_Submit_that_resolves_no_battle()
    {
        var session = BattleFixture();
        session.Submit("attack-army north-army-1 south-army-1");
        Assert.NotEmpty(session.LastBattles);

        session.Submit("status");

        Assert.Empty(session.LastBattles);
    }

    /// <summary>
    /// Rework round 1 (PR #476 review, gate 5, blocking): the capture site inside
    /// <c>PlayUntilOneFullLapOrRepeat</c> (an AI seat's own turn, played from <c>end</c>) had no test —
    /// every test above only ever drives a battle through <c>IssueCommand</c>'s own site (a human-issued
    /// <c>attack-army</c>). This is the same deterministic AI-attacks-on-its-own-initiative fixture
    /// <c>PeaceTreatyOfferTests.ABattleInsideAnAiSeatsOwnTurn_AlsoCapturesTheOffer</c> already proved
    /// reachable (that test's own remarks: "south's own AI turn (on end) then attacks the adjacent, weaker
    /// north-army-1 on its own initiative and wins" — <c>AttackLegality</c> gates only the attacker's own
    /// moves, never the defender's, and the shipped <c>south-army-1</c> heavily outweighs
    /// <c>north-army-1</c>). The human only declares war; nothing here issues <c>attack-army</c> itself, so
    /// any captured battle can only have come from the AI-turn site this finding named.
    /// </summary>
    [Fact]
    public void An_AI_seats_own_attack_during_end_is_captured()
    {
        var session = BattleFixture();
        session.Submit("declare-war south");

        var output = session.Submit("end");

        var battle = Assert.Single(output.Battles);
        Assert.Equal("south-army-1", battle.AttackerId);
        Assert.Equal("north-army-1", battle.DefenderId);
        Assert.Equal("south", battle.AttackerNationId);
        Assert.Equal("north", battle.DefenderNationId);
        Assert.Same(battle, Assert.Single(session.LastBattles));
    }

    /// <summary>
    /// Rework round 1: the review also found <c>MainGameScreen.OnCommandIssued</c>'s own "more than one
    /// battle in a single 'end' never stacks silently" remark unverified — nothing produced two battles in
    /// one <c>Submit</c> anywhere in the suite. <see cref="TwoAiBattlesFixture"/> gives <c>south</c> two
    /// armies, each adjacent to its own separate, much weaker <c>north</c> army; the toy ruleset's
    /// <c>ai.maxActionsPerTurn</c> (24) comfortably allows both attacks inside <c>south</c>'s one turn, so
    /// a single <c>end</c> resolves both battles in one <c>_coordinator.RunTurn</c> call — the exact "more
    /// than one battle in one Submit" case the review asked for.
    /// </summary>
    [Fact]
    public void Two_AI_battles_within_one_end_are_both_captured_in_order_and_do_not_leak_into_the_next_submit()
    {
        var session = TwoAiBattlesFixture();
        session.Submit("declare-war south");

        var output = session.Submit("end");

        Assert.Equal(2, output.Battles.Count);
        Assert.Contains(output.Battles, b => b.AttackerId == "south-army-1" && b.DefenderId == "north-army-1");
        Assert.Contains(output.Battles, b => b.AttackerId == "south-army-2" && b.DefenderId == "north-army-2");
        Assert.Equal(output.Battles.Count, session.LastBattles.Count);
        for (var i = 0; i < output.Battles.Count; i++)
        {
            Assert.Same(output.Battles[i], session.LastBattles[i]);
        }

        var next = session.Submit("status");
        Assert.Empty(next.Battles);
        Assert.Empty(session.LastBattles);
    }

    /// <summary>
    /// The shipped toy scenario/world, with <c>south-army-1</c> moved from its shipped (4,4) to (4,2) —
    /// one tile from <c>north-army-1</c>'s own (3,2) — so <c>attack-army</c>'s adjacency gate passes
    /// without an extra move. The same repositioning <c>PeaceTreatyOfferTests.OfferFixture</c> uses, minus
    /// that test's own ruleset override (irrelevant here — this only needs a battle to resolve, not the
    /// auto-peace treaty to fire).
    /// </summary>
    /// <remarks>
    /// T156 (issue #925): the AI's army scorers read the original's <c>FUN_0044a930</c> strength (bowmen
    /// triple-weighted, no <c>combatPowerWeight</c> per-unit divide), so a state where south's army
    /// outweighs north's under the old <c>ArmyPower.Compute</c> formula may now sit *behind* it (archers
    /// gain a factor of three). The fixture boosts <c>south-army-1</c> to a strength under the new
    /// formula that clears the 100-threshold with margin, so the AI's tree selects
    /// <c>AttackArmy</c> and the test still exercises "an AI seat attacks on its own initiative".
    /// </remarks>
    private static GameSession BattleFixture()
    {
        var toy = CoreTestbed.Toy;
        var southArmy = toy.World.StartingArmies.Single(a => a.Id == "south-army-1") with
        {
            X = 4,
            Y = 2,
            Units = ValueList.Of(
                new UnitSlot(
                    MercenaryLabel: 0,
                    UnitTypeId: "heavy_infantry",
                    Troops: 50_000,
                    Quality: 6,
                    Name: "1st Guards Battalion")),
        };
        var customWorld = toy.World with
        {
            StartingArmies = ValueList.From(
                toy.World.StartingArmies.Select(a => a.Id == "south-army-1" ? southArmy : a)),
        };

        return new GameSession(customWorld, toy.Ruleset, toy.Scenario);
    }

    /// <summary>
    /// <see cref="BattleFixture"/>'s own pair, plus a second, independent north-vs-south pair at (0,0)/(1,0)
    /// — the same tiles and army shapes <c>PeaceTreatyOfferTests.TwoBattleOfferFixture</c> already proved
    /// safe (empty, walkable, clear of every city and the base pair's own armies). That fixture gives its
    /// own second <c>south-army-2</c> zero moves specifically to <em>suppress</em> the AI's own initiative
    /// (its own remarks: "the AI's own AiMilitaryPhase.ProposeArmyAttacks would otherwise attack any
    /// adjacent enemy army during the 'end' call ... regardless of the odds"); here that is the point, so
    /// <c>south-army-2</c> keeps ordinary moves instead.
    /// </summary>
    private static GameSession TwoAiBattlesFixture()
    {
        var toy = CoreTestbed.Toy;
        var southArmy = toy.World.StartingArmies.Single(a => a.Id == "south-army-1") with { X = 4, Y = 2 };

        var northArmy2 = new StartingArmy(
            "north-army-2", "north", X: 0, Y: 0, Morale: 68, Money: 0, SupplyTons: 0, Moves: 5,
            Units: ValueList.Of(new UnitSlot(MercenaryLabel: 0, "light_infantry", Troops: 15000, Quality: 6, Name: "2nd Battalion")));

        var southArmy2 = new StartingArmy(
            "south-army-2", "south", X: 1, Y: 0, Morale: 59, Money: 0, SupplyTons: 0, Moves: 5,
            Units: ValueList.Of(new UnitSlot(MercenaryLabel: 0, "heavy_infantry", Troops: 6000, Quality: 6, Name: "2nd Guards Battalion")));

        var customWorld = toy.World with
        {
            StartingArmies = ValueList.From(
                toy.World.StartingArmies.Select(a => a.Id == "south-army-1" ? southArmy : a)
                    .Append(northArmy2).Append(southArmy2)),
        };

        return new GameSession(customWorld, toy.Ruleset, toy.Scenario);
    }
}
