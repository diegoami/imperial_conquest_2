using IC2.Engine.Battle.Tactical;
using IC2.Engine.Core;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core.Determinism;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Battle.Tactical;

/// <summary>
/// Determinism and isolation: <c>docs/game-design.md</c> design principle 4 and "Every random draw, in
/// order" (one draw seam), and the task's scope: a fresh port that uses neither the candidate engines
/// nor diplomacy (<c>FieldBattleTests.AssertNoBattleSourceReferencesDiplomacy</c>).
/// </summary>
public class DeterminismAndIsolationTests
{
    [Fact]
    public void The_same_state_orders_and_draws_give_byte_identical_states_and_traces()
    {
        var first = ScriptedHumanBattle.Play(new ScriptedDraws(ScriptedHumanBattle.Script));
        var second = ScriptedHumanBattle.Play(new ScriptedDraws(ScriptedHumanBattle.Script));

        Assert.Equal(GameJson.Serialize(first.Battle), GameJson.Serialize(second.Battle));
        Assert.Equal(GameJson.Serialize(first.Game), GameJson.Serialize(second.Game));
        Assert.Equal(GameJson.Serialize(first.Report), GameJson.Serialize(second.Report));
        Assert.Equal(first.Battle, second.Battle);
    }

    [Fact]
    public void A_battle_on_the_originals_generator_replays_byte_for_byte_and_another_seed_differs()
    {
        var a = Slugfest(1);
        var b = Slugfest(1);
        var c = Slugfest(2);

        Assert.True(a.IsOver);
        Assert.Equal(GameJson.Serialize(a), GameJson.Serialize(b));
        Assert.NotEqual(GameJson.Serialize(a), GameJson.Serialize(c));
    }

    [Fact]
    public void The_trace_serializes_every_event_kind_with_its_discriminator()
    {
        var battle = ScriptedHumanBattle.Play(new ScriptedDraws(ScriptedHumanBattle.Script)).Battle;
        var json = GameJson.Serialize(battle);

        foreach (var kind in new[] { "draw", "copiedIn", "setUp", "halfRoundEnded", "placed", "moved", "targetSet", "shot", "melee", "routed", "battleOver", "promotion" })
        {
            Assert.Contains($"\"kind\": \"{kind}\"", json);
        }
    }

    [Fact]
    public void The_tactical_sources_reference_no_candidate_engine_no_System_Random_and_no_diplomacy()
    {
        var folder = Path.Combine(ModelTestPaths.RepositoryRoot, "src", "IC2.Engine", "Battle", "Tactical");
        var files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        var offenders = files
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
            .Where(x => x.line.Contains("Candidates", StringComparison.Ordinal)
                        || x.line.Contains("System.Random", StringComparison.Ordinal)
                        || x.line.Contains("Diplomacy", StringComparison.Ordinal))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_determinism_scanner_finds_nothing_in_the_tactical_sources()
    {
        var folder = Path.Combine(ModelTestPaths.RepositoryRoot, "src", "IC2.Engine", "Battle", "Tactical");
        Assert.Empty(DeterminismScanner.Scan(folder, ModelTestPaths.RepositoryRoot));
    }

    /// <summary>
    /// Two heavy infantry units, one a side, adjacent; each human side sets its target every half-round
    /// and ends it, until the battle ends. Every draw is the original's generator from <paramref name="seed"/>.
    /// </summary>
    private static TacticalBattleState Slugfest(uint seed)
    {
        var draws = new DelphiBattleDraws(seed);
        var state = Arena(
            (0, Unit(HI, 5, 5, 6000, quality: 6, morale: 65)),
            (1, Unit(LI, 4, 5, 9000, quality: 5, morale: 70)),
            (D0, Unit(HI, 5, 6, 6000, quality: 6, morale: 88)),
            (D0 + 1, Unit(LI, 4, 6, 9000, quality: 5, morale: 70)));

        for (var half = 0; half < 500 && !state.IsOver; half++)
        {
            var side = state.SideToMove;
            var own = side * Context.SlotsPerSide;
            var enemy = (1 - side) * Context.SlotsPerSide;
            for (var s = own; s < own + 2; s++)
            {
                for (var e = enemy; e < enemy + 2 && state.Slots[s].IsLive; e++)
                {
                    var set = TacticalOrders.SetTarget(state, s, e, Context);
                    if (set.Accepted)
                    {
                        state = set.State;
                        break;
                    }
                }
            }

            state = TacticalOrders.EndHalfRound(state, Context, draws, NullTacticalGeneral.Instance).State;
        }

        return state;
    }
}
