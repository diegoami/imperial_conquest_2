using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Import;
using Xunit;

namespace IC2.Engine.Tests.Ui;

public sealed class RetreatSupplyPersistenceTests
{
    [Fact]
    public void Per_unit_mode_survives_native_save_and_older_saves_load_with_no_mode()
    {
        var session = new GameSession(RealGameData.World, RealGameData.Ruleset,
            RealGameData.Scenario, seedOverride: 23, humanSeatNationId: "rome");
        var army = session.State.Armies.First(a => a.Nation == "rome" && !a.IsEmbarked);
        var plan = new RetreatSupplyPlan(army.Id, army.Nation, IsFleet: false);
        Assert.Empty(session.RetreatSupplyPlans);
        session.SetRetreatSupplyPlan(plan);
        var path = Path.Combine(Path.GetTempPath(), $"ic2-recovery-{Guid.NewGuid():N}.json");
        try
        {
            Assert.Contains(session.Submit($"save {path}").Lines,
                line => line.StartsWith("Saved to", StringComparison.Ordinal));
            var save = SaveManager.LoadFile(path, session.World, session.Ruleset);
            Assert.Equal(plan, new GameSession(session.World, session.Ruleset, session.Scenario,
                save).RetreatSupplyPlanFor(army.Id, isFleet: false));
            Assert.Empty(new GameSession(session.World, session.Ruleset, session.Scenario,
                save with { RetreatSupplyPlans = null }).RetreatSupplyPlans);
            session.ClearRetreatSupplyPlan(army.Id, isFleet: false);
            Assert.Contains(session.Submit($"load {path}").Lines,
                line => line.StartsWith("Loaded", StringComparison.Ordinal));
            Assert.Equal(plan, session.RetreatSupplyPlanFor(army.Id, isFleet: false));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void An_army_mode_requires_explicit_confirmation_before_replacing_a_fixed_target()
    {
        var session = new GameSession(RealGameData.World, RealGameData.Ruleset,
            RealGameData.Scenario, seedOverride: 23, humanSeatNationId: "rome");
        var army = session.State.Armies.First(a => a.Nation == "rome" && !a.IsEmbarked);
        session.SetArmyTravelTarget(new ArmyTravelTarget(army.Id, army.Nation, army.X, army.Y));

        Assert.Throws<InvalidOperationException>(() => session.SetRetreatSupplyPlan(
            new RetreatSupplyPlan(army.Id, army.Nation, IsFleet: false)));
        Assert.Empty(session.RetreatSupplyPlans);
        Assert.True(session.ClearArmyTravelTarget(army.Id));
        session.SetRetreatSupplyPlan(new RetreatSupplyPlan(army.Id, army.Nation, IsFleet: false));
        Assert.NotNull(session.RetreatSupplyPlanFor(army.Id, isFleet: false));
    }
}
