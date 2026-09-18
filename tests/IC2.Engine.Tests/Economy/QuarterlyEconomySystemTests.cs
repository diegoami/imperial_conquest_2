using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// End-to-end coverage of <see cref="QuarterlyEconomySystem"/> against real <see cref="GameState"/>,
/// firing through <c>TurnCoordinator.FireQuarterBoundary</c> exactly the way T06's calendar fires it from
/// inside <c>TurnPhase.CalendarAdvance</c>. Rewritten by <c>docs/task-catalogue.md</c> "T39 Quarterly
/// upkeep: who pays, mercenary desertion, and deposition for debt" (bug
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/80">#80</see>): T08's "mutiny"
/// consequence and its treasury-pays-mercenaries rule are gone. The toy world's own north army already
/// carries one mercenary (an archers unit, quality 7, in a purse-256 army) alongside its regular light
/// infantry, which is exactly the mixed roster these tests need.
/// </summary>
public sealed class QuarterlyEconomySystemTests
{
    private static GameState Coordinate(GameState state) =>
        EconomyTestbed.CoordinatorOnly(sink: null, typeof(QuarterlyEconomySystem))
            .FireQuarterBoundary(state, endingSeasonIndex: 0);

    [Fact]
    public void OnQuarterBoundary_RegularsAndShipsAreChargedToTheTreasury_NoBalanceCheck()
    {
        var state = EconomyTestbed.InitialState();
        var north = state.NationById("north")!;
        var south = state.NationById("south")!;

        var after = Coordinate(state);

        // North: 10 ships (30) + the light-infantry regular (15,000 -> 75, docs/task-catalogue.md
        // Done-when 1's own worked value) = 105. The archers mercenary is paid from the army's purse,
        // never the treasury.
        Assert.Equal(north.Treasury - 105, after.NationById("north")!.Treasury);

        // South: 12 ships (36) + the heavy-infantry regular (6,000 -> 60, the other Done-when 1 worked
        // value) = 96. South's single unit is a regular; nothing is paid from its purse at all.
        Assert.Equal(south.Treasury - 96, after.NationById("south")!.Treasury);
    }

    [Fact]
    public void OnQuarterBoundary_NoBalanceCheck_TreasuryGoesNegativeRatherThanBlockingPayment()
    {
        var state = EconomyTestbed.InitialState();
        var poorNations = state.Nations.Select(n => n.Id == "north" ? n with { Treasury = 0 } : n);
        state = state with { Nations = ValueList.From(poorNations) };

        var after = Coordinate(state);

        Assert.Equal(-105, after.NationById("north")!.Treasury); // 0 - 105, no rejection, no floor.
    }

    [Fact]
    public void OnQuarterBoundary_MercenaryWithAPositivePurse_IsPaidFromThePurse_ArmyUnitsUnchanged()
    {
        var state = EconomyTestbed.InitialState();
        var northArmyBefore = state.Armies.First(a => a.Nation == "north");
        var mercenaryBefore = northArmyBefore.Units.Single(u => u.IsMercenary);

        var after = Coordinate(state);
        var northArmyAfter = after.ArmyById(northArmyBefore.Id)!;

        Assert.Equal(northArmyBefore.Units.Count, northArmyAfter.Units.Count); // nobody deserted.
        var mercenaryAfter = northArmyAfter.Units.Single(u => u.IsMercenary);
        Assert.Equal(mercenaryBefore.Troops, mercenaryAfter.Troops);

        // archers, 3,500 troops, quality 7: (3,500/200)*1 = 17, pay = 17*7/5 = 23.
        Assert.Equal(northArmyBefore.Money - 23, northArmyAfter.Money);
    }

    [Fact]
    public void OnQuarterBoundary_MercenaryReachedWithAnEmptyPurse_DesertsAsAWholeUnit()
    {
        var state = EconomyTestbed.InitialState();
        var northArmyBefore = state.Armies.First(a => a.Nation == "north");
        var mercenaryBefore = northArmyBefore.Units.Single(u => u.IsMercenary);
        var regularBefore = northArmyBefore.Units.Single(u => u.IsRegular);

        var armies = state.Armies.Select(a => a.Id == northArmyBefore.Id ? a with { Money = 0 } : a);
        state = state with { Armies = ValueList.From(armies) };

        var after = Coordinate(state);
        var northArmyAfter = after.ArmyById(northArmyBefore.Id)!;

        Assert.Single(northArmyAfter.Units); // the mercenary is gone.
        Assert.True(northArmyAfter.Units[0].IsRegular);
        Assert.Equal(regularBefore.Troops, northArmyAfter.Units[0].Troops); // the regular is untouched.
        Assert.Equal(northArmyBefore.Morale, northArmyAfter.Morale); // no morale write on this path.
    }

