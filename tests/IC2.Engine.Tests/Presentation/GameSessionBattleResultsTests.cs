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
    /// The shipped toy scenario/world, with <c>south-army-1</c> moved from its shipped (4,4) to (4,2) —
    /// one tile from <c>north-army-1</c>'s own (3,2) — so <c>attack-army</c>'s adjacency gate passes
    /// without an extra move. The same repositioning <c>PeaceTreatyOfferTests.OfferFixture</c> uses, minus
    /// that test's own ruleset override (irrelevant here — this only needs a battle to resolve, not the
    /// auto-peace treaty to fire).
    /// </summary>
    private static GameSession BattleFixture()
    {
        var toy = CoreTestbed.Toy;
        var southArmy = toy.World.StartingArmies.Single(a => a.Id == "south-army-1") with { X = 4, Y = 2 };
        var customWorld = toy.World with
        {
            StartingArmies = ValueList.From(
                toy.World.StartingArmies.Select(a => a.Id == "south-army-1" ? southArmy : a)),
        };

        return new GameSession(customWorld, toy.Ruleset, toy.Scenario);
    }
}
