using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using Xunit;
using Xunit.Abstractions;
using F = IC2.Engine.Tests.Ai.AiReachableFallbackTests;
using Capture = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

public sealed class AiFallbackDecisionLoggingTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("emergency-city")]
    [InlineData("emergency-army")]
    [InlineData("garrison")]
    [InlineData("boxed")]
    [InlineData("city-march")]
    [InlineData("city-siege")]
    [InlineData("army-attack")]
    [InlineData("marched-city-siege")]
    [InlineData("marched-army-attack")]
    [InlineData("marched-no-offer")]
    public void The_log_records_only_the_original_action_actually_offered(string scenario)
    {
        var fallback = scenario is "emergency-city" or "emergency-army" or "garrison";
        var boxed = scenario == "boxed";
        var adjacent = scenario.Contains("siege") || scenario.Contains("army-attack");
        var armyTarget = scenario.Contains("army");
        var world = F.Terrain(cells =>
        {
            // Existing toy terrain codes: mountain costs four, plain costs one.
            if (fallback) cells[(5 * 32) + 6] = 5;
            if (boxed) F.SeaRing(cells, 5, 5, 1);
        });
        var army = F.Army(5, 5, moves: 1);
        if (!armyTarget && !boxed)
            army = army with { Units = ValueList.From(new[] { Capture.Unit("archers", 100_000) }) };
        var targetX = adjacent ? 6 : 9;
        var state = F.State(
            [
                F.City("n-cap", "north", scenario == "garrison" ? 3 : 20, 5, supply: 5_000),
                F.City("s-cap", "south", armyTarget ? 30 : targetX, armyTarget ? 30 : 5),
            ], army);
        if (armyTarget)
        {
            var enemy = Capture.Army("enemy", "south", targetX, 5, 60,
                Capture.Unit("light_infantry", 1_000)) with { Moves = 1 };
            state = state with { Armies = ValueList.From(state.Armies.Append(enemy)) };
        }
        if (!boxed)
            state = state with { Relations = state.Relations.WithRelation("north", "south", AiScriptedStates.Ruleset.Diplomacy.StateCodes.War) };
        var view = new AiView(state, AiScriptedStates.Ruleset, world, "north");
        var decision = AiArmyTargetTree.DecideUnlogged(view, army, !boxed, out var original);
        if (fallback)
        {
            Assert.Equal(armyTarget ? AiArmyTargetTree.Kind.AttackArmy : AiArmyTargetTree.Kind.AttackCity, decision.Selected);
            Assert.True(AiArmyTargetTree.IsReachable(view, army, targetX, 5));
            var blocked = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), view.Ruleset, world, new RecordingEventSink())
                .Dispatch(state, new MoveArmyCommand("north", army.Id, targetX, 5));
            Assert.True(blocked.IsAccepted);
            Assert.Equal((army.X, army.Y), (blocked.State.ArmyById(army.Id)!.X, blocked.State.ArmyById(army.Id)!.Y));
        }
        var marched = scenario.StartsWith("marched", StringComparison.Ordinal);
        var candidates = new List<AiCandidate>();
        var boxedIn = new List<string>();
        var rng = SplitMix64Rng.ForStream(1UL, "ai.turn");
        var initialRng = rng.State;
        var log = AiArmyTargetTree.NewDecisionLog();
        List<AiArmyTargetTree.DecisionRecord> recorded;
        try
        {
            AiMilitaryPhase.Propose(view, AiPersonalityProfile.For(state.NationById("north")!, view.Ruleset),
                rng, marched ? [army.Id] : [], candidates, boxedIn: boxedIn);
            recorded = log.ToList();
        }
        finally { AiArmyTargetTree.ClearDecisionLog(); }
        Assert.Equal(initialRng, rng.State);
        var sink = new RecordingEventSink();
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), view.Ruleset, world, sink);
        var after = state;
        var commands = new List<ICommand>();
        foreach (var candidate in candidates)
        foreach (var command in candidate.Commands)
        {
            commands.Add(command);
            var result = dispatcher.Dispatch(after, command);
            Assert.True(result.IsAccepted);
            after = result.State;
        }
        // Printed before logging assertions, so the red-before run also captures the exact actions,
        // scores, command payloads, resulting state and draw position for comparison with green-after.
        var fingerprint = JsonSerializer.Serialize(new
        {
            Candidates = candidates.Select(c => new { c.Kind, c.Score, c.Rationale, c.SubjectId }),
            Commands = commands.Select(c => new { Type = c.GetType().Name, Payload = JsonSerializer.Serialize(c, c.GetType()) }),
            State = JsonSerializer.Serialize(after), Rng = rng.State, Boxed = boxedIn,
        });
        output.WriteLine(scenario + " actions/state/draws SHA256 " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint))));
        if (fallback)
        {
            var candidate = Assert.Single(candidates);
            Assert.Contains(scenario == "garrison" ? "garrison fallback" : "emergency move", candidate.Rationale);
            Assert.IsType<MoveArmyCommand>(Assert.Single(commands));
            Assert.NotEqual((army.X, army.Y), (after.ArmyById(army.Id)!.X, after.ArmyById(army.Id)!.Y));
            Assert.Empty(recorded);
        }
        else if (boxed || scenario == "marched-no-offer")
        {
            Assert.Empty(candidates);
            Assert.Equal(state, after);
            if (boxed) Assert.Equal(new[] { army.Id }, boxedIn);
            Assert.Empty(recorded);
        }
        else
        {
            Assert.Single(candidates);
            Assert.Equal(original, Assert.Single(recorded));
            Assert.Equal(armyTarget ? AiArmyTargetTree.Kind.AttackArmy : AiArmyTargetTree.Kind.AttackCity, recorded[0].Kind);
        }
    }
}
