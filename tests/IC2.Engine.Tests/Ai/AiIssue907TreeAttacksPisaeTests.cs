using IC2.Engine.Battle;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T156 (issue #925) Done-when 4: issue #907's reproduction attacks. <c>classical-mediterranean</c>, seat
/// Rome, the default seed, <c>end</c> repeated as the issue did: Gaul's <c>army-9</c> walks to Pisae's
/// border and, once beside it, besieges or attacks Pisae within three turns, in both presets. Under the
/// designed siege-ratio gate it stood there for fourteen turns.
/// </summary>
public sealed class AiIssue907TreeAttacksPisaeTests
{
    private const int RoundsAllowed = 16;
    private const int TurnsToAttack = 3;

    [Theory]
    [InlineData("classical-mediterranean")]
    [InlineData("example-classical-improved")]
    public void Gauls_army_9_besieges_Pisae_within_three_turns_of_standing_beside_it(string scenarioId)
    {
        var resolved = GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve(scenarioId);
        var session = new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario, null, "rome");
        var battles = new List<(int Round, BattleResult Result)>();
        var round = 0;
        session.EventsPublished += events =>
        {
            foreach (var resolvedBattle in events.OfType<BattleResolved>())
            {
                battles.Add((round, resolvedBattle.Result));
            }
        };

        var pisae = resolved.World.Cities.Single(c => string.Equals(c.Name, "Pisae", StringComparison.Ordinal));
        int? besideSince = null;
        var trace = new List<string>();
        for (round = 1; round <= RoundsAllowed; round++)
        {
            session.Submit("end");
            var army = session.State.ArmyById("army-9");
            trace.Add(army is null ? $"r{round}: gone" : $"r{round}: ({army.X},{army.Y})");
            if (besideSince is null
                && army is not null
                && Math.Max(Math.Abs(army.X - pisae.X), Math.Abs(army.Y - pisae.Y)) == 1)
            {
                besideSince = round;
            }
        }

        var where = "army-9: " + string.Join(" ", trace) + "; its battles: "
            + string.Join(", ", battles
                .Where(b => b.Result.AttackerId == "army-9" || b.Result.DefenderId == "army-9")
                .Select(b => $"r{b.Round} {b.Result.AttackerId}>{b.Result.DefenderId}"));
        Assert.True(besideSince is not null, "army-9 never stood beside Pisae. " + where);
        var attack = battles.FirstOrDefault(b =>
            string.Equals(b.Result.AttackerId, "army-9", StringComparison.Ordinal)
            && string.Equals(b.Result.DefenderId, pisae.Id, StringComparison.Ordinal));
        Assert.True(attack.Result is not null, "army-9 never besieged Pisae. " + where);
        Assert.InRange(attack.Round, besideSince!.Value, besideSince.Value + TurnsToAttack);
    }
}
