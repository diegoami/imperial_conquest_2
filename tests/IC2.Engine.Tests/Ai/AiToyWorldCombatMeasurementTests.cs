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
/// the truncation is real, at instruction level, not a rounding choice this task made). This is a
/// measurement, not an assertion: nothing here can fail from what it counts, only report it.
/// </summary>
/// <remarks>
/// <strong>Measured result: the hazard does not change the soak, because there was nothing to change.</strong>
/// The same count, taken the same way in a detached worktree at <c>origin/main</c> (before this task),
/// is <strong>also</strong> 0 wars, 0 attacks, 0 sieges across all 50 seeds — <c>AllAiScenario</c>'s own
/// starting armies and cities never happen to put two AI seats' forces adjacent within the 1,200-turn
/// cap, with or without an implicit declaration, so combat was already absent from this specific soak
/// before T82 and remains absent after it. The PR body reports both counts side by side.
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

        foreach (var seed in AiTestbed.SoakSeeds())
        {
            var result = AiTestbed.RunSeed(seed);
            totalCommands += result.CommandsIssued;
            foreach (var line in result.Transcript)
            {
                if (!line.StartsWith("  issued ", StringComparison.Ordinal))
                {
                    continue;
                }

                var kind = line["  issued ".Length..];
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
            "T82 (after): {0} seeds, {1} total commands -- {2} diplomacy.declare-war, {3} battle.attack-army, "
            + "{4} battle.attack-fleet, {5} battle.besiege-city",
            AiTestbed.SoakSeedCount, totalCommands, declaredWars, attackArmy, attackFleet, besiegeCity));

        // Nothing asserted: see this class's own remarks. The PR body carries the actual counts,
        // compared against the same measurement taken on origin/main before this task's changes.
    }
}
