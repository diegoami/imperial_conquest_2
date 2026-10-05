using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Tests.Cities.Capture;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T13 Recruitment and mercenaries", Done-when 2 and 4: the Felsina hire
/// (6,438 troops, "very good") passes the confirmed formula's gate exactly, leaves the <strong>army purse
/// and the treasury unchanged</strong> (T143, bug #755), and leaves the pool slot at the
/// <c>0xFFFF</c> sentinel (modelled as absent, matching <see cref="MercenaryPoolSlot"/>'s own "empty
/// slots are simply absent" convention); and the 100,000-troop army cap blocks an over-cap hire with a
/// typed rejection.
/// </summary>
public sealed class HireMercenaryCommandHandlerTests
{
    // mercenary-pool-record.md's confirmed Felsina offer: x=98, y=31, label=11 ("Gallic"), type 0 (light
    // infantry), troops 6,438, quality 8 ("very good"). T76 (#330): the toy world has no Felsina, so the
    // offer's tile is placed on the toy world's Arx (2,1) -- the one city tile at Chebyshev distance 1
    // from north-army-1 at (3,2), which the player's adjacency rule now requires.
    private static MercenaryPoolSlot FelsinaOffer => new(
        SlotIndex: 33, X: 2, Y: 1, NameLabel: 11, UnitTypeId: "light_infantry", Troops: 6438, Quality: 8);

    // A second, unrelated offer that must survive a Felsina hire untouched -- otherwise a hire that
    // clears the whole pool instead of just the hired slot would pass undetected (build-process.md
    // §4.2 gate 5's two-entity probe; the same gap that blocked T39's mercenary desertion). At Portus
    // (5,2), Chebyshev distance 2 from north-army-1, so it is not a second adjacent offer.
    private static MercenaryPoolSlot OtherOffer => new(
        SlotIndex: 7, X: 5, Y: 2, NameLabel: 3, UnitTypeId: "heavy_infantry", Troops: 2000, Quality: 5);

