using IC2.Engine.Armies;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using IC2.Engine.Recruitment.Commands;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// T55 Done-when 1, 3, 5 and 6 at the command seam — <c>FUN_0044a4e0</c> end to end
/// (<c>decompiled-mobilization-and-mercenary-restock.md</c> §1).
/// </summary>
public sealed class MobilizeRecruitSlotCommandHandlerTests
{
    private const string Nation = "north";
    private const string CityId = "training-city";

    private static readonly World World = MobilizationFixture.OpenWorld(20, 20);

    /// <summary>
    /// A state with one city at <c>(10, 10)</c>, the armies given, and one recruitment slot per entry
    /// in <paramref name="slots"/>. The issuing nation is the toy scenario's human seat, which is also
    /// the active one, so the dispatcher's turn check never gets in the way.
    /// </summary>
    private static GameState StateWith(IEnumerable<RecruitmentSlot> slots, params ArmyState[] armies)
    {
        var state = RecruitmentTestbed.InitialState();
        state = state with
        {
            Cities = ValueList.Of(MobilizationFixture.City(CityId, 10, 10, Nation)),
            Armies = ValueList.Of(armies),
        };

        return MobilizationFixture.WithSlots(state, Nation, slots.ToArray());
    }

    private static RecruitmentSlot Slot(string unitTypeId = "light_infantry", int troops = 1_000, int stateCode = 24) =>
        new(CityId, unitTypeId, troops, stateCode);

    /// <summary>
    /// Done-when 1's order of operations, on the happy path: the slot is read, the unit lands in the
    /// adjacent army, it is named, and only then is the slot gone. The city's own garrison is never
    /// touched — "this is not a garrison transfer".
    /// </summary>
    [Fact]
    public void A_ready_slot_becomes_a_named_unit_in_the_adjacent_army_and_the_slot_is_deleted()
    {
        var sink = new RecordingEventSink();
        var army = MobilizationFixture.Army("army-0", Nation, 11, 10, new[] { MobilizationFixture.Unit("1st Foot Battalion") });
        var before = StateWith(new[] { Slot("archers", 3_500, 24) }, army);

        var result = MobilizationFixture.DispatcherOn(World, sink)
            .Dispatch(before, new MobilizeRecruitSlotCommand(Nation, 0, "overflow-army"));

        Assert.True(result.IsAccepted);
        var after = result.State;

        // The unit landed, with the type and troops copied from the slot and quality = state / 4.
        var updatedArmy = after.ArmyById("army-0")!;
        Assert.Equal(2, updatedArmy.Units.Count);
        var mobilized = updatedArmy.Units[1];
        Assert.Equal("archers", mobilized.UnitTypeId);
        Assert.Equal(3_500, mobilized.Troops);
        Assert.Equal(6, mobilized.Quality);
        Assert.Equal(0, mobilized.MercenaryLabel);
        Assert.True(mobilized.IsRegular);

        // Named by T15's own nation-wide scan, not by a second implementation here.
        Assert.Equal(ArmyNaming.NextName(before, Nation, "archers"), mobilized.Name);
        Assert.Equal("1st Bowmen Battalion", mobilized.Name);

        // The slot is gone, no army was created, and the city keeps its (empty) garrison.
        Assert.Empty(after.NationById(Nation)!.RecruitmentSlots);
        Assert.Single(after.Armies);
        Assert.Equal(before.CityById(CityId)!.Garrison, after.CityById(CityId)!.Garrison);

        var published = Assert.IsType<RecruitMobilized>(Assert.Single(sink.Events));
        Assert.Equal("army-0", published.ArmyId);
        Assert.False(published.ArmyWasCreated);
        Assert.Equal(6, published.Quality);
        Assert.Equal("1st Bowmen Battalion", published.UnitName);
    }

