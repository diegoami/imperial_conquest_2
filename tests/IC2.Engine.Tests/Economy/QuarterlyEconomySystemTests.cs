using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// End-to-end coverage of <see cref="QuarterlyEconomySystem"/> against real <see cref="GameState"/>,
/// firing through <c>TurnCoordinator.FireQuarterBoundary</c> exactly the way T06's calendar fires it from
/// inside <c>TurnPhase.CalendarAdvance</c> — the same reason T06 exposed that entry point directly, so a
/// quarterly-boundary subscriber is testable with no calendar implementation present at all.
/// </summary>
public sealed class QuarterlyEconomySystemTests
{
    [Fact]
    public void OnQuarterBoundary_TreasuryCoversUpkeep_DebitsExactlyShipPlusArmyUpkeepAndNoMutiny()
    {
        var sink = new RecordingEventSink();
        var coordinator = EconomyTestbed.CoordinatorOnly(sink, typeof(QuarterlyEconomySystem));
        var state = EconomyTestbed.InitialState();

        var north = state.NationById("north")!;
        var armiesBefore = state.Armies.Where(a => a.Nation == "north").ToList();
        var fleetsBefore = state.Fleets.Where(f => f.Nation == "north" && !f.IsUnderConstruction).ToList();

        var expectedShipUpkeep = ShipUpkeep.Compute(fleetsBefore.Sum(f => f.Ships), EconomyTestbed.Ruleset);
        var expectedArmyUpkeep = armiesBefore.Sum(a => ArmyUpkeep.Compute(a.Units, EconomyTestbed.Ruleset));

        var after = coordinator.FireQuarterBoundary(state, endingSeasonIndex: 0);

        var northAfter = after.NationById("north")!;
        Assert.Equal(north.Treasury - expectedShipUpkeep - expectedArmyUpkeep, northAfter.Treasury);
        Assert.Equal(
            Math.Max(0, north.Unity - EconomyTestbed.Ruleset.Economy.UnityDecayPerQuarter), northAfter.Unity);

        // No mutiny: the toy world's treasury comfortably covers upkeep, so every army's roster survives
        // unchanged (troop counts are exactly what they started with, unit for unit).
        Assert.Empty(sink.Events.OfType<ArmyMutinied>());
        foreach (var before in armiesBefore)
        {
            var afterArmy = after.ArmyById(before.Id)!;
            for (var i = 0; i < before.Units.Count; i++)
            {
                Assert.Equal(before.Units[i].Troops, afterArmy.Units[i].Troops);
            }
        }
    }

    [Fact]
    public void OnQuarterBoundary_TreasuryCannotCoverUpkeep_MutiniesEveryArmyOfThatNation()
    {
        var sink = new RecordingEventSink();
        var coordinator = EconomyTestbed.CoordinatorOnly(sink, typeof(QuarterlyEconomySystem));
        var state = EconomyTestbed.InitialState();

        // Drain north's treasury so it cannot possibly cover this quarter's upkeep.
        var north = state.NationById("north")!;
        var poorNations = state.Nations.Select(n => n.Id == "north" ? n with { Treasury = 0 } : n);
        state = state with { Nations = ValueList.From(poorNations) };

        var northArmyBefore = state.Armies.First(a => a.Nation == "north");

        var after = coordinator.FireQuarterBoundary(state, endingSeasonIndex: 0);

        var mutinies = sink.Events.OfType<ArmyMutinied>().ToList();
        Assert.Contains(mutinies, e => e.ArmyId == northArmyBefore.Id && e.NationId == "north");

        var northArmyAfter = after.ArmyById(northArmyBefore.Id)!;
        var totalBefore = northArmyBefore.Units.Sum(u => u.Troops);
        var totalAfter = northArmyAfter.Units.Sum(u => u.Troops);
        Assert.True(totalAfter < totalBefore, "An unpaid army must actually lose troops, not merely accrue debt.");

        // South was never touched -- treasury insufficiency is per-nation, not global.
        var southArmyId = state.Armies.First(a => a.Nation == "south").Id;
        Assert.DoesNotContain(mutinies, e => e.ArmyId == southArmyId);
    }

    [Fact]
    public void OnQuarterBoundary_FleetUnderConstruction_PaysNoUpkeep()
    {
        var sink = new RecordingEventSink();
        var coordinator = EconomyTestbed.CoordinatorOnly(sink, typeof(QuarterlyEconomySystem));
        var state = EconomyTestbed.InitialState();

        var north = state.NationById("north")!;
        var fleet = state.Fleets.First(f => f.Nation == "north");
        var underConstruction = state.Fleets.Select(f =>
            f.Id == fleet.Id ? f with { ConstructionTicksRemaining = 12 } : f);
        state = state with { Fleets = ValueList.From(underConstruction) };

        var after = coordinator.FireQuarterBoundary(state, endingSeasonIndex: 0);

        var armyUpkeep = state.Armies.Where(a => a.Nation == "north").Sum(a => ArmyUpkeep.Compute(a.Units, EconomyTestbed.Ruleset));
        Assert.Equal(north.Treasury - armyUpkeep, after.NationById("north")!.Treasury); // no ship upkeep at all.
    }
}
