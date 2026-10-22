using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>The Recruit unit dialog's <em>Max</em> button (the owner's play session of 2026-10-22).</summary>
public sealed class RecruitMaxTests
{
    private static RecruitUnitDialogModel ModelWithTreasury(int treasury)
    {
        var classical = GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");
        var session = new GameSession(classical.World, classical.Ruleset, classical.Scenario, seedOverride: 3, humanSeatNationId: "rome");
        var state = session.State with
        {
            Nations = ValueList.From(session.State.Nations.Select(n => n.Id == "rome" ? n with { Treasury = treasury } : n)),
        };
        return RecruitUnitDialogModel.ForActiveNation(state, session.Ruleset, session.World);
    }

    [Fact]
    public void Max_is_the_bound_when_the_treasury_covers_it()
    {
        var model = ModelWithTreasury(1_000_000);
        const string type = "light_infantry";
        Assert.Equal(model.TroopBoundsFor(type).Maximum, model.AffordableMaximumFor(type));
    }

    [Fact]
    public void Max_is_the_largest_affordable_step_when_the_treasury_is_short()
    {
        const string type = "light_infantry";
        var probe = ModelWithTreasury(1_000_000);
        var bounds = probe.TroopBoundsFor(type);
        var treasury = probe.InitialCostFor(bounds.Maximum / 2, type);
        var model = ModelWithTreasury(treasury);

        var max = model.AffordableMaximumFor(type);

        Assert.True(model.InitialCostFor(max, type) <= treasury);
        Assert.True(max == bounds.Maximum || model.InitialCostFor(max + bounds.Step, type) > treasury);
        Assert.Equal(0, (max - bounds.Minimum) % bounds.Step);
    }
}
