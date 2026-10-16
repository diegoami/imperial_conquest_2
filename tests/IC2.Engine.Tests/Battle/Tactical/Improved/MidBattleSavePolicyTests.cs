using System.Text.Json.Nodes;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Presentation;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Battle.Tactical.Improved;

/// <summary>T130: the save policy alone changes the gate and reload, while preserving the battle and its draws.</summary>
public sealed class MidBattleSavePolicyTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ic2-t130-save-").FullName;

    private static string ClassicalFile => Path.Combine(ModelTestPaths.DataRoot, "rulesets", "classical-faithful.json");

    private static Ruleset Classical => GameDataLoader.LoadFile<Ruleset>(ClassicalFile);

    private static Ruleset WithPolicy(Ruleset ruleset, string policy) =>
        ruleset with { Combat = ruleset.Combat with { Tactical = ruleset.Combat.Tactical with { MidBattleSave = policy } } };

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathFor(string name) => Path.Combine(_directory, name);

    private static GameSession NewSession(Ruleset ruleset) =>
        TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, ruleset, TacticalSessionTestbed.StrongAttacker());

    [Fact]
    public void One_field_changes_the_save_gate_and_reload_but_no_battle_state_or_draw()
    {
        var classical = Classical;
        var startViews = new List<TacticalBattleView>();
        var orderedViews = new List<TacticalBattleView>();
        var states = new List<GameState>();
        foreach (var ruleset in new[] { classical, WithPolicy(classical, "halfRoundStart") })
        {
            var session = NewSession(ruleset);
            var view = Assert.IsType<TacticalBattleView>(session.Submit("attack-army north-army-1 south-army-1").OpenBattleView);
            startViews.Add(view);

            var atStart = PathFor(ruleset.Combat.Tactical.MidBattleSave + "-start.json");
            Assert.Equal($"Saved to '{atStart}'.", session.Submit($"save {atStart}").Lines[1]);
            var saved = SaveManager.LoadFile(atStart, session.World, ruleset);
            Assert.True(saved.Battle!.AtHalfRoundStart);

            var reloaded = new GameSession(session.World, ruleset, session.Scenario, saved) { HumanBattles = HumanBattleMode.Tactical };
            var afterReload = PathFor(ruleset.Combat.Tactical.MidBattleSave + "-reload.json");
            reloaded.Submit($"save {afterReload}");
            var savedAgain = SaveManager.LoadFile(afterReload, session.World, ruleset);
            Assert.Equal(saved.State, savedAgain.State);
            Assert.Equal(saved.Battle.DrawState, savedAgain.Battle!.DrawState);
            if (ruleset.Combat.Tactical.MidBattleSave == "halfRoundStart")
            {
                Assert.Equal(saved, savedAgain); // Every saved field, including counter, moves, targets, claims and RNG state.
            }
            else
            {
                Assert.Equal(saved.Battle.Counter + 1, savedAgain.Battle.Counter);
                var expected = TacticalHalfRound.Resume(TacticalBattleSession.FromSaved(saved.Battle), TacticalContext.From(ruleset));
                Assert.Equal(expected.Slots, reloaded.OpenTacticalBattle!.Slots);
                Assert.Equal(expected.Grid, reloaded.OpenTacticalBattle.Grid);
            }

            var ordered = session.Submit("battle place 1 1 2");
            Assert.DoesNotContain(ordered.Lines, line => line.StartsWith("Refused:", StringComparison.Ordinal));
            orderedViews.Add(Assert.IsType<TacticalBattleView>(ordered.OpenBattleView));
            var beforeSave = session.State;
            var mid = PathFor(ruleset.Combat.Tactical.MidBattleSave + "-mid.json");
            File.WriteAllText(mid, "existing file");
            var result = session.Submit($"save {mid}");
            if (ruleset.Combat.Tactical.MidBattleSave == "halfRoundStart")
            {
                Assert.Equal("Save at the start of a half-round, before giving any battle order.", result.Lines[1]);
                Assert.Equal("existing file", File.ReadAllText(mid));
            }
            else
            {
                Assert.Equal($"Saved to '{mid}'.", result.Lines[1]);
                Assert.False(SaveManager.LoadFile(mid, session.World, ruleset).Battle!.AtHalfRoundStart);
            }
            Assert.Equal(beforeSave, session.State);
            Assert.Equal(ordered.OpenBattleView, result.OpenBattleView);
            states.Add(session.State);

            // The next human half-round opens the gate again.
            session.Submit("battle end");
            var next = PathFor(ruleset.Combat.Tactical.MidBattleSave + "-next.json");
            Assert.Equal($"Saved to '{next}'.", session.Submit($"save {next}").Lines[1]);
            Assert.True(SaveManager.LoadFile(next, session.World, ruleset).Battle!.AtHalfRoundStart);
        }
        Assert.Equal(startViews[0], startViews[1]);
        Assert.Equal(orderedViews[0], orderedViews[1]);
        Assert.Equal(states[0], states[1]);
    }

    [Fact]
    public void The_policy_selected_exact_restore_continues_the_unsaved_battle_with_the_same_trace_and_draw_state()
    {
        var ruleset = WithPolicy(Classical, "halfRoundStart");
        var session = NewSession(ruleset);
        var unsaved = TacticalBattleSession.Open(session.State, "north-army-1", "south-army-1", ruleset, out var opened);
        Assert.True(unsaved.EndHalfRound().Accepted);
        var saved = unsaved.ToSaved();
        Assert.True(saved.AtHalfRoundStart);
        var restored = TacticalBattleSession.Reopen(opened, ruleset, saved, TacticalBattleSession.RulesetAsksForExactRestore(ruleset));
        Assert.Equal(saved, restored.ToSaved());
        Assert.Equal(TacticalBattleSession.FromSaved(saved), restored.Battle);

        var prefix = unsaved.Battle.Log.Count;
        foreach (var battle in new[] { unsaved, restored })
        {
            var played = battle.GeneralOn();
            Assert.True(played.Accepted, string.Join("; ", played.Lines));
        }
        Assert.Contains(unsaved.Battle.Log.Skip(prefix), e => e is TacticalShotEvent or TacticalMeleeEvent);
        Assert.NotEqual(saved.DrawState, unsaved.DrawState);
        Assert.True(unsaved.IsOver);
        Assert.True(restored.IsOver);
        Assert.Equal(unsaved.Battle.Log.Skip(prefix).ToList(), restored.Battle.Log.ToList());
        Assert.Equal(unsaved.Battle with { Log = ValueList<TacticalEvent>.Empty }, restored.Battle with { Log = ValueList<TacticalEvent>.Empty });
        Assert.Equal(unsaved.DrawState, restored.DrawState);
    }

    [Fact]
    public void A_refused_order_does_not_close_the_start_gate()
    {
        var session = NewSession(WithPolicy(Classical, "halfRoundStart"));
        session.Submit("attack-army north-army-1 south-army-1");
        var before = session.OpenTacticalBattle;
        var refused = session.Submit("battle place 99 1 2");
        Assert.Contains(refused.Lines, line => line.StartsWith("Refused:", StringComparison.Ordinal));
        Assert.Equal(before, refused.OpenBattleView);
        var path = PathFor("after-refused-order.json");
        Assert.Equal($"Saved to '{path}'.", session.Submit($"save {path}").Lines[1]);
    }

    [Fact]
    public void Restore_selection_reads_the_field_even_when_the_preset_identity_is_changed()
    {
        var classical = Classical;
        Assert.False(TacticalBattleSession.RulesetAsksForExactRestore(classical with { Id = "improved", Name = "Other name" }));
        Assert.True(TacticalBattleSession.RulesetAsksForExactRestore(WithPolicy(classical, "halfRoundStart")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("halfroundstart")]
    [InlineData("unknown")]
    [InlineData(null)]
    public void Unknown_save_policies_are_rejected(string? policy)
    {
        var ruleset = WithPolicy(Classical, policy!);
        var error = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate("policy.json", ruleset));
        Assert.Contains("combat.tactical.midBattleSave", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_legacy_ruleset_with_no_policy_defaults_to_the_original_save_behavior()
    {
        var node = JsonNode.Parse(File.ReadAllText(ClassicalFile))!;
        Assert.True(node["combat"]!["tactical"]!.AsObject().Remove("midBattleSave"));
        var legacy = GameDataLoader.Load<Ruleset>("legacy-ruleset.json", node.ToJsonString());
        Assert.Equal("anyTime", legacy.Combat.Tactical.MidBattleSave);
        Assert.False(TacticalBattleSession.RulesetAsksForExactRestore(legacy));
    }
}