    /// <summary>
    /// Done-when 1's failure direction: when the mobilization cannot complete, the slot is still
    /// there. The fixture makes the last step fail — a city with no free neighbouring cell — which is
    /// exactly the case an implementation that deleted the slot first would get wrong.
    /// </summary>
    [Fact]
    public void A_refused_mobilization_leaves_the_slot_and_the_armies_untouched()
    {
        // A 1x1 world: the city's own tile is the only cell there is, and a city tile never qualifies.
        var tinyWorld = MobilizationFixture.OpenWorld(1, 1);
        var state = RecruitmentTestbed.InitialState();
        state = state with
        {
            Cities = ValueList.Of(MobilizationFixture.City(CityId, 0, 0, Nation)),
            Armies = ValueList<ArmyState>.Empty,
        };
        var before = MobilizationFixture.WithSlots(state, Nation, Slot());

        var sink = new RecordingEventSink();
        var result = MobilizationFixture.DispatcherOn(tinyWorld, sink)
            .Dispatch(before, new MobilizeRecruitSlotCommand(Nation, 0, "new-army"));

        Assert.Equal(MobilizeRecruitSlotRejections.NoReceivingArmy, result.Code);
        Assert.Same(before, result.State);
        Assert.Single(before.NationById(Nation)!.RecruitmentSlots);
        Assert.Empty(sink.Events);
    }

    /// <summary>
    /// Done-when 3: the 20-unit cap is the trigger for a second army, not an error. The receiving army
    /// fills to exactly the cap and the next recruit gets a new army beside the city — with the
    /// creation stats and the <c>(+1, +1)</c> placement, and never a 21st unit anywhere.
    /// </summary>
    [Fact]
    public void Filling_an_army_to_the_unit_cap_creates_a_second_army_rather_than_refusing()
    {
        var cap = RecruitmentTestbed.Ruleset.ArmyManagement.MaxUnitsPerArmy;
        var units = Enumerable.Range(1, cap - 1)
            .Select(i => MobilizationFixture.Unit($"{i}th Guards Battalion", "heavy_infantry"));
        var army = MobilizationFixture.Army("army-0", Nation, 11, 10, units);

        var slots = new[] { Slot("light_infantry", 1_000), Slot("light_infantry", 2_000) };
        var before = StateWith(slots, army);
        var dispatcher = MobilizationFixture.DispatcherOn(World);

        // Descending slot order, as the original's dialog iterates.
        var first = dispatcher.Dispatch(before, new MobilizeRecruitSlotCommand(Nation, 1, "army-1"));
        Assert.True(first.IsAccepted);
        Assert.Equal(cap, first.State.ArmyById("army-0")!.Units.Count);
        Assert.Single(first.State.Armies);

        var second = dispatcher.Dispatch(first.State, new MobilizeRecruitSlotCommand(Nation, 0, "army-1"));
        Assert.True(second.IsAccepted);

        var filled = second.State.ArmyById("army-0")!;
        var created = second.State.ArmyById("army-1")!;

        Assert.Equal(cap, filled.Units.Count);                       // never a 21st unit
        Assert.Equal(2_000, filled.Units[cap - 1].Troops);           // the first recruit stayed put
        Assert.Single(created.Units);
        Assert.Equal(1_000, created.Units[0].Troops);
        Assert.Equal(11, created.X);                                  // city (10, 10) + (+1, +1)
        Assert.Equal(11, created.Y);
        Assert.Equal(RecruitmentTestbed.Ruleset.ArmyManagement.NewArmyMorale, created.Morale);
        Assert.Equal(0, created.Money);
        Assert.Equal(0, created.SupplyTons);
        Assert.Empty(second.State.NationById(Nation)!.RecruitmentSlots);
    }

