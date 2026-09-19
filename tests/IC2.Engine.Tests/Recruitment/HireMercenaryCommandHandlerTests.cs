using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T13 Recruitment and mercenaries", Done-when 2 and 4: the Felsina hire
/// (6,438 troops, "very good") costs the confirmed formula exactly, is debited from the <strong>army
/// purse</strong> with the <strong>treasury unchanged</strong>, and leaves the pool slot at the
/// <c>0xFFFF</c> sentinel (modelled as absent, matching <see cref="MercenaryPoolSlot"/>'s own "empty
/// slots are simply absent" convention); and the 100,000-troop army cap blocks an over-cap hire with a
/// typed rejection.
/// </summary>
public sealed class HireMercenaryCommandHandlerTests
{
    // mercenary-pool-record.md's confirmed Felsina offer: x=98, y=31, label=11 ("Gallic"), type 0 (light
    // infantry), troops 6,438, quality 8 ("very good").
    private static MercenaryPoolSlot FelsinaOffer => new(
        SlotIndex: 33, NameLabel: 11, UnitTypeId: "light_infantry", Troops: 6438, Quality: 8);

    [Fact]
    public void Felsina_hire_costs_the_confirmed_formula_debits_the_army_purse_and_empties_the_pool_slot()
    {
        var sink = new RecordingEventSink();
        var dispatcher = RecruitmentTestbed.Dispatcher(sink);
        var initial = RecruitmentTestbed.InitialState();

        var army = initial.ArmyById("north-army-1")!;
        var nation = initial.NationById("north")!;
        var before = RecruitmentTestbed.WithMercenaryPool(initial, FelsinaOffer);

        var expectedCost = FixtureCorpus.Get("mercenary.felsina.troops").AsInt() * 1 / 1000
            * FixtureCorpus.Get("mercenary.felsina.qualityCode").AsInt(); // (6438*1)/1000*8 = 48.
        Assert.Equal(48, expectedCost);

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, FelsinaOffer.SlotIndex));

        Assert.True(result.IsAccepted);
        var hired = Assert.IsType<MercenaryHired>(Assert.Single(sink.Events));
        Assert.Equal(expectedCost, hired.TalentsPaid);

        // Debited from the ARMY's own purse.
        var updatedArmy = result.State.ArmyById(army.Id)!;
        Assert.Equal(army.Money - expectedCost, updatedArmy.Money);

        // Treasury UNCHANGED.
        var updatedNation = result.State.NationById(nation.Id)!;
        Assert.Equal(nation.Treasury, updatedNation.Treasury);

        // The pool slot is consumed: 0xFFFF sentinel, modelled as absent.
        Assert.Empty(result.State.MercenaryPool);

        // The hired unit is appended with the marker set from the offer's Label.
        var hiredUnit = Assert.Single(updatedArmy.Units, u => u.Troops == FelsinaOffer.Troops);
        Assert.True(hiredUnit.IsMercenary);
        Assert.Equal(FelsinaOffer.NameLabel, hiredUnit.MercenaryLabel);
        Assert.Equal(FelsinaOffer.UnitTypeId, hiredUnit.UnitTypeId);
        Assert.Equal(FelsinaOffer.Quality, hiredUnit.Quality);
    }

    [Fact]
    public void Unknown_army_is_rejected_and_changes_nothing()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var before = RecruitmentTestbed.WithMercenaryPool(RecruitmentTestbed.InitialState(), FelsinaOffer);

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, "no-such-army", FelsinaOffer.SlotIndex));

        Assert.Equal(HireMercenaryRejections.UnknownArmy, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void Another_nations_army_is_rejected_and_changes_nothing()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var before = RecruitmentTestbed.WithMercenaryPool(RecruitmentTestbed.InitialState(), FelsinaOffer);
        Assert.Equal("north", before.ActiveNationId);

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, "south-army-1", FelsinaOffer.SlotIndex));

        Assert.Equal(HireMercenaryRejections.NotYourArmy, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void Unknown_pool_slot_is_rejected_and_changes_nothing()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var before = RecruitmentTestbed.InitialState(); // no mercenary pool slots at all.
        var army = before.ArmyById("north-army-1")!;

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, 33));

        Assert.Equal(HireMercenaryRejections.UnknownPoolSlot, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void An_army_that_cannot_afford_the_hire_is_rejected_and_changes_nothing()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var initial = RecruitmentTestbed.InitialState();
        var army = initial.ArmyById("north-army-1")!;
        var poorArmy = army with { Money = 0 }; // Felsina hire costs 48; 0 can't afford it.
        var before = RecruitmentTestbed.WithMercenaryPool(
            RecruitmentTestbed.WithArmy(initial, poorArmy), FelsinaOffer);

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, FelsinaOffer.SlotIndex));

        Assert.Equal(HireMercenaryRejections.InsufficientMoney, result.Code);
        Assert.Same(before, result.State);
    }

    /// <summary>
    /// Done-when 4: "The 100,000-troop army cap blocks an over-cap recruitment with a typed rejection" —
    /// confirmed directly against the mercenary hire function itself
    /// (<c>decompiled-unit-map-orders-and-record-fields.md</c>: "Also confirmed: ... the 100,000-troop
    /// army cap"). north-army-1 already carries 18,500 troops; a 90,000-troop offer pushes it to 108,500.
    /// </summary>
    [Fact]
    public void An_over_cap_hire_is_rejected_with_the_100k_army_cap_and_changes_nothing()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var initial = RecruitmentTestbed.InitialState();
        var army = initial.ArmyById("north-army-1")!;
        Assert.Equal(18_500, army.TotalTroops);

        var richArmy = army with { Money = 1000 }; // plenty to afford the hire itself.
        var bigOffer = new MercenaryPoolSlot(SlotIndex: 5, NameLabel: 7, UnitTypeId: "light_infantry", Troops: 90_000, Quality: 6);
        var before = RecruitmentTestbed.WithMercenaryPool(
            RecruitmentTestbed.WithArmy(initial, richArmy), bigOffer);

        Assert.True(18_500 + bigOffer.Troops > RecruitmentTestbed.Ruleset.ArmyManagement.MaxTroopsPerArmy);

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, bigOffer.SlotIndex));

        Assert.Equal(HireMercenaryRejections.OverArmyTroopCap, result.Code);
        Assert.Same(before, result.State);
    }

    /// <summary>
    /// The confirmed embarked-fleet-space check: an army aboard a fleet with too little remaining
    /// capacity for the hired troops is refused. north-fleet-1 carries 10 ships → capacity 5,000 troops
    /// (<c>transportTroopsPerShip</c>); an already-embarked 4,000-troop army hiring 2,000 more exceeds it.
    /// </summary>
    [Fact]
    public void An_embarked_army_without_enough_fleet_space_is_rejected_and_changes_nothing()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var initial = RecruitmentTestbed.InitialState();
        var fleet = initial.FleetById("north-fleet-1")!;
        Assert.Equal(10, fleet.Ships);

        var army = initial.ArmyById("north-army-1")!;
        var embarkedUnits = ValueList.Of(new UnitSlot(0, "light_infantry", 4000, 6, "Test Battalion"));
        var embarkedArmy = army with { Money = 100, CoveredTileCode = null, AboardFleetId = fleet.Id, Units = embarkedUnits };
        var smallOffer = new MercenaryPoolSlot(SlotIndex: 9, NameLabel: 3, UnitTypeId: "light_infantry", Troops: 2000, Quality: 5);
        var before = RecruitmentTestbed.WithMercenaryPool(
            RecruitmentTestbed.WithArmy(initial, embarkedArmy), smallOffer);

        var capacity = fleet.Ships * RecruitmentTestbed.Ruleset.Naval.TransportTroopsPerShip;
        Assert.True(embarkedArmy.TotalTroops + smallOffer.Troops > capacity);

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, smallOffer.SlotIndex));

        Assert.Equal(HireMercenaryRejections.FleetNoSpace, result.Code);
        Assert.Same(before, result.State);
    }
}