    [Fact]
    public void OnQuarterBoundary_Desertion_NeitherWritesMoraleNorPublishesAMessage()
    {
        var state = EconomyTestbed.InitialState();
        var northArmyBefore = state.Armies.First(a => a.Nation == "north");
        var armies = state.Armies.Select(a => a.Id == northArmyBefore.Id ? a with { Money = 0 } : a);
        state = state with { Armies = ValueList.From(armies) };

        var sink = new RecordingEventSink();
        var coordinator = EconomyTestbed.CoordinatorOnly(sink, typeof(QuarterlyEconomySystem));
        var after = coordinator.FireQuarterBoundary(state, endingSeasonIndex: 0);

        Assert.Equal(northArmyBefore.Morale, after.ArmyById(northArmyBefore.Id)!.Morale);
        Assert.Empty(sink.Events); // no event of any kind on the desertion path.
    }

    [Fact]
    public void OnQuarterBoundary_DesertingUnit_TakesItsSupplyShareWithIt()
    {
        var state = EconomyTestbed.InitialState();
        var northArmyBefore = state.Armies.First(a => a.Nation == "north");
        var mercenary = northArmyBefore.Units.Single(u => u.IsMercenary);

        var armies = state.Armies.Select(a =>
            a.Id == northArmyBefore.Id ? a with { Money = 0, SupplyTons = 500 } : a);
        state = state with { Armies = ValueList.From(armies) };

        var after = Coordinate(state);
        var northArmyAfter = after.ArmyById(northArmyBefore.Id)!;

        Assert.Equal(500 - (mercenary.Troops / 100), northArmyAfter.SupplyTons);
    }

    [Fact]
    public void OnQuarterBoundary_RegularsNeverDesert_EvenWithTheArmysPurseDeeplyNegative()
    {
        // South's single unit is a regular; draining its purse must not touch it at all -- the treasury
        // is what pays regulars, and the treasury never even looks at this field.
        var state = EconomyTestbed.InitialState();
        var southArmyBefore = state.Armies.First(a => a.Nation == "south");

        var armies = state.Armies.Select(a => a.Id == southArmyBefore.Id ? a with { Money = -999_999 } : a);
        state = state with { Armies = ValueList.From(armies) };

        var after = Coordinate(state);
        var southArmyAfter = after.ArmyById(southArmyBefore.Id)!;

        Assert.Equal(southArmyBefore.Units.Count, southArmyAfter.Units.Count);
        Assert.Equal(southArmyBefore.TotalTroops, southArmyAfter.TotalTroops);
    }

    [Fact]
    public void OnQuarterBoundary_ANationDeeplyInDebt_KeepsEveryRegularUnit()
    {
        // The report's own Rome recollection: a nation 900+ in debt loses no regular troops at all --
        // there is no morale write and no news message on the treasury side of this rule either.
        var state = EconomyTestbed.InitialState();
        var poorNations = state.Nations.Select(n => n.Id == "south" ? n with { Treasury = -900 } : n);
        state = state with { Nations = ValueList.From(poorNations) };
        var southArmyBefore = state.Armies.First(a => a.Nation == "south");

        var after = Coordinate(state);
        var southArmyAfter = after.ArmyById(southArmyBefore.Id)!;

        Assert.Equal(southArmyBefore.TotalTroops, southArmyAfter.TotalTroops);
        Assert.Equal(-996, after.NationById("south")!.Treasury); // -900 - 96, deeper still, never rejected.
    }

