using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Export;
using IC2.Engine.Tests.Import;
using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

public sealed class AutoRecruitPlannerTests
{
    private static (GameState State, CityState City, Ruleset Ruleset, AutoRecruitCityPlan Plan) Setup(bool improved = false)
    {
        var ruleset = improved
            ? GameDataLoader.LoadFile<Ruleset>(ExportedDataPaths.ImprovedRulesetFile)
            : RealGameData.Ruleset;
        var state = new GameSession(RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario,
            seedOverride: 7, humanSeatNationId: "rome").State;
        state = state with { RulesetId = ruleset.Id,
            ActiveSeatIndex = state.TurnOrder.ToList().IndexOf("rome") };
        var city = state.CityById(state.NationById("rome")!.CapitalCityId!)!;
        var bounds = RecruitTroopBounds.For(ruleset, "light_infantry");
        var queued = state.NationById("rome")!.RecruitmentSlots.Count(slot => slot.TargetCityId == city.Id);
        return (state, city, ruleset,
            new AutoRecruitCityPlan(RealGameData.World.Id, city.Id, "light_infantry", bounds.Minimum, queued + 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Valid_plan_proposes_the_existing_recruit_command_without_mutating_state(bool improved)
    {
        var (state, city, ruleset, plan) = Setup(improved);
        var before = state with { };
        var settings = new UserSettings { AutoRecruitEnabled = true, AutoTreasuryReserveTalents = 0,
            AutoRecruitPlans = ValueList.Of(plan) };

        var decision = AutoRecruitPlanner.ForCity(state, RealGameData.World, ruleset, settings, plan);

        Assert.Null(decision.Reason);
        Assert.Equal($"recruit-standing {city.Id} light_infantry {plan.Troops}", decision.CommandLine);
        Assert.Equal(before, state);
        Assert.Equal(before.RandomSeed, state.RandomSeed);
    }

    [Fact]
    public void World_ownership_type_bounds_queue_and_reserve_gate_the_plan()
    {
        var (state, city, ruleset, plan) = Setup();
        var settings = new UserSettings { AutoRecruitEnabled = true, AutoTreasuryReserveTalents = 0 };
        string Reason(GameState s, AutoRecruitCityPlan p, UserSettings u) =>
            AutoRecruitPlanner.ForCity(s, RealGameData.World, ruleset, u, p).Reason ?? string.Empty;
        Assert.Contains("another world", Reason(state, plan with { WorldId = "italy-500bc" }, settings));
        Assert.Contains("unknown city", Reason(state, plan with { CityId = "missing-city" }, settings));
        Assert.Contains("not owned", Reason(state with { Cities = ValueList.From(state.Cities.Select(c =>
            c.Id == city.Id ? c with { Owner = "gaul" } : c)) }, plan, settings));
        Assert.Contains("unavailable", Reason(state, plan with { UnitTypeId = "unknown" }, settings));
        Assert.Contains("bounds", Reason(state, plan with { Troops = 1 }, settings));
        Assert.Contains("step and bounds", Reason(state, plan with { Troops = plan.Troops + 1 }, settings));
        Assert.Contains("reserve", Reason(state, plan, settings with
        { AutoTreasuryReserveTalents = int.MaxValue }));
        Assert.Contains("off", Reason(state, plan, new UserSettings()));

        var nation = state.NationById("rome")!;
        var queued = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == "rome"
            ? n with { RecruitmentSlots = ValueList.Of(
                new RecruitmentSlot(city.Id, plan.UnitTypeId, plan.Troops, 0)) } : n)) };
        Assert.Contains("target reached", Reason(queued, plan with { TargetQueued = 1 }, settings));
        var capped = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == "rome"
            ? n with { MobilizedPercent = ruleset.Recruitment.MobilizationCapPercent } : n)) };
        Assert.Contains("cap", Reason(capped, plan, settings));
        var full = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == "rome"
            ? n with { RecruitmentSlots = ValueList.From(Enumerable.Range(0, ruleset.Recruitment.MaxSlots)
                .Select(_ => new RecruitmentSlot("caere", plan.UnitTypeId, plan.Troops, 0))) } : n)) };
        Assert.Contains("queue is full", Reason(full, plan with { TargetQueued = 40 }, settings));
        var computer = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == "rome"
            ? n with { Control = SeatControl.Ai } : n)) };
        Assert.Contains("not human", Reason(computer, plan, settings));
        Assert.Equal(nation, state.NationById("rome"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Proposed_order_is_accepted_by_the_real_session_in_both_rulesets(bool improved)
    {
        var (state, city, ruleset, plan) = Setup(improved);
        var scenario = RealGameData.Scenario with { RulesetId = ruleset.Id };
        var save = new SaveGame(state.SchemaVersion, "auto-recruit", "auto-recruit",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(RealGameData.World, ruleset, scenario, save);
        var settings = new UserSettings { AutoRecruitEnabled = true, AutoTreasuryReserveTalents = 0 };
        var decision = AutoRecruitPlanner.ForCity(session.State, RealGameData.World, ruleset, settings, plan);
        Assert.NotNull(decision.CommandLine);
        var beforeTreasury = session.State.NationById("rome")!.Treasury;
        var beforeCount = session.State.NationById("rome")!.RecruitmentSlots.Count;

        Assert.DoesNotContain(session.Submit(decision.CommandLine).Lines,
            line => line.StartsWith("Refused:", StringComparison.Ordinal));
        Assert.Equal(beforeCount + 1, session.State.NationById("rome")!.RecruitmentSlots.Count);
        Assert.Equal(beforeTreasury - decision.CostTalents, session.State.NationById("rome")!.Treasury);
    }
}