    /// <summary>
    /// Done-when 5 through the command: a gap below an occupied slot is never reused. The army's slot
    /// 0 is an exhausted unit and slot 1 is live, so the recruit lands in slot 2 and the hole stays
    /// where it is.
    /// </summary>
    [Fact]
    public void A_gap_below_an_occupied_slot_is_not_reused()
    {
        var army = MobilizationFixture.Army(
            "army-0", Nation, 11, 10,
            new[] { MobilizationFixture.EmptyUnitSlot(), MobilizationFixture.Unit("1st Foot Battalion") });
        var before = StateWith(new[] { Slot("archers", 3_500) }, army);

        var result = MobilizationFixture.DispatcherOn(World)
            .Dispatch(before, new MobilizeRecruitSlotCommand(Nation, 0, "new-army"));

        Assert.True(result.IsAccepted);
        var units = result.State.ArmyById("army-0")!.Units;
        Assert.Equal(3, units.Count);
        Assert.Equal(0, units[0].Troops);              // the hole is still a hole
        Assert.Equal("1st Foot Battalion", units[1].Name);
        Assert.Equal(3_500, units[2].Troops);          // the recruit went past it
    }

    /// <summary>
    /// The other side of Done-when 5, and the second branch of the write: a hole <em>above</em> the
    /// highest occupied slot is the free slot, so the recruit replaces it rather than lengthening the
    /// army.
    /// </summary>
    [Fact]
    public void A_gap_above_the_highest_occupied_slot_is_where_the_recruit_lands()
    {
        var army = MobilizationFixture.Army(
            "army-0", Nation, 11, 10,
            new[] { MobilizationFixture.Unit("1st Foot Battalion"), MobilizationFixture.EmptyUnitSlot() });
        var before = StateWith(new[] { Slot("archers", 3_500) }, army);

        var result = MobilizationFixture.DispatcherOn(World)
            .Dispatch(before, new MobilizeRecruitSlotCommand(Nation, 0, "new-army"));

        Assert.True(result.IsAccepted);
        var units = result.State.ArmyById("army-0")!.Units;
        Assert.Equal(2, units.Count);
        Assert.Equal("1st Foot Battalion", units[0].Name);
        Assert.Equal(3_500, units[1].Troops);
        Assert.Equal("1st Bowmen Battalion", units[1].Name);
    }

    /// <summary>
    /// Done-when 6: deleting a slot compacts the list, so the slots above it move down — which is why
    /// a multi-slot mobilization runs in descending order.
    /// </summary>
    [Fact]
    public void Deleting_a_slot_compacts_the_queue()
    {
        var army = MobilizationFixture.Army("army-0", Nation, 11, 10, new[] { MobilizationFixture.Unit("1st Foot Battalion") });
        var before = StateWith(
            new[] { Slot("archers", 100), Slot("light_cavalry", 200), Slot("heavy_cavalry", 300) }, army);

        var result = MobilizationFixture.DispatcherOn(World)
            .Dispatch(before, new MobilizeRecruitSlotCommand(Nation, 0, "new-army"));

        Assert.True(result.IsAccepted);
        var slots = result.State.NationById(Nation)!.RecruitmentSlots;
        Assert.Equal(2, slots.Count);
        Assert.Equal("light_cavalry", slots[0].UnitTypeId);   // shifted down from index 1
        Assert.Equal("heavy_cavalry", slots[1].UnitTypeId);
    }

    /// <summary>Done-when 7's other half: mobilizing does not touch the mobilization rate.</summary>
    [Fact]
    public void Mobilizing_does_not_change_the_mobilization_rate()
    {
        var army = MobilizationFixture.Army("army-0", Nation, 11, 10, new[] { MobilizationFixture.Unit("1st Foot Battalion") });
        var before = StateWith(new[] { Slot("archers", 3_500) }, army);
        before = RecruitmentTestbed.WithNation(before, before.NationById(Nation)! with { MobilizedPercent = 62 });

        var result = MobilizationFixture.DispatcherOn(World)
            .Dispatch(before, new MobilizeRecruitSlotCommand(Nation, 0, "new-army"));

        Assert.True(result.IsAccepted);
        Assert.Equal(62, result.State.NationById(Nation)!.MobilizedPercent);
    }

