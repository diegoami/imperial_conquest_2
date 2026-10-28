using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

public sealed class DialogCommandDraftTests
{
    private const string Army = "north-army-1";

    private static GameSession Build(bool tinyWealth = false)
    {
        var data = CoreTestbed.Toy;
        var ruleset = data.Ruleset with { UnitTypes = ValueList.From(data.Ruleset.UnitTypes.Select(t =>
            t.Id == "light_infantry" ? t with { StandardBattalionSize = 15000 } : t)) };
        var initial = new GameSession(data.World, ruleset, data.Scenario, seedOverride: 42, humanSeatNationId: "north");
        var army = initial.State.ArmyById(Army)! with { Money = 123, SupplyTons = 45,
            Units = ValueList.Of(new UnitSlot(0, "light_infantry", 6000, 6, "First"),
                new UnitSlot(0, "light_infantry", 6000, 6, "Second"), new UnitSlot(0, "heavy_infantry", 2400, 6, "Third")) };
        var state = initial.State with { Armies = ValueList.From(initial.State.Armies.Select(a => a.Id == Army ? army : a)),
            Nations = ValueList.From(initial.State.Nations.Select(n => n.Id == "north"
                ? n with { Treasury = 500, Unity = 900, Wealth = tinyWealth ? 1 : n.Wealth } : n)) };
        var save = new SaveGame(state.SchemaVersion, "dialog-test", "dialog-test", state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(data.World, ruleset, data.Scenario, save);
        session.SetArmyTravelTarget(new ArmyTravelTarget(Army, "north", army.X, army.Y));
        session.SetRallyPoint(new NationRallyPoint("north", 2, 1, "arx"));
        return session;
    }

    private static string[] Mixed => new[] { $"rename-unit {Army} 0 Renamed", $"split-unit {Army} 0 3000",
        $"join-units {Army} 0 3", $"disband-unit {Army} 1" };

    [Fact]
    public void Preview_and_cancel_preserve_authoritative_state_metadata_rng_and_events()
    {
        var session = Build();
        var before = session.State;
        var travel = session.ArmyTravelTargets;
        var rally = session.NationRallyPoints;
        var events = 0;
        session.EventsPublished += _ => events++;
        var draft = session.BeginDialogEdit(DialogEditKind.Army);
        Assert.True(draft.StageMany(Mixed).Accepted);
        Assert.Equal("Renamed", draft.WorkingState.ArmyById(Army)!.Units[0].Name);
        Assert.Equal(2, draft.WorkingState.ArmyById(Army)!.Units.Count);
        Assert.Same(before, session.State);
        Assert.Equal(travel, session.ArmyTravelTargets);
        Assert.Equal(rally, session.NationRallyPoints);
        Assert.Equal(0, events);
        draft.Cancel();
        Assert.False(draft.Commit().Accepted);
        Assert.Same(before, session.State);
        Assert.Equal(0, events);
    }

    [Fact]
    public void Commit_matches_real_commands_and_publishes_only_the_complete_final_state_once()
    {
        var session = Build();
        var control = Build();
        var draft = session.BeginDialogEdit(DialogEditKind.Army);
        Assert.True(draft.StageMany(Mixed).Accepted);
        var expectedLines = Mixed.SelectMany(line => control.Submit(line).Lines).ToArray();
        var observedStates = new List<GameState>();
        session.EventsPublished += _ => observedStates.Add(session.State);
        var applied = draft.Commit();
        Assert.True(applied.Accepted, string.Join('\n', applied.Output.Lines));
        Assert.Equal(control.State, session.State);
        Assert.Equal(expectedLines, applied.Output.Lines);
        Assert.Equal(control.ArmyTravelTargets, session.ArmyTravelTargets);
        Assert.Equal(control.NationRallyPoints, session.NationRallyPoints);
        Assert.Equal(session.State, Assert.Single(observedStates));
        Assert.Equal(control.Submit("end").Lines, session.Submit("end").Lines);
        Assert.Equal(control.State, session.State);
        Assert.False(draft.Commit().Accepted);
    }

    [Fact]
    public void Failed_final_command_does_not_commit_an_accepted_prefix()
    {
        var session = Build();
        var before = session.State;
        var events = 0;
        session.EventsPublished += _ => events++;
        var draft = session.BeginDialogEdit(DialogEditKind.Army);
        Assert.True(draft.Stage($"rename-unit {Army} 0 Pending").Accepted);
        var result = draft.Commit($"split-unit {Army} 0 1");
        Assert.False(result.Accepted);
        Assert.Contains(result.Output.Lines, l => l.Contains("rejected", StringComparison.Ordinal));
        Assert.Same(before, session.State);
        Assert.Equal(0, events);
        Assert.True(draft.Commit().Accepted);
        Assert.Equal("Pending", session.State.ArmyById(Army)!.Units[0].Name);
        Assert.Equal(1, events);
    }

    [Fact]
    public void Failed_multi_edit_does_not_even_partially_change_the_working_copy()
    {
        var session = Build();
        var draft = session.BeginDialogEdit(DialogEditKind.Army);
        Assert.False(draft.StageMany(new[] { $"rename-unit {Army} 0 Pending", $"disband-unit {Army} 99" }).Accepted);
        Assert.Empty(draft.Commands);
        Assert.Same(session.State, draft.WorkingState);
    }

    [Fact]
    public void Stale_campaign_refuses_commit_without_overwriting_the_external_change()
    {
        var session = Build();
        var draft = session.BeginDialogEdit(DialogEditKind.Army);
        Assert.True(draft.Stage($"rename-unit {Army} 0 Pending").Accepted);
        session.Submit($"rename-unit {Army} 1 External");
        var external = session.State;
        Assert.False(draft.Commit().Accepted);
        Assert.Same(external, session.State);
        Assert.Equal("First", session.State.ArmyById(Army)!.Units[0].Name);
        Assert.Equal("External", session.State.ArmyById(Army)!.Units[1].Name);
    }

    [Theory]
    [InlineData("end")]
    [InlineData("save not-a-real-target.json")]
    [InlineData("load not-a-real-target.json")]
    [InlineData("declare-war south")]
    public void Army_draft_cannot_execute_unrelated_session_or_diplomacy_actions(string line)
    {
        var session = Build();
        var before = session.State;
        var draft = session.BeginDialogEdit(DialogEditKind.Army);
        Assert.False(draft.Stage(line).Accepted);
        Assert.Same(before, session.State);
        Assert.Empty(draft.Commands);
    }

    [Fact]
    public void Opening_a_draft_does_not_run_load_time_deposition_in_the_middle_of_a_turn()
    {
        var session = Build(tinyWealth: true);
        var funding = session.Submit($"transfer-money {Army} 1000");
        Assert.DoesNotContain(funding.Lines, l => l.Contains("rejected", StringComparison.Ordinal));
        Assert.True(Deposition.InDebt(session.State.NationById("north")!, session.Ruleset));
        var before = session.State;
        var draft = session.BeginDialogEdit(DialogEditKind.Army);
        Assert.True(draft.Stage($"rename-unit {Army} 0 Pending").Accepted);
        Assert.Equal(SeatControl.Human, draft.WorkingState.NationById("north")!.Control);
        Assert.Same(before, session.State);
    }
}
