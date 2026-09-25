using System.Globalization;
using IC2.Engine.Ai;
using Xunit;
using Xunit.Abstractions;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T82 (#359, bug #357) Owns amendment (PR #378), new hazard: "The toy world's AI may stop declaring
/// war." <c>P = (wealth/20000)·(unity/100)</c>, and the toy nations' shipped wealth (400 for north, 360
/// for south — both in the hundreds) truncates to 0 under both terms, so <c>AiOwnDiplomacyRule.BestWarTarget</c>
/// can never clear its own ratio gate there (<see cref="AiOwnDiplomacyRule.Power"/>'s own remarks confirm
/// the truncation is real, at instruction level, not a rounding choice this task made).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Rework round 1, B5.</strong> The parser used to match transcript lines against
/// <c>"  issued "</c> (two leading spaces), which never matched anything: <see cref="AiTurn"/> writes
/// each line as <c>"  issued {kind}"</c> (its own two spaces), and <see cref="AiGameRunner"/>'s own
/// transcript builder (:185) prepends four more ahead of every line it copies in, so the committed
/// transcript's actual prefix is six spaces, not two. The test therefore always counted zero of
/// everything and asserted nothing, which is why the original PR body's "0 wars, 0 battles and 0
/// sieges, no change from before T82" reading was wrong on both counts: the real, corrected count is
/// nonzero, and it does differ from a baseline measurement taken the same way on <c>origin/main</c>
/// (150 declare-war / 150 attack-army there, against this branch's own count below) — see the PR body
/// for that side-by-side comparison. <see cref="string.TrimStart()"/> makes the parse robust to either
/// prefix rather than hard-coding a specific count of leading spaces.
/// </para>
/// <para>
/// The counts below are pinned exactly, not just reported: the toy soak is a fixed set of seeds run
/// against a fixed scenario and ruleset, so it is exactly as deterministic as
/// <c>AiSoakTests</c>' own rejected/mismatch counts are.
/// </para>
/// </remarks>
public sealed class AiToyWorldCombatMeasurementTests
{
    private readonly ITestOutputHelper _output;

    public AiToyWorldCombatMeasurementTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void FiftySeedSoak_WarBattleAndSiegeCounts()
    {
        var declaredWars = 0;
        var attackArmy = 0;
        var attackFleet = 0;
        var besiegeCity = 0;
        var totalCommands = 0;
        var totalRejected = 0;

        foreach (var seed in AiTestbed.SoakSeeds())
        {
            var result = AiTestbed.RunSeed(seed);
            totalCommands += result.CommandsIssued;
            totalRejected += result.CommandsRejected;
            foreach (var line in result.Transcript)
            {
                var trimmed = line.TrimStart(' ');
                if (!trimmed.StartsWith("issued ", StringComparison.Ordinal))
                {
                    continue;
                }

                var kind = trimmed["issued ".Length..];
                switch (kind)
                {
                    case "diplomacy.declare-war": declaredWars++; break;
                    case "battle.attack-army": attackArmy++; break;
                    case "battle.attack-fleet": attackFleet++; break;
                    case "battle.besiege-city": besiegeCity++; break;
                }
            }
        }

        _output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "T82 (after): {0} seeds, {1} total commands, {2} rejected -- {3} diplomacy.declare-war, "
            + "{4} battle.attack-army, {5} battle.attack-fleet, {6} battle.besiege-city",
            AiTestbed.SoakSeedCount, totalCommands, totalRejected, declaredWars, attackArmy, attackFleet, besiegeCity));

        // Pinned: combat did not vanish, it fell by two thirds against origin/main's own 150/150 (the PR
        // body carries that side-by-side comparison). 0 rejected throughout the soak, same as before.
        Assert.Equal(50, declaredWars);
        Assert.Equal(50, attackArmy);
        Assert.Equal(0, attackFleet);
        Assert.Equal(0, besiegeCity);
        Assert.Equal(0, totalRejected);
    }
}