    /// <summary>A slot below the seat's readiness threshold is refused, and the state is unchanged.</summary>
    [Fact]
    public void A_slot_that_is_not_ready_is_refused()
    {
        var army = MobilizationFixture.Army("army-0", Nation, 11, 10, new[] { MobilizationFixture.Unit("1st Foot Battalion") });
        var dispatcher = MobilizationFixture.DispatcherOn(World);

        var notReady = StateWith(new[] { Slot("archers", 3_500, stateCode: 14) }, army);
        var refused = dispatcher.Dispatch(notReady, new MobilizeRecruitSlotCommand(Nation, 0, "new-army"));
        Assert.Equal(MobilizeRecruitSlotRejections.SlotNotReady, refused.Code);
        Assert.Same(notReady, refused.State);

        var ready = StateWith(new[] { Slot("archers", 3_500, stateCode: 16) }, army);
        Assert.True(dispatcher.Dispatch(ready, new MobilizeRecruitSlotCommand(Nation, 0, "new-army")).IsAccepted);
    }

    /// <summary>
    /// The AI's threshold through the command: the same state-16 slot a human seat mobilizes is
    /// refused when the issuing nation is computer-controlled, and its state-24 twin is not.
    /// </summary>
    [Fact]
    public void An_ai_seat_is_refused_the_slot_a_human_seat_may_mobilize()
    {
        var army = MobilizationFixture.Army("army-0", Nation, 11, 10, new[] { MobilizationFixture.Unit("1st Foot Battalion") });
        var dispatcher = MobilizationFixture.DispatcherOn(World);

        var state = StateWith(new[] { Slot("archers", 3_500, stateCode: 16) }, army);
        var asAi = RecruitmentTestbed.WithNation(state, state.NationById(Nation)! with { Control = SeatControl.Ai });

        var refused = dispatcher.Dispatch(asAi, new MobilizeRecruitSlotCommand(Nation, 0, "new-army"));
        Assert.Equal(MobilizeRecruitSlotRejections.SlotNotReady, refused.Code);

        var atCap = MobilizationFixture.WithSlots(asAi, Nation, Slot("archers", 3_500, stateCode: 24));
        Assert.True(dispatcher.Dispatch(atCap, new MobilizeRecruitSlotCommand(Nation, 0, "new-army")).IsAccepted);
    }

    /// <summary>
    /// The AI's radius through the command: a slot whose only army is five tiles away joins that army
    /// for an AI seat, and gets a brand-new one for a human seat.
    /// </summary>
    [Fact]
    public void An_ai_seat_reaches_an_army_five_tiles_from_the_city_and_a_human_seat_does_not()
    {
        var distant = MobilizationFixture.Army("army-0", Nation, 15, 10, new[] { MobilizationFixture.Unit("1st Foot Battalion") });
        var state = StateWith(new[] { Slot("archers", 3_500) }, distant);
        var dispatcher = MobilizationFixture.DispatcherOn(World);

        var human = dispatcher.Dispatch(state, new MobilizeRecruitSlotCommand(Nation, 0, "new-army"));
        Assert.True(human.IsAccepted);
        Assert.Equal(2, human.State.Armies.Count);
        Assert.Single(human.State.ArmyById("army-0")!.Units);

        var asAi = RecruitmentTestbed.WithNation(state, state.NationById(Nation)! with { Control = SeatControl.Ai });
        var ai = dispatcher.Dispatch(asAi, new MobilizeRecruitSlotCommand(Nation, 0, "new-army"));
        Assert.True(ai.IsAccepted);
        Assert.Single(ai.State.Armies);
        Assert.Equal(2, ai.State.ArmyById("army-0")!.Units.Count);
    }

    /// <summary>An out-of-range slot index is refused.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(99)]
    public void An_out_of_range_slot_index_is_refused(int slotIndex)
    {
        var army = MobilizationFixture.Army("army-0", Nation, 11, 10, new[] { MobilizationFixture.Unit("1st Foot Battalion") });
        var before = StateWith(new[] { Slot() }, army);

        var result = MobilizationFixture.DispatcherOn(World)
            .Dispatch(before, new MobilizeRecruitSlotCommand(Nation, slotIndex, "new-army"));

        Assert.Equal(MobilizeRecruitSlotRejections.UnknownSlot, result.Code);
        Assert.Same(before, result.State);
    }