    [Fact]
    public void Felsina_hire_passes_the_confirmed_gate_leaves_the_army_purse_and_empties_the_pool_slot()
    {
        var sink = new RecordingEventSink();
        var dispatcher = RecruitmentTestbed.Dispatcher(sink);
        var initial = RecruitmentTestbed.InitialState();

        var army = initial.ArmyById("north-army-1")!;
        var nation = initial.NationById("north")!;
        var before = RecruitmentTestbed.WithMercenaryPool(initial, FelsinaOffer, OtherOffer);

        var expectedGate = FixtureCorpus.Get("mercenary.felsina.troops").AsInt() * 1 / 1000
            * FixtureCorpus.Get("mercenary.felsina.qualityCode").AsInt(); // (6438*1)/1000*8 = 48.
        Assert.Equal(48, expectedGate);

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, FelsinaOffer.SlotIndex));

        Assert.True(result.IsAccepted);
        var hired = Assert.IsType<MercenaryHired>(Assert.Single(sink.Events));
        Assert.Equal(expectedGate, hired.HireGate);

        // Bug #755/T143: the ARMY's own purse is UNCHANGED — the gate is not a debit.
        var updatedArmy = result.State.ArmyById(army.Id)!;
        Assert.Equal(army.Money, updatedArmy.Money);

        // Treasury UNCHANGED.
        var updatedNation = result.State.NationById(nation.Id)!;
        Assert.Equal(nation.Treasury, updatedNation.Treasury);

        // The pool slot is consumed: 0xFFFF sentinel, modelled as absent. Only the hired slot is
        // removed — the other offer in the pool survives record-identical, which an over-broad clear
        // of the whole pool (the mutation this test is built to catch) would not leave standing.
        var remainingSlot = Assert.Single(result.State.MercenaryPool);
        Assert.Equal(OtherOffer, remainingSlot);

        // The hired unit is appended with the marker set from the offer's Label.
        var hiredUnit = Assert.Single(updatedArmy.Units, u => u.Troops == FelsinaOffer.Troops);
        Assert.True(hiredUnit.IsMercenary);
        Assert.Equal(FelsinaOffer.NameLabel, hiredUnit.MercenaryLabel);
        Assert.Equal(FelsinaOffer.UnitTypeId, hiredUnit.UnitTypeId);
        Assert.Equal(FelsinaOffer.Quality, hiredUnit.Quality);
    }

    /// <summary>
    /// A hire touches only the hiring army and the mercenary pool: every other army, every nation
    /// (including the hiring nation's own <em>treasury</em>, covered above), every city and every fleet
    /// comes out record-identical.
    /// </summary>
    [Fact]
    public void Felsina_hire_leaves_other_armies_nations_cities_and_fleets_untouched()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var initial = RecruitmentTestbed.InitialState();
        var army = initial.ArmyById("north-army-1")!;
        var otherArmy = initial.ArmyById("south-army-1")!;
        var otherNation = initial.NationById("south")!;
        var before = RecruitmentTestbed.WithMercenaryPool(initial, FelsinaOffer);

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, FelsinaOffer.SlotIndex));

        Assert.True(result.IsAccepted);
        Assert.Equal(otherArmy, result.State.ArmyById(otherArmy.Id));
        Assert.Equal(otherNation, result.State.NationById(otherNation.Id));
        Assert.Equal(initial.Cities, result.State.Cities);
        Assert.Equal(initial.Fleets, result.State.Fleets);
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
        var poorArmy = army with { Money = 0 }; // Felsina hire's gate is 48; 0 is below it.
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

        var richArmy = army with { Money = 1000 }; // plenty to pass the hire's gate itself.
        var bigOffer = new MercenaryPoolSlot(SlotIndex: 5, X: 2, Y: 1, NameLabel: 7, UnitTypeId: "light_infantry", Troops: 90_000, Quality: 6);
        var before = RecruitmentTestbed.WithMercenaryPool(
            RecruitmentTestbed.WithArmy(initial, richArmy), bigOffer);

        Assert.True(18_500 + bigOffer.Troops > RecruitmentTestbed.Ruleset.ArmyManagement.MaxTroopsPerArmy);

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, bigOffer.SlotIndex));

        Assert.Equal(HireMercenaryRejections.OverArmyTroopCap, result.Code);
        Assert.Same(before, result.State);
    }

    /// <summary>
    /// <c>docs/task-catalogue.md</c> T15 Done-when 6 (issue #181): <see cref="Model.ArmyManagementRules.MaxUnitsPerArmy"/>
    /// (20) is now enforced on a mercenary hire, the same shape as <c>JoinArmiesCommandHandler</c>'s own
    /// cap -- inclusive, so 19 units accepting a hire (ending at 20) is still allowed. Small troop counts
    /// throughout, and plenty of money, so only the unit count is under test.
    /// </summary>
    // SupplyTons 1,000, not 0 (bug #769): a hire now passes the original's supply floor of 15 per
    // 10,000 troops, and these small test armies (10 troops a unit) would otherwise be refused before
    // the unit cap this theory exists to probe.
    private static ArmyState ArmyWithUnits(int unitCount, string id = "unit-cap-army") =>
        new(
            id, "north", 3, 2, Moves: 5, Morale: 68, Money: 1000, SupplyTons: 1000,
            CoveredTileCode: 2, AboardFleetId: null,
            Units: ValueList.From(Enumerable.Range(0, unitCount)
                .Select(i => new UnitSlot(0, "light_infantry", 10, 6, $"filler {i}"))));

    [Theory]
    [InlineData(18, 19)] // 18 + 1 hired = 19: well under the cap.
    [InlineData(19, 20)] // 19 + 1 hired = 20: the cap is inclusive, so this still accepts.
    public void A_hire_that_keeps_the_army_at_or_under_twenty_units_is_accepted(int startingUnits, int expectedUnits)
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var initial = RecruitmentTestbed.InitialState();
        var army = ArmyWithUnits(startingUnits);
        var offer = new MercenaryPoolSlot(SlotIndex: 40, X: 2, Y: 1, NameLabel: 3, UnitTypeId: "light_infantry", Troops: 10, Quality: 5);
        var before = RecruitmentTestbed.WithMercenaryPool(
            initial with { Armies = ValueList.From(initial.Armies.Append(army)) }, offer);

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, offer.SlotIndex));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(expectedUnits, result.State.ArmyById(army.Id)!.Units.Count);
    }

    /// <summary>
    /// The exact scenario T13's reviewer probed (T15 Done-when 6, issue #181): an army already holding 20
    /// units accepts a hire and ends at 21, a shape the original's 20-slot army record cannot hold.
    /// </summary>
    [Fact]
    public void An_army_already_at_twenty_units_is_rejected_from_reaching_twentyOne_and_changes_nothing()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var initial = RecruitmentTestbed.InitialState();
        var army = ArmyWithUnits(20);
        var offer = new MercenaryPoolSlot(SlotIndex: 41, X: 2, Y: 1, NameLabel: 3, UnitTypeId: "light_infantry", Troops: 10, Quality: 5);
        var before = RecruitmentTestbed.WithMercenaryPool(
            initial with { Armies = ValueList.From(initial.Armies.Append(army)) }, offer);

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, offer.SlotIndex));

        Assert.Equal(HireMercenaryRejections.OverArmyUnitCap, result.Code);
        Assert.Same(before, result.State);
        Assert.Equal(20, before.ArmyById(army.Id)!.Units.Count); // still 20, never reached 21.
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
        var smallOffer = new MercenaryPoolSlot(SlotIndex: 9, X: 2, Y: 1, NameLabel: 3, UnitTypeId: "light_infantry", Troops: 2000, Quality: 5);
        var before = RecruitmentTestbed.WithMercenaryPool(
            RecruitmentTestbed.WithArmy(initial, embarkedArmy), smallOffer);

        var capacity = fleet.Ships * RecruitmentTestbed.Ruleset.Naval.TransportTroopsPerShip;
        Assert.True(embarkedArmy.TotalTroops + smallOffer.Troops > capacity);

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, smallOffer.SlotIndex));

        Assert.Equal(HireMercenaryRejections.FleetNoSpace, result.Code);
        Assert.Same(before, result.State);
    }

    // ---- T76 (#330, bug #325): the player's position gate ----

    /// <summary>A city on <paramref name="state"/> at (<paramref name="x"/>,<paramref name="y"/>) owned by <paramref name="owner"/>.</summary>
    private static GameState WithCityAt(GameState state, string id, int x, int y, string owner) =>
        state with
        {
            Cities = ValueList.From(state.Cities.Append(CaptureTestbed.City(
                id, id, x, y, owner, owner, loyalty: 50, fortificationCode: 0,
                populationThousands: 10, maxPopulationThousands: 20, tribute: 0))),
        };

    private static MercenaryPoolSlot SmallOffer(int slotIndex, int x, int y) =>
        new(SlotIndex: slotIndex, X: x, Y: y, NameLabel: 3, UnitTypeId: "light_infantry", Troops: 10, Quality: 5);

    /// <summary>
    /// Distance 1: an offer on a city tile adjacent to the army is reachable. north-army-1 sits at
    /// (3,2) and the toy world's Arx is at (2,1) — Chebyshev distance exactly 1.
    /// </summary>
    [Fact]
    public void An_offer_on_an_adjacent_city_tile_is_accepted()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var before = RecruitmentTestbed.WithMercenaryPool(RecruitmentTestbed.InitialState(), SmallOffer(39, 2, 1));

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, "north-army-1", 39));

        Assert.True(result.IsAccepted, result.ToString());
    }

    /// <summary>
    /// Distance 0: an offer on the army's own tile is not adjacent. A city is placed on that tile too, so
    /// the test would pass only if the gate were mistakenly "at most 1" — which the original's
    /// <c>FUN_004492A0</c> is not.
    /// </summary>
    [Fact]
    public void An_offer_on_the_armys_own_tile_is_rejected_as_not_adjacent()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var initial = RecruitmentTestbed.InitialState();
        var army = initial.ArmyById("north-army-1")!;
        var withCity = WithCityAt(initial, "camp", army.X, army.Y, "north");
        var before = RecruitmentTestbed.WithMercenaryPool(withCity, SmallOffer(39, army.X, army.Y));

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, 39));

        Assert.Equal(HireMercenaryRejections.NoAdjacentOffer, result.Code);
        Assert.Same(before, result.State);
    }

    /// <summary>
    /// Distance 2: Portus sits two tiles east of north-army-1, so its offer is out of reach.
    /// </summary>
    [Fact]
    public void An_offer_two_tiles_away_is_rejected_as_not_adjacent()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var before = RecruitmentTestbed.WithMercenaryPool(RecruitmentTestbed.InitialState(), SmallOffer(39, 5, 2));
        Assert.Equal(2, System.Math.Max(System.Math.Abs(5 - 3), System.Math.Abs(2 - 2)));

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, "north-army-1", 39));

        Assert.Equal(HireMercenaryRejections.NoAdjacentOffer, result.Code);
        Assert.Same(before, result.State);
    }

    /// <summary>
    /// Two offer cities both adjacent to one army: <c>FUN_00449D08</c> picks the first live offer in slot
    /// order (slot 5, on the added east-town at (4,2)) and the dialog lists only that city's tile, so the
    /// higher-numbered <em>other</em> city's offer (Arx, slot 9) cannot be hired. The two cities are
    /// Chebyshev distance 2 apart, the only shape in which the original's collision can happen.
    /// </summary>
    [Fact]
    public void Only_the_first_adjacent_live_offers_city_is_available()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var initial = RecruitmentTestbed.InitialState();
        var army = initial.ArmyById("north-army-1")!;
        Assert.Equal(2, System.Math.Max(System.Math.Abs(4 - 2), System.Math.Abs(2 - 1)));

        var withEastTown = WithCityAt(initial, "east-town", 4, 2, "north");
        var firstOffer = SmallOffer(5, 4, 2);   // east-town: the lowest live slot, so its city is chosen.
        var secondOffer = SmallOffer(9, 2, 1);  // Arx: adjacent too, but not on the chosen city.
        var before = RecruitmentTestbed.WithMercenaryPool(withEastTown, firstOffer, secondOffer);

        var second = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, secondOffer.SlotIndex));
        Assert.Equal(HireMercenaryRejections.OfferNotAdjacentCity, second.Code);
        Assert.Same(before, second.State);

        var first = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, firstOffer.SlotIndex));
        Assert.True(first.IsAccepted, first.ToString());
    }

    /// <summary>
    /// An offer on an adjacent city whose owner is at war with the hiring nation is refused with the
    /// order's own <c>"You cannot recruit from an enemy city."</c> gate.
    /// </summary>
    [Fact]
    public void An_offer_on_a_city_at_war_with_the_hiring_nation_is_rejected()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var initial = RecruitmentTestbed.InitialState();
        var army = initial.ArmyById("north-army-1")!;
        var withEnemyTown = WithCityAt(initial, "enemy-town", 4, 2, "south");
        var atWar = withEnemyTown with
        {
            Relations = withEnemyTown.Relations.WithRelation(
                "north", "south", RecruitmentTestbed.Ruleset.Diplomacy.StateCodes.War),
        };
        var before = RecruitmentTestbed.WithMercenaryPool(atWar, SmallOffer(39, 4, 2));

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, 39));

        Assert.Equal(HireMercenaryRejections.EnemyCity, result.Code);
        Assert.Same(before, result.State);
    }

    /// <summary>
    /// R5: the enemy-city refusal sits after the 20-unit and 100,000-troop refusals, as the original's
    /// <c>TUnitMap_RecruitMercenaries</c> checks them first. A full army standing next to an at-war offer
    /// city must still return the pre-T76 <see cref="HireMercenaryRejections.OverArmyUnitCap"/>, not the
    /// new enemy-city code.
    /// </summary>
    [Fact]
    public void A_full_army_next_to_an_enemy_offer_city_is_refused_by_the_unit_cap_first()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var initial = RecruitmentTestbed.InitialState();
        var army = ArmyWithUnits(20);
        var withArmy = initial with { Armies = ValueList.From(initial.Armies.Append(army)) };
        var withEnemyTown = WithCityAt(withArmy, "enemy-town", 4, 2, "south");
        var atWar = withEnemyTown with
        {
            Relations = withEnemyTown.Relations.WithRelation(
                "north", "south", RecruitmentTestbed.Ruleset.Diplomacy.StateCodes.War),
        };
        var offer = new MercenaryPoolSlot(SlotIndex: 42, X: 4, Y: 2, NameLabel: 3, UnitTypeId: "light_infantry", Troops: 10, Quality: 5);
        var before = RecruitmentTestbed.WithMercenaryPool(atWar, offer);

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, offer.SlotIndex));

        Assert.Equal(HireMercenaryRejections.OverArmyUnitCap, result.Code);
        Assert.Same(before, result.State);
    }

    /// <summary>
    /// R4: the human seat's range is read from the ruleset, never written as a literal. Raise
    /// <c>mercenaryHireRangeHumanSeat</c> to 2 and the same Portus offer at Chebyshev distance 2 from
    /// north-army-1 is accepted; a handler hard-coding <c>== 1</c> rejects it with
    /// <see cref="HireMercenaryRejections.NoAdjacentOffer"/>.
    /// </summary>
    [Fact]
    public void The_human_range_comes_from_the_ruleset_not_a_literal()
    {
        var ruleset = RecruitmentTestbed.Ruleset;
        Assert.Equal(1, ruleset.Recruitment.MercenaryHireRangeHumanSeat);

        var widened = ruleset with
        {
            Recruitment = ruleset.Recruitment with { MercenaryHireRangeHumanSeat = 2 },
        };
        var dispatcher = new CommandDispatcher(
            SystemRegistry.FromEngineAssembly(), widened, CoreTestbed.Toy.World, NullEventSink.Instance);

        var offer = SmallOffer(39, 5, 2); // Portus, Chebyshev distance 2 from (3,2).
        Assert.Equal(2, System.Math.Max(System.Math.Abs(5 - 3), System.Math.Abs(2 - 2)));
        var before = RecruitmentTestbed.WithMercenaryPool(RecruitmentTestbed.InitialState(), offer);

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, "north-army-1", offer.SlotIndex));

        Assert.True(result.IsAccepted, result.ToString());
    }

    /// <summary>
    /// R8: <c>FUN_004498D8</c> skips an adjacent offer whose tile holds no city and keeps looking, so a
    /// higher-numbered adjacent offer on a city still wins. If the off-city slot (4, at (3,1)) were
    /// chosen anyway, <c>CityAt</c> would resolve to no city and the on-city Arx offer (9, at (2,1))
    /// would be unreachable.
    /// </summary>
    [Fact]
    public void An_adjacent_offer_with_no_city_is_skipped_and_the_next_adjacent_offer_on_a_city_wins()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var initial = RecruitmentTestbed.InitialState();
        var offCity = SmallOffer(4, 3, 1);   // adjacent to north-army-1 at (3,2), but no city on (3,1).
        var onCity = SmallOffer(9, 2, 1);    // Arx: adjacent too, and on a city tile.
        var before = RecruitmentTestbed.WithMercenaryPool(initial, offCity, onCity);

        // The lower-numbered, off-city offer maps onto Arx's tile instead, because Arx is the chosen city.
        var offCityResult = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, "north-army-1", offCity.SlotIndex));
        Assert.Equal(HireMercenaryRejections.OfferNotAdjacentCity, offCityResult.Code);

        // The on-city offer is reachable only because the off-city one was skipped rather than chosen.
        var onCityResult = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, "north-army-1", onCity.SlotIndex));
        Assert.True(onCityResult.IsAccepted, onCityResult.ToString());
    }
}
