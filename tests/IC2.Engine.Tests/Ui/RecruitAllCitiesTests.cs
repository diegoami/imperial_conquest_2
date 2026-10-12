using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T162 (bug #1001): the Recruit unit dialog's All cities entry, its opening rule, acting on a row from
/// it, and the readiness text. States are real sessions; slots come from the engine's own
/// <c>recruit-standing</c> and become ready through <c>end</c>.
/// </summary>
public sealed class RecruitAllCitiesTests
{
    private const string RomeId = "rome";
    private const string RomeCityId = "rome";
    private const string LuceriaCityId = "luceria";

    private static GameSession RomeSession()
    {
        var classical = GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");

        // As RecruitmentPanelViewModelTests: start without T161's starting queues so the rows are the ordered ones.
        var world = classical.World with
        {
            Nations = ValueList.From(classical.World.Nations.Select(n => n with { RecruitmentSlots = null })),
        };
        return new GameSession(
            world, classical.Ruleset, classical.Scenario, seedOverride: 1, humanSeatNationId: RomeId);
    }

    private static void Recruit(GameSession session, string cityId, string unitTypeId, int troops)
    {
        var output = session.Submit($"recruit-standing {cityId} {unitTypeId} {troops}");
        Assert.Contains("recruitment.recruit-standing-unit accepted.", output.Lines, StringComparer.Ordinal);
    }

    private static void EndTurns(GameSession session, int turns)
    {
        for (var i = 0; i < turns; i++)
        {
            session.Submit("end");
        }
    }

    private static RecruitUnitDialogModel Model(GameSession s) =>
        RecruitUnitDialogModel.ForActiveNation(s.State, s.Ruleset, s.World);

    private static GameSession TwoCitySession()
    {
        var session = RomeSession();
        Recruit(session, RomeCityId, "light_infantry", 200);
        Recruit(session, LuceriaCityId, "archers", 400);
        return session;
    }

    [Fact]
    public void All_cities_lists_every_unit_with_its_city_in_queue_order_and_a_city_view_only_its_own()
    {
        var session = TwoCitySession();
        var model = Model(session);

        var all = model.TrainingFor(RecruitUnitDialogModel.AllCitiesId);
        Assert.Equal(2, all.Count);
        Assert.Equal((RomeCityId, "light_infantry", 0), (all[0].TargetCityId, all[0].UnitTypeId, all[0].SlotIndex));
        Assert.Equal((LuceriaCityId, "archers", 1), (all[1].TargetCityId, all[1].UnitTypeId, all[1].SlotIndex));

        var luceria = Assert.Single(model.TrainingFor(LuceriaCityId));
        Assert.Equal("archers", luceria.UnitTypeId);
        Assert.Equal("light_infantry", Assert.Single(model.TrainingFor(RomeCityId)).UnitTypeId);

        Assert.Equal("Luceria", model.CityName(LuceriaCityId), ignoreCase: true);
    }

    [Fact]
    public void The_dialog_opens_on_All_cities_only_when_a_unit_trains_outside_the_capital()
    {
        var session = RomeSession();
        Assert.False(Model(session).OpensOnAllCities);

        Recruit(session, RomeCityId, "light_infantry", 200);
        Assert.False(Model(session).OpensOnAllCities);

        Recruit(session, LuceriaCityId, "archers", 400);
        Assert.True(Model(session).OpensOnAllCities);
    }

    [Fact]
    public void All_cities_offers_no_recruit()
    {
        var model = Model(TwoCitySession());
        Assert.False(model.CanRecruitAt(RecruitUnitDialogModel.AllCitiesId));
        Assert.NotNull(model.RecruitRefusalReasonAt(RecruitUnitDialogModel.AllCitiesId));
        Assert.True(RecruitUnitDialogModel.IsAllCities(RecruitUnitDialogModel.AllCitiesId));
        Assert.DoesNotContain(model.ListedCities, c => RecruitUnitDialogModel.IsAllCities(c.Id));
    }

    [Fact]
    public void Mobilize_is_offered_only_for_a_ready_row_and_Disband_for_any_row()
    {
        var session = TwoCitySession();
        var notReady = Model(session).TrainingFor(RecruitUnitDialogModel.AllCitiesId)[1];
        Assert.False(RecruitUnitDialogModel.MayMobilize(notReady));
        Assert.True(RecruitUnitDialogModel.MayDisband(notReady));
        Assert.False(RecruitUnitDialogModel.MayMobilize(null));
        Assert.False(RecruitUnitDialogModel.MayDisband(null));

        EndTurns(session, 8);
        var ready = Model(session).TrainingFor(RecruitUnitDialogModel.AllCitiesId)[1];
        Assert.True(RecruitUnitDialogModel.MayMobilize(ready));
    }

    [Fact]
    public void Mobilize_and_Disband_from_All_cities_submit_the_same_line_and_reach_the_same_state_as_the_city_view()
    {
        var viaAll = TwoCitySession();
        var viaCity = TwoCitySession();
        EndTurns(viaAll, 8);
        EndTurns(viaCity, 8);

        var allRow = Model(viaAll).TrainingFor(RecruitUnitDialogModel.AllCitiesId)
            .Single(r => r.TargetCityId == LuceriaCityId);
        var cityRow = Model(viaCity).TrainingFor(LuceriaCityId).Single();
        Assert.Equal(cityRow.SlotIndex, allRow.SlotIndex);

        var lineAll = Model(viaAll).MobilizeLine(allRow.SlotIndex, RecruitUnitDialogModel.NextArmyId(viaAll.State));
        var lineCity = Model(viaCity).MobilizeLine(cityRow.SlotIndex, RecruitUnitDialogModel.NextArmyId(viaCity.State));
        Assert.Equal(lineCity, lineAll);
        Assert.Equal(viaCity.Submit(lineCity).Lines, viaAll.Submit(lineAll).Lines);
        Assert.Equal(viaCity.State.Armies.Count, viaAll.State.Armies.Count);
        Assert.Single(Model(viaAll).TrainingFor(RecruitUnitDialogModel.AllCitiesId));

        // The receiving army is the one the engine gives for the row's own city, from either view.
        var target = Model(viaAll).MobilizationTargetFor(0, "army-x");
        Assert.Equal(Model(viaCity).MobilizationTargetFor(0, "army-x"), target);

        var disbandAll = Model(viaAll).DisbandSlotLine(0);
        Assert.Equal(Model(viaCity).DisbandSlotLine(0), disbandAll);
        Assert.Equal(viaCity.Submit(disbandAll).Lines, viaAll.Submit(disbandAll).Lines);
        Assert.Empty(Model(viaAll).TrainingFor(RecruitUnitDialogModel.AllCitiesId));
    }

    [Fact]
    public void The_readiness_text_reads_weeks_until_ready_and_then_ready_at_the_boundary()
    {
        var session = RomeSession();
        Recruit(session, LuceriaCityId, "archers", 400);
        var threshold = session.Ruleset.Recruitment.MobilizationMinStateCodeHumanSeat;
        var step = session.Ruleset.Calendar.CityUnitStateCodeStep;
        var weeksToReady = threshold / step;

        EndTurns(session, weeksToReady - 1);
        var oneBefore = Model(session).TrainingFor(RecruitUnitDialogModel.AllCitiesId).Single();
        Assert.False(oneBefore.IsReady);
        Assert.Equal("1 week", oneBefore.ReadinessText);

        EndTurns(session, 1);
        var ready = Model(session).TrainingFor(RecruitUnitDialogModel.AllCitiesId).Single();
        Assert.True(ready.IsReady);
        Assert.Equal("ready", ready.ReadinessText);
    }
}