    [Fact]
    public void OnQuarterBoundary_ArmyEmptiedByDesertion_IsDeletedFromTheState()
    {
        var state = EconomyTestbed.InitialState();
        var northArmyBefore = state.Armies.First(a => a.Nation == "north");

        // Strip the army down to just its one mercenary unit, purse empty: it deserts, and with nothing
        // left the army itself is deleted.
        var mercenary = northArmyBefore.Units.Single(u => u.IsMercenary);
        var armies = state.Armies.Select(a =>
            a.Id == northArmyBefore.Id ? a with { Money = 0, Units = ValueList.Of(mercenary) } : a);
        state = state with { Armies = ValueList.From(armies) };

        var after = Coordinate(state);

        Assert.Null(after.ArmyById(northArmyBefore.Id));
        Assert.DoesNotContain(after.Armies, a => a.Id == northArmyBefore.Id);
    }

    /// <summary>
    /// Review round 1, B1: an embarked army deleted by desertion must not leave its carrying fleet
    /// pointing at an army that no longer exists — <see cref="Model.FleetState.CarriedArmyId"/> is the
    /// model's one cross-reference to an army id, and <see cref="Serialization.GameDataValidation.Validate"/>
    /// rejects a dangling one on every save load. <c>[derived]</c>: the report never states whether the
    /// original clears this pointer, the same undocumented-but-necessary status the army-deletion rule
    /// itself already carries (<see cref="MercenaryDesertion"/>'s own remarks).
    /// </summary>
    [Fact]
    public void OnQuarterBoundary_EmbarkedArmyEmptiedByDesertion_AlsoClearsTheFleetsCarriedArmyId()
    {
        var state = EconomyTestbed.InitialState();
        var northArmyBefore = state.Armies.First(a => a.Nation == "north");
        var northFleet = state.Fleets.First(f => f.Nation == "north");
        var mercenary = northArmyBefore.Units.Single(u => u.IsMercenary);

        // Embark the army on north's own fleet, strip it to just its one mercenary, purse empty: it
        // deserts, and with nothing left the army is deleted while still (before the fix) "aboard".
        var armies = state.Armies.Select(a => a.Id == northArmyBefore.Id
            ? a with { Money = 0, Units = ValueList.Of(mercenary), AboardFleetId = northFleet.Id }
            : a);
        var fleets = state.Fleets.Select(f => f.Id == northFleet.Id
            ? f with { CarriedArmyId = northArmyBefore.Id }
            : f);
        state = state with { Armies = ValueList.From(armies), Fleets = ValueList.From(fleets) };

        var after = Coordinate(state);

        Assert.Null(after.ArmyById(northArmyBefore.Id)); // the army is gone, as before this fix.
        Assert.Null(after.FleetById(northFleet.Id)!.CarriedArmyId); // and the fleet no longer points at it.

        // The resulting state round-trips: no dangling reference for GameDataValidation to reject on a
        // reload (the exact failure mode this fix closes -- previously an UnresolvedReferenceException).
        Serialization.GameDataValidation.Validate("probe.json", after);
    }

    [Fact]
    public void OnQuarterBoundary_GarrisonUpkeep_ChargesEveryRecruitmentSlotWithTroops_NotReadyIncluded()
    {
        var state = EconomyTestbed.InitialState();
        var north = state.NationById("north")!;
        var slots = ValueList.Of(
            new RecruitmentSlot("arx", "heavy_infantry", 6_000, StateCode: 0)); // not ready; still billed.
        state = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == "north" ? n with { RecruitmentSlots = slots } : n)) };

        var after = Coordinate(state);

        // 30 (ships) + 75 (the light-infantry regular) + 60 (the garrison slot) = 165.
        Assert.Equal(north.Treasury - 165, after.NationById("north")!.Treasury);
    }

    [Fact]
    public void OnQuarterBoundary_FleetUnderConstruction_PaysNoUpkeep()
    {
        var state = EconomyTestbed.InitialState();
        var north = state.NationById("north")!;
        var fleet = state.Fleets.First(f => f.Nation == "north");
        var underConstruction = state.Fleets.Select(f =>
            f.Id == fleet.Id ? f with { ConstructionTicksRemaining = 12 } : f);
        state = state with { Fleets = ValueList.From(underConstruction) };

        var after = Coordinate(state);

        Assert.Equal(north.Treasury - 75, after.NationById("north")!.Treasury); // no ship upkeep at all.
    }
}