    /// <summary>
    /// A slot whose training city is no longer in the state — captured and removed, say — is refused
    /// rather than mobilizing from nowhere.
    /// </summary>
    [Fact]
    public void A_slot_whose_city_is_gone_is_refused()
    {
        var army = MobilizationFixture.Army("army-0", Nation, 11, 10, new[] { MobilizationFixture.Unit("1st Foot Battalion") });
        var before = StateWith(new[] { new RecruitmentSlot("vanished-city", "archers", 3_500, 24) }, army);

        var result = MobilizationFixture.DispatcherOn(World)
            .Dispatch(before, new MobilizeRecruitSlotCommand(Nation, 0, "new-army"));

        Assert.Equal(MobilizeRecruitSlotRejections.UnknownCity, result.Code);
        Assert.Same(before, result.State);
    }

    /// <summary>
    /// The new army's id has to be free: reusing an id already on the map is refused rather than
    /// replacing the army that holds it.
    /// </summary>
    [Fact]
    public void A_new_army_id_that_is_already_in_use_is_refused()
    {
        // No army in range, so the command must create one -- and the id it is given is taken.
        var elsewhere = MobilizationFixture.Army("far-away", Nation, 1, 1, new[] { MobilizationFixture.Unit("1st Foot Battalion") });
        var before = StateWith(new[] { Slot() }, elsewhere);

        var result = MobilizationFixture.DispatcherOn(World)
            .Dispatch(before, new MobilizeRecruitSlotCommand(Nation, 0, "far-away"));

        Assert.Equal(MobilizeRecruitSlotRejections.DuplicateArmyId, result.Code);
        Assert.Same(before, result.State);
    }

    /// <summary>
    /// The recruit's quality is the state code it was collected at, permanently — two slots of the
    /// same type and size, mobilized at 16 and at 24, produce a <c>very poor</c> and an
    /// <c>average</c> unit in the same army.
    /// </summary>
    [Fact]
    public void Two_slots_mobilized_at_different_readiness_produce_differently_qualified_units()
    {
        var army = MobilizationFixture.Army("army-0", Nation, 11, 10, new[] { MobilizationFixture.Unit("1st Foot Battalion") });
        var before = StateWith(
            new[] { Slot("archers", 3_500, stateCode: 16), Slot("archers", 3_500, stateCode: 24) }, army);
        var dispatcher = MobilizationFixture.DispatcherOn(World);

        var first = dispatcher.Dispatch(before, new MobilizeRecruitSlotCommand(Nation, 1, "new-army"));
        var second = dispatcher.Dispatch(first.State, new MobilizeRecruitSlotCommand(Nation, 0, "new-army"));

        Assert.True(second.IsAccepted);
        var units = second.State.ArmyById("army-0")!.Units;
        Assert.Equal(6, units[1].Quality);   // the state-24 slot: average
        Assert.Equal(4, units[2].Quality);   // the state-16 slot: very poor, for good
    }

    /// <summary>
    /// The naming scan is nation-wide and skips mercenaries: a hired unit of the same type in the same
    /// army takes no battalion ordinal, so the recruit is still the <c>1st</c>.
    /// </summary>
    [Fact]
    public void A_mercenary_of_the_same_type_does_not_consume_a_battalion_ordinal()
    {
        var mercenary = new UnitSlot(MercenaryLabel: 7, UnitTypeId: "archers", Troops: 2_000, Quality: 8, Name: "Gallic");
        var army = MobilizationFixture.Army("army-0", Nation, 11, 10, new[] { mercenary });
        var before = StateWith(new[] { Slot("archers", 3_500) }, army);

        var result = MobilizationFixture.DispatcherOn(World)
            .Dispatch(before, new MobilizeRecruitSlotCommand(Nation, 0, "new-army"));

        Assert.True(result.IsAccepted);
        Assert.Equal("1st Bowmen Battalion", result.State.ArmyById("army-0")!.Units[1].Name);
    }
}
