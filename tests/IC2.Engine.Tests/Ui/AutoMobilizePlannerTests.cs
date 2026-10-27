using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Export;
using IC2.Engine.Tests.Import;
using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

public sealed class AutoMobilizePlannerTests
{
    [Theory]
    [InlineData(false, 16, false)]
    [InlineData(false, 20, false)]
    [InlineData(false, 23, false)]
    [InlineData(false, 24, true)]
    [InlineData(false, 28, true)]
    [InlineData(false, 36, true)]
    [InlineData(true, 16, false)]
    [InlineData(true, 20, false)]
    [InlineData(true, 24, true)]
    [InlineData(true, 28, true)]
    public void Automatic_mobilization_requires_average_or_better(bool improved, int stateCode, bool expected)
    {
        var (state, ruleset) = Training(improved);
        state = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == "rome"
            ? n with { RecruitmentSlots = ValueList.Of(n.RecruitmentSlots[0] with { StateCode = stateCode }) }
            : n)) };

        Assert.Equal(expected, AutoMobilizePlanner.Next(state, ruleset, 0) is not null);
    }

    [Fact]
    public void Automatic_threshold_uses_resulting_quality_not_a_fixed_training_counter()
    {
        var (state, ruleset) = Training();
        var changed = ruleset with { Recruitment = ruleset.Recruitment with { MobilizationQualityDivisor = 6 } };
        Assert.NotNull(AutoMobilizePlanner.Next(state, ruleset, 1)); // 24 / 4 = Average
        Assert.Null(AutoMobilizePlanner.Next(state, changed, 1)); // 24 / 6 = Very poor
    }

    [Fact]
    public void Manual_mobilization_still_accepts_very_poor_troops_and_display_matches_result()
    {
        var (state, ruleset) = Training();
        state = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == "rome"
            ? n with { RecruitmentSlots = ValueList.Of(n.RecruitmentSlots[0] with { StateCode = 16 }) }
            : n)) };
        var save = new SaveGame(state.SchemaVersion, "manual-quality", "manual-quality",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(RealGameData.World, ruleset, RealGameData.Scenario, save);
        var row = Assert.Single(RecruitmentPanelViewModel.TrainingForNation(state, ruleset, "rome"));
        Assert.True(row.IsReady);
        Assert.Equal(4, row.Quality);
        Assert.Equal("very poor", row.QualityText);
        Assert.Null(AutoMobilizePlanner.Next(state, ruleset, 0));
        var unitsBefore = session.State.Armies.Sum(a => a.Units.Count);
        var output = session.Submit($"mobilize 0 {RecruitUnitDialogModel.NextArmyId(state)}");
        Assert.DoesNotContain(output.Lines, line => line.StartsWith("Refused:", StringComparison.Ordinal));
        Assert.Empty(session.State.NationById("rome")!.RecruitmentSlots);
        Assert.Equal(unitsBefore + 1, session.State.Armies.Sum(a => a.Units.Count));
        Assert.Contains(session.State.Armies.Where(a => a.Nation == "rome").SelectMany(a => a.Units),
            u => u.Quality == row.Quality && u.UnitTypeId == row.UnitTypeId && u.Troops == row.Troops);
    }

    private static (GameState State, Ruleset Ruleset) Training(bool improved = false)
    {
        var ruleset = improved
            ? GameDataLoader.LoadFile<Ruleset>(ExportedDataPaths.ImprovedRulesetFile)
            : RealGameData.Ruleset;
        var state = new GameSession(RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario,
            seedOverride: 7, humanSeatNationId: "rome").State;
        var city = state.Cities.First(c => c.Owner == "rome");
        var slots = ValueList.Of(
            new RecruitmentSlot(city.Id, "light_infantry", 3_000, 15),
            new RecruitmentSlot(city.Id, "heavy_infantry", 4_000, 24),
            new RecruitmentSlot(city.Id, "archers", 2_000, 28));
        state = state with
        {
            RulesetId = ruleset.Id,
            ActiveSeatIndex = state.TurnOrder.ToList().IndexOf("rome"),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == "rome"
                ? n with { RecruitmentSlots = slots } : n)),
        };
        return (state, ruleset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_ready_human_slots_are_proposed_with_deterministic_new_army_id(bool improved)
    {
        var (state, ruleset) = Training(improved);
        var before = state with { };
        Assert.Null(AutoMobilizePlanner.Next(state, ruleset, 0));
        var ready = AutoMobilizePlanner.Next(state, ruleset, 1);
        Assert.NotNull(ready);
        Assert.Equal($"mobilize 1 {RecruitUnitDialogModel.NextArmyId(state)}", ready.CommandLine);
        Assert.Equal(ready, AutoMobilizePlanner.Next(state, ruleset, 1));
        Assert.NotNull(AutoMobilizePlanner.Next(state, ruleset, 2));
        Assert.Null(AutoMobilizePlanner.Next(state, ruleset, 3));
        Assert.Equal(before, state);
        Assert.Equal(before.RandomSeed, state.RandomSeed);

        var ai = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == "rome"
            ? n with { Control = SeatControl.Ai } : n)) };
        Assert.Null(AutoMobilizePlanner.Next(ai, ruleset, 1));
    }

    [Fact]
    public void Accepted_compaction_makes_the_next_ready_slot_visible_at_the_same_index()
    {
        var (state, ruleset) = Training();
        var nation = state.NationById("rome")!;
        var compacted = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == "rome"
            ? n with { RecruitmentSlots = ValueList.From(nation.RecruitmentSlots.Where((_, i) => i != 1)) }
            : n)) };

        Assert.Equal("archers", compacted.NationById("rome")!.RecruitmentSlots[1].UnitTypeId);
        Assert.StartsWith("mobilize 1 ", AutoMobilizePlanner.Next(compacted, ruleset, 1)!.CommandLine);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Proposed_mobilize_uses_the_real_engine_command_under_both_rulesets(bool improved)
    {
        var (state, ruleset) = Training(improved);
        var nation = state.NationById("rome")!;
        state = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == "rome"
            ? n with { RecruitmentSlots = ValueList.Of(nation.RecruitmentSlots[2]) } : n)) };
        var scenario = RealGameData.Scenario with { RulesetId = ruleset.Id };
        var save = new SaveGame(state.SchemaVersion, "auto-mobilize", "auto-mobilize",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(RealGameData.World, ruleset, scenario, save);
        var proposal = AutoMobilizePlanner.Next(session.State, ruleset, 0);
        Assert.NotNull(proposal);

        var output = session.Submit(proposal.CommandLine);
        Assert.DoesNotContain(output.Lines, line => line.StartsWith("Refused:", StringComparison.Ordinal));
        Assert.Empty(session.State.NationById("rome")!.RecruitmentSlots);
    }
}
