using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T140 (docs/tasks/T140.md, Done-when 2): the city, own-army, foreign-army, own-fleet, foreign-fleet
/// and fleet-army panels built by <see cref="InformationPanelModel"/> on the shipped
/// <c>classical-mediterranean</c> world with Rome as the viewer. Each assertion reads the live state
/// or the ruleset — no C# literal hardcodes a figure the engine or the ruleset should be reading.
/// </summary>
/// <remarks>
/// Tests are intentionally split per panel so a regression in one branch names the file:line the
/// model loses. Cities pick Rome (the capital marker), Capua (a non-capital own city) and Carthago
/// (a foreign capital). Armies pick army-0 (Rome's first — own), army-2 (Carthage's — foreign to
/// Rome). Fleets pick fleet-0 (Carthage's — foreign to Rome, the only Rome-start fleet is none). The
/// Roman-fleet and fleet-carrying-army cases construct a state that has them.
/// </remarks>
public sealed class InformationPanelModelTests
{
    private const string RomeId = "rome";
    private const string CarthageId = "carthage";
    private const string RomeCityId = "rome";
    private const string RomeCapitalMarker = "  (capital of Rome)";
    private const string CapuaCityId = "capua";
    private const string CarthagoCityId = "carthago";
    private const string RomanArmyId = "army-0";
    private const string CarthaginianArmyId = "army-2";
    private const string CarthaginianFleetId = "fleet-0";

    private static ResolvedScenario Classical() =>
        GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");

    private static GameSession RomeSession(ulong seed = 1)
    {
        var classical = Classical();
        return new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: seed, humanSeatNationId: RomeId);
    }

    private static IReadOnlyDictionary<string, string> ByKey(IReadOnlyList<InformationPanelLine> lines) =>
        lines
            .Where(line => line.Key.Length > 0 && !string.Equals(line.Key, InformationPanelModel.ArmyBlankKey, StringComparison.Ordinal))
            .ToDictionary(line => line.Key, line => line.Text, StringComparer.Ordinal);

    private static CityState City(GameState state, string id) =>
        state.CityById(id) ?? throw new InvalidOperationException($"No city '{id}' in the state.");

    private static ArmyState Army(GameState state, string id) =>
        state.ArmyById(id) ?? throw new InvalidOperationException($"No army '{id}' in the state.");

    private static FleetState Fleet(GameState state, string id) =>
        state.FleetById(id) ?? throw new InvalidOperationException($"No fleet '{id}' in the state.");

    // ========================================================================
    // City panel
    // ========================================================================

    [Fact]
    public void Romes_city_panel_includes_the_capital_marker_and_the_full_facts_list()
    {
        var session = RomeSession();
        var state = session.State;
        var city = City(state, RomeCityId);
        var ruleset = session.Ruleset;
        var rome = state.NationById(RomeId)!;
        var world = session.World;

        var lines = InformationPanelModel.City(state, world, ruleset, city, viewerNationId: RomeId);
        var byKey = ByKey(lines);

        // C01: capital of the city's controller (Rome), because rome's CapitalCityId == RomeCityId.
        Assert.Equal(city.Name + RomeCapitalMarker, byKey[InformationPanelModel.CityNameKey]);
        Assert.Equal($"Controlled by: {rome.Name}", byKey[InformationPanelModel.CityControlledByKey]);
        Assert.Equal($"Allegiance to: {rome.Name}", byKey[InformationPanelModel.CityAllegianceToKey]);

        // C04: Population = pop × 1000 + trunc(pop × 100 / maxPop)%.
        var percent = city.PopulationThousands * 100 / city.MaxPopulationThousands;
        Assert.Equal(
            $"Population: {city.PopulationThousands * 1000} ({percent}%)",
            byKey[InformationPanelModel.CityPopulationKey]);

        // C05: Loyalty band word.
        Assert.Equal(
            $"Loyalty: {InformationWords.Loyalty(city.Loyalty)}",
            byKey[InformationPanelModel.CityLoyaltyKey]);

        // C06: Fortification. Rome's classical fortification is 78, so the under-construction
        // marker is absent.
        Assert.Equal("Fortification: 78%", byKey[InformationPanelModel.CityFortificationKey]);

        // C08: Tribute = CityTaxContribution.Compute in talents (the same number the engine uses
        // for the quarterly rebuild).
        var expectedTribute = CityTaxContribution.Compute(city);
        Assert.Equal(
            $"Tribute: {expectedTribute} talents",
            byKey[InformationPanelModel.CityTributeKey]);

        // C10: Supply with tons. Rome's starting supply is 990; the brief's expectation matches.
        Assert.Equal($"Supply: {city.SupplyTons} tons", byKey[InformationPanelModel.CitySupplyKey]);
    }

    [Fact]
    public void A_non_capital_own_city_omits_the_capital_marker()
    {
        var session = RomeSession();
        var state = session.State;
        var city = City(state, CapuaCityId);
        var rome = state.NationById(RomeId)!;

        var lines = InformationPanelModel.City(
            state, session.World, session.Ruleset, city, viewerNationId: RomeId);
        var byKey = ByKey(lines);

        // C01: Capua's name only — no capital marker.
        Assert.Equal(city.Name, byKey[InformationPanelModel.CityNameKey]);
        Assert.Equal($"Controlled by: {rome.Name}", byKey[InformationPanelModel.CityControlledByKey]);
    }

    [Fact]
    public void A_foreign_city_carries_the_tribute_word_and_a_blank_supply_line()
    {
        var session = RomeSession();
        var state = session.State;
        var city = City(state, CarthagoCityId);

        var lines = InformationPanelModel.City(
            state, session.World, session.Ruleset, city, viewerNationId: RomeId);
        var byKey = ByKey(lines);

        // C09: foreign city → tribute word. Carthago's classical tribute of 287 sits in the
        // 101..10000 "very rich" band.
        var tribute = state.CityById(CarthagoCityId)!.Tribute;
        Assert.Equal(
            $"Tribute: {InformationWords.Tribute(tribute)}",
            byKey[InformationPanelModel.CityTributeKey]);

        // C10: foreign city → Supply line present but blank (not omitted), keeping the field's
        // position in the original's order.
        Assert.True(byKey.ContainsKey(InformationPanelModel.CitySupplyKey));
        Assert.Equal("Supply:", byKey[InformationPanelModel.CitySupplyKey]);

        // C01: capital-marker names the controller (Carthage), not Rome.
        Assert.Equal(city.Name + "  (capital of Carthage)", byKey[InformationPanelModel.CityNameKey]);
    }

    [Fact]
    public void The_active_seat_picks_the_viewer_so_a_Roman_view_of_Carthago_sees_the_tribute_word()
    {
        var session = RomeSession();
        var state = session.State;
        var carthago = City(state, CarthagoCityId);
        var world = session.World;
        var ruleset = session.Ruleset;

        var asForeign = InformationPanelModel.City(state, world, ruleset, carthago, viewerNationId: RomeId);
        var asOwn = InformationPanelModel.City(state, world, ruleset, carthago, viewerNationId: CarthageId);

        // Rome-view: tribute word and a blank Supply line.
        var asForeignKey = ByKey(asForeign);
        Assert.True(asForeignKey.ContainsKey(InformationPanelModel.CitySupplyKey));
        Assert.Equal("Supply:", asForeignKey[InformationPanelModel.CitySupplyKey]);
        Assert.Contains(
            $"Tribute: {InformationWords.Tribute(carthago.Tribute)}",
            asForeignKey[InformationPanelModel.CityTributeKey],
            StringComparison.Ordinal);

        // Carthage-view: tribute in talents and supply in tons (the original's own-city shapes).
        var asOwnKey = ByKey(asOwn);
        var talents = CityTaxContribution.Compute(carthago);
        Assert.Equal(
            $"Tribute: {talents} talents",
            asOwnKey[InformationPanelModel.CityTributeKey]);
        Assert.Equal(
            $"Supply: {carthago.SupplyTons} tons",
            asOwnKey[InformationPanelModel.CitySupplyKey]);
    }

    [Fact]
    public void The_bracket_sums_the_controllers_recruit_slots_at_this_city_under_every_state_code()
    {
        var session = RomeSession();
        var state = session.State;
        var rome = state.NationById(RomeId)!;
        var capua = City(state, CapuaCityId);

        // Arrange Rome's queue with one slot at state 0 and one at state 24 at Capua, plus a
        // third slot at another city that must NOT count toward the Capua bracket.
        var arranged = state with
        {
            Nations = ValueList.From(state.Nations.Select(n =>
                string.Equals(n.Id, RomeId, StringComparison.Ordinal)
                    ? n with
                    {
                        RecruitmentSlots = ValueList.Of(
                            new RecruitmentSlot(TargetCityId: CapuaCityId, UnitTypeId: "heavy_infantry", Troops: 1_000, StateCode: 0),
                            new RecruitmentSlot(TargetCityId: CapuaCityId, UnitTypeId: "light_cavalry", Troops: 2_000, StateCode: 24),
                            new RecruitmentSlot(TargetCityId: "rome", UnitTypeId: "archers", Troops: 5_000, StateCode: 12)),
                    }
                    : n)),
        };

        var fortificationLineRome = ByKey(InformationPanelModel.City(
            arranged, session.World, session.Ruleset, capua, viewerNationId: RomeId))
            [InformationPanelModel.CityFortificationKey];

        var fortificationLineAsForeign = ByKey(InformationPanelModel.City(
            arranged, session.World, session.Ruleset, capua, viewerNationId: CarthageId))
            [InformationPanelModel.CityFortificationKey];

        // Both viewers see the same controller's slots — the bracket is one number, 3,000
        // (1,000 at state 0 plus 2,000 at state 24, the third slot's 5,000 lives at a different
        // city and is excluded).
        Assert.Contains("   (3000)", fortificationLineRome, StringComparison.Ordinal);
        Assert.Contains("   (3000)", fortificationLineAsForeign, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bracket_is_absent_when_no_slot_is_at_the_city_under_any_viewer()
    {
        var session = RomeSession();
        var state = session.State;
        var capua = City(state, CapuaCityId);

        // Starting classical has recruitment slots queued, but the test removes them to verify the
        // "absent" branch.
        var arranged = state with
        {
            Nations = ValueList.From(state.Nations.Select(n =>
                string.Equals(n.Id, RomeId, StringComparison.Ordinal)
                    ? n with { RecruitmentSlots = ValueList<RecruitmentSlot>.Empty }
                    : n)),
        };

        var fortificationLineRome = ByKey(InformationPanelModel.City(
            arranged, session.World, session.Ruleset, capua, viewerNationId: RomeId))
            [InformationPanelModel.CityFortificationKey];

        Assert.DoesNotContain("(", fortificationLineRome.Split("%)")[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Another_nations_slot_at_this_city_does_not_add_to_the_bracket()
    {
        var session = RomeSession();
        var state = session.State;
        var capua = City(state, CapuaCityId);

        // Carthage (not the controller) has a slot at Capua of 7,000 — it must not appear in the
        // bracket Rome's city panel renders.
        var arranged = state with
        {
            Nations = ValueList.From(state.Nations.Select(n =>
                n.Id switch
                {
                    RomeId => n with { RecruitmentSlots = ValueList<RecruitmentSlot>.Empty },
                    CarthageId => n with
                    {
                        RecruitmentSlots = ValueList.Of(
                            new RecruitmentSlot(TargetCityId: CapuaCityId, UnitTypeId: "heavy_infantry", Troops: 7_000, StateCode: 12)),
                    },
                    _ => n,
                })),
        };

        var fortificationLineRome = ByKey(InformationPanelModel.City(
            arranged, session.World, session.Ruleset, capua, viewerNationId: RomeId))
            [InformationPanelModel.CityFortificationKey];

        Assert.DoesNotContain("7000", fortificationLineRome, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(100, "100%")]
    [InlineData(101, "1%  (under construction)")]
    [InlineData(150, "50%  (under construction)")]
    [InlineData(199, "99%  (under construction)")]
    [InlineData(200, "0%  (under construction)")]
    public void Fortification_codes_encode_finished_and_under_construction(int code, string expected)
    {
        var session = RomeSession();
        var state = session.State;
        var city = City(state, RomeCityId).Id;

        var arranged = state with
        {
            Cities = ValueList.From(state.Cities.Select(c =>
                string.Equals(c.Id, city, StringComparison.Ordinal) ? c with { FortificationCode = code } : c)),
        };

        var fortLine = ByKey(InformationPanelModel.City(
            arranged, session.World, session.Ruleset, arranged.CityById(city)!, viewerNationId: RomeId))
            [InformationPanelModel.CityFortificationKey];

        Assert.StartsWith($"Fortification: {expected}", fortLine, StringComparison.Ordinal);
    }

    /// <summary>
    /// R3: a captured capital. Carthage's CapitalCityId still points to Carthago, but Carthago's
    /// controller is now Rome — the (capital of ...) marker must read the controller, not the
    /// pointer-holder (C01: "the nation named is the city's controller (CityState.Owner), not the
    /// nation whose capital it is"). The shipped state has no captured capitals, so the test arranges
    /// one.
    /// </summary>
    [Fact]
    public void A_captured_capital_marks_the_controller_not_the_pointer_holder()
    {
        var session = RomeSession();
        var state = session.State;

        // Rome owns Carthago; Carthage's CapitalCityId still equals Carthago. The marker must say
        // "Carthago (capital of Rome)", not "Carthago (capital of Carthage)".
        var arranged = state with
        {
            Cities = ValueList.From(state.Cities.Select(c =>
                string.Equals(c.Id, CarthagoCityId, StringComparison.Ordinal)
                    ? c with { Owner = RomeId }
                    : c)),
        };
        var carthago = arranged.CityById(CarthagoCityId)!;

        var lines = InformationPanelModel.City(
            arranged, session.World, session.Ruleset, carthago, viewerNationId: RomeId);
        var byKey = ByKey(lines);

        Assert.Equal(
            "Carthago  (capital of Rome)",
            byKey[InformationPanelModel.CityNameKey]);
    }

    // ========================================================================
    // Army panel — own
    // ========================================================================

    [Fact]
    public void Romes_own_army_panel_has_supply_percent_morale_terrain_no_of_units_and_upkeep()
    {
        var session = RomeSession();
        var state = session.State;
        var army = Army(state, RomanArmyId);
        var ruleset = session.Ruleset;

        var lines = InformationPanelModel.OwnArmy(state, session.World, ruleset, army);
        var byKey = ByKey(lines);

        // A02: Moves
        Assert.Equal($"Moves: {army.Moves}", byKey[InformationPanelModel.ArmyMovesKey]);

        // A03: Supply percent through the engine's PercentFull. The research read's row A03 names
        // this formula as matching the original. TotalTroops==0 is impossible for a non-empty army.
        var pct = SupplyCapacity.PercentFull(army.SupplyTons, army.TotalTroops, ruleset);
        Assert.Equal(
            $"Supply: {army.SupplyTons} tons  ({pct}%)",
            byKey[InformationPanelModel.ArmySupplyKey]);

        // A04: Morale word.
        Assert.Equal(
            $"Morale: {InformationWords.Morale(army.Morale)}",
            byKey[InformationPanelModel.ArmyMoraleKey]);

        // A05: Money.
        Assert.Equal($"Money: {army.Money} talents", byKey[InformationPanelModel.ArmyMoneyKey]);

        // A06: Terrain. army-0 may or may not have a covered tile (classical shipped state may
        // stage them); either path is allowed but the line, when present, reads the tile name.
        if (army.CoveredTileCode is { } code)
        {
            var expectedName = session.World.TileTypeByCode(code)?.Name ?? $"code {code}";
            Assert.Equal($"Terrain: {expectedName}", byKey[InformationPanelModel.ArmyTerrainKey]);
        }

        // A07 + A08: five unit-type sums plus Total troops.
        var sums = SumsByType(army);
        foreach (var typeId in InformationPanelModel.UnitTypeIds)
        {
            var key = InformationPanelModel.ArmyUnitTypeKeyPrefix + typeId;
            var sum = sums.TryGetValue(typeId, out var v) ? v : 0;
            Assert.Equal($"{UnitDisplayName(typeId)}: {sum}", byKey[key]);
        }

        Assert.Equal(
            $"Total troops: {army.TotalTroops}",
            byKey[InformationPanelModel.ArmyTotalTroopsKey]);

        // A09: No. of units = the index of the last slot with troops > 0, plus 1.
        Assert.Equal($"No. of units: {LastNonEmptyIndexPlusOne(army)}", byKey[InformationPanelModel.ArmyNoOfUnitsKey]);

        // A10 / A11: upkeep totals.
        var (regulars, mercenaries) = SumUpkeepByKind(army, ruleset);
        Assert.Equal($"Regulars cost: {regulars} talents per quarter", byKey[InformationPanelModel.ArmyRegularsCostKey]);
        Assert.Equal($"Mercenary pay: {mercenaries} talents per quarter", byKey[InformationPanelModel.ArmyMercenaryPayKey]);
    }

    [Fact]
    public void An_army_with_no_covered_tile_omits_the_terrain_line()
    {
        var session = RomeSession();
        var state = session.State;
        var army = Army(state, RomanArmyId);

        // Force no covered tile (the embarked sentinel).
        var arranged = state with
        {
            Armies = ValueList.From(state.Armies.Select(a =>
                string.Equals(a.Id, RomanArmyId, StringComparison.Ordinal)
                    ? a with { CoveredTileCode = null }
                    : a)),
        };
        var embarked = arranged.ArmyById(RomanArmyId)!;

        var lines = InformationPanelModel.OwnArmy(arranged, session.World, session.Ruleset, embarked);
        var byKey = ByKey(lines);

        Assert.False(byKey.ContainsKey(InformationPanelModel.ArmyTerrainKey));

        // The blank line before the unit types stays: it is the research-read A06's
        // "every later line moves up one", documented as a separator rather than a hard absence.
        Assert.Contains(lines, line => line.Key == InformationPanelModel.ArmyBlankKey);
    }

    [Fact]
    public void Upkeep_sums_follow_the_research_read_formula_for_an_army_with_both_kinds()
    {
        var session = RomeSession();
        var state = session.State;
        var ruleset = session.Ruleset;

        // Construct an own army with both regular and mercenary units (built with `with` from the
        // shipped army-0 so we know the unit types are in the ruleset).
        var original = Army(state, RomanArmyId);
        var arranged = state with
        {
            Armies = ValueList.From(state.Armies.Select(a =>
                string.Equals(a.Id, RomanArmyId, StringComparison.Ordinal)
                    ? a with
                    {
                        Units = ValueList.Of(
                            // Regular: 400 heavy_infantry (QuarterlyPrice = 2), so 400 / 200 = 2,
                            // 2 × 2 = 4 talents of regular upkeep.
                            new UnitSlot(MercenaryLabel: 0, UnitTypeId: "heavy_infantry", Troops: 400, Quality: 6, Name: "Hi"),
                            // Mercenary: 1000 light_cavalry (QuarterlyPrice = 3, quality 6), so
                            // (1000 / 200) × 3 = 15, then 15 × 6 / 5 = 18 talents of merc pay.
                            new UnitSlot(MercenaryLabel: 1, UnitTypeId: "light_cavalry", Troops: 1000, Quality: 6, Name: "Lc")),
                    }
                    : a)),
        };
        var army = arranged.ArmyById(RomanArmyId)!;
        var lines = InformationPanelModel.OwnArmy(arranged, session.World, ruleset, army);
        var byKey = ByKey(lines);

        // Engine-verified: 400 troops / 200 × price 2 = 4 talents of regulars; for mercenary
        // (1000/200) = 5, 5 × price 3 = 15, then 15 × quality 6 / 5 = 18. These are the values
        // ArmyUpkeep.ComputeUnit returns today, and the panel reads it.
        var (regulars, mercs) = SumUpkeepByKind(army, ruleset);
        Assert.Equal(4, regulars);
        Assert.Equal(18, mercs);

        Assert.Equal($"Regulars cost: {regulars} talents per quarter", byKey[InformationPanelModel.ArmyRegularsCostKey]);
        Assert.Equal($"Mercenary pay: {mercs} talents per quarter", byKey[InformationPanelModel.ArmyMercenaryPayKey]);
    }

    // ========================================================================
    // Army panel — foreign
    // ========================================================================

    [Fact]
    public void A_Carthaginian_army_viewed_by_Rome_shows_withheld_facts_and_no_upkeep()
    {
        var session = RomeSession();
        var state = session.State;
        var army = Army(state, CarthaginianArmyId);

        var lines = InformationPanelModel.ForeignArmy(state, session.World, session.Ruleset, army);
        var byKey = ByKey(lines);

        // A01: Army of <nation>
        Assert.Equal(
            $"Army of {state.NationById(CarthageId)!.Name}",
            byKey[InformationPanelModel.ArmyOfKey]);

        // A02–A05 (foreign): four "withheld" captions in the original's phrasing.
        Assert.Equal("Moves: withheld", byKey[InformationPanelModel.ArmyMovesKey]);
        Assert.Equal("Supply: withheld", byKey[InformationPanelModel.ArmySupplyKey]);
        Assert.Equal("Morale: withheld", byKey[InformationPanelModel.ArmyMoraleKey]);
        Assert.Equal("Money: withheld", byKey[InformationPanelModel.ArmyMoneyKey]);

        // A06 (Terrain, when covered tile is present): same path the own army uses, but no field
        // reordering — absence is also allowed.
        if (army.CoveredTileCode is { } code)
        {
            var name = session.World.TileTypeByCode(code)?.Name ?? $"code {code}";
            Assert.Equal($"Terrain: {name}", byKey[InformationPanelModel.ArmyTerrainKey]);
        }

        // A07: five unit-type lines, each with its troop sum (0 when none).
        var sums = SumsByType(army);
        foreach (var typeId in InformationPanelModel.UnitTypeIds)
        {
            var sum = sums.TryGetValue(typeId, out var v) ? v : 0;
            var expected = $"{UnitDisplayName(typeId)}: {sum}";
            var key = InformationPanelModel.ArmyUnitTypeKeyPrefix + typeId;
            Assert.Equal(expected, byKey[key]);
        }

        // A08: Total troops (always).
        Assert.Equal(
            $"Total troops: {army.TotalTroops}",
            byKey[InformationPanelModel.ArmyTotalTroopsKey]);

        // The foreign army's panel carries no No. of units, Regulars cost, or Mercenary pay.
        Assert.False(byKey.ContainsKey(InformationPanelModel.ArmyNoOfUnitsKey));
        Assert.False(byKey.ContainsKey(InformationPanelModel.ArmyRegularsCostKey));
        Assert.False(byKey.ContainsKey(InformationPanelModel.ArmyMercenaryPayKey));
    }

    // ========================================================================
    // Fleet panel
    // ========================================================================

    [Fact]
    public void An_own_fleet_panel_lists_moves_repair_supply_percent_capacity_and_sea()
    {
        var session = RomeSession();
        var state = session.State;
        var ruleset = session.Ruleset;

        // The classical start gives Rome no fleet; build one to exercise the own-fleet panel.
        var arranged = state with
        {
            Fleets = ValueList.From(state.Fleets.Append(
                new FleetState(
                    Id: "rome-fleet",
                    Nation: RomeId,
                    X: 100, Y: 37,
                    Moves: 25,
                    Ships: 30,
                    ConditionPercent: 85,
                    Money: 200,
                    SupplyTons: 120,
                    ConstructionTicksRemaining: null,
                    BuildCityId: null,
                    CarriedArmyId: null,
                    CoveredTileCode: 0))),
        };

        var fleet = arranged.FleetById("rome-fleet")!;
        var lines = InformationPanelModel.OwnFleet(arranged, session.World, ruleset, fleet);
        var byKey = ByKey(lines);

        Assert.Equal($"Fleet of {state.NationById(RomeId)!.Name}", byKey[InformationPanelModel.FleetOfKey]);
        Assert.Equal($"Moves: {fleet.Moves}", byKey[InformationPanelModel.FleetMovesKey]);
        Assert.Equal($"Ships: {fleet.Ships}", byKey[InformationPanelModel.FleetShipsKey]);
        Assert.Equal($"Repair: {fleet.ConditionPercent}%", byKey[InformationPanelModel.FleetRepairKey]);

        // F05: Supply percent = supplyTons × 100 / (ships × FleetSupplyTonsPerShip). The ruleset's
        // 8-ton figure is read through SupplyCapacity.FleetCapacityTons.
        var capacity = SupplyCapacity.FleetCapacityTons(fleet.Ships, ruleset);
        var pct = capacity == 0 ? 0 : fleet.SupplyTons * 100 / capacity;
        Assert.Equal(
            $"Supply: {fleet.SupplyTons} tons  ({pct}%)",
            byKey[InformationPanelModel.FleetSupplyKey]);

        Assert.Equal($"Money: {fleet.Money} talents", byKey[InformationPanelModel.FleetMoneyKey]);

        // F07: Capacity = ships × TransportTroopsPerShip (ruleset data, never a literal).
        var transport = ruleset.Naval.TransportTroopsPerShip;
        Assert.Equal(
            $"Capacity: {fleet.Ships * transport} troops",
            byKey[InformationPanelModel.FleetCapacityKey]);

        // F08: Sea — calm for code 0, rough for anything else.
        Assert.Equal("Sea: calm", byKey[InformationPanelModel.FleetSeaKey]);
    }

    [Fact]
    public void An_own_fleet_panel_says_rough_sea_for_a_non_zero_tile_code()
    {
        var session = RomeSession();
        var state = session.State;
        var ruleset = session.Ruleset;
        var arranged = state with
        {
            Fleets = ValueList.From(state.Fleets.Append(
                new FleetState(
                    Id: "rome-fleet-rough",
                    Nation: RomeId,
                    X: 100, Y: 37,
                    Moves: 25, Ships: 30, ConditionPercent: 85, Money: 200, SupplyTons: 120,
                    ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: null,
                    CoveredTileCode: 1))),
        };

        var fleet = arranged.FleetById("rome-fleet-rough")!;
        var lines = InformationPanelModel.OwnFleet(arranged, session.World, ruleset, fleet);
        var byKey = ByKey(lines);

        Assert.Equal("Sea: rough", byKey[InformationPanelModel.FleetSeaKey]);
    }

    [Fact]
    public void A_foreign_fleet_panel_carries_only_fleet_of_ships_capacity_and_sea()
    {
        var session = RomeSession();
        var state = session.State;
        var fleet = Fleet(state, CarthaginianFleetId);

        var lines = InformationPanelModel.ForeignFleet(state, session.World, session.Ruleset, fleet);
        var byKey = ByKey(lines);

        // Always-shown lines.
        Assert.Equal($"Fleet of {state.NationById(CarthageId)!.Name}", byKey[InformationPanelModel.FleetOfKey]);
        Assert.Equal($"Ships: {fleet.Ships}", byKey[InformationPanelModel.FleetShipsKey]);
        var transport = session.Ruleset.Naval.TransportTroopsPerShip;
        Assert.Equal(
            $"Capacity: {fleet.Ships * transport} troops",
            byKey[InformationPanelModel.FleetCapacityKey]);
        var expectedSea = $"Sea: {InformationWords.Sea(fleet.CoveredTileCode ?? 0)}";
        Assert.Equal(expectedSea, byKey[InformationPanelModel.FleetSeaKey]);

        // Withheld: foreign-fleet captions for Moves, Repair, Supply, Money.
        Assert.Equal("Moves:", byKey[InformationPanelModel.FleetMovesKey]);
        Assert.Equal("Repair:", byKey[InformationPanelModel.FleetRepairKey]);
        Assert.Equal("Supply:", byKey[InformationPanelModel.FleetSupplyKey]);
        Assert.Equal("Money:", byKey[InformationPanelModel.FleetMoneyKey]);
    }

    /// <summary>
    /// R5: a foreign fleet uses the same field order as an own fleet — Moves, Ships, Repair, Supply,
    /// Money, Capacity, Sea — with the foreign-only values (Moves, Repair, Supply, Money) blank.
    /// The own-fleet case is asserted position-by-position below; the foreign case asserts the same
    /// shape on its own.
    /// </summary>
    [Fact]
    public void An_own_fleet_and_a_foreign_fleet_share_the_same_field_order()
    {
        var session = RomeSession();
        var state = session.State;
        var ruleset = session.Ruleset;

        // The classical start gives Rome no fleet; build one to exercise the own-fleet panel.
        var arranged = state with
        {
            Fleets = ValueList.From(state.Fleets.Append(
                new FleetState(
                    Id: "rome-fleet-order",
                    Nation: RomeId,
                    X: 100, Y: 37,
                    Moves: 25,
                    Ships: 30,
                    ConditionPercent: 85,
                    Money: 200,
                    SupplyTons: 120,
                    ConstructionTicksRemaining: null,
                    BuildCityId: null,
                    CarriedArmyId: null,
                    CoveredTileCode: 0))),
        };

        var ownLines = InformationPanelModel.OwnFleet(
            arranged, session.World, ruleset, arranged.FleetById("rome-fleet-order")!);
        var foreignLines = InformationPanelModel.ForeignFleet(
            arranged, session.World, ruleset, Fleet(state, CarthaginianFleetId));

        // The order of the named keys (F02..F08) on each panel.
        var ownOrder = FieldOrder(ownLines, InformationPanelModel.FleetOfKey, InformationPanelModel.FleetArmyHeaderKey);
        var foreignOrder = FieldOrder(foreignLines, InformationPanelModel.FleetOfKey, InformationPanelModel.FleetArmyHeaderKey);

        var expected = new[]
        {
            InformationPanelModel.FleetMovesKey,
            InformationPanelModel.FleetShipsKey,
            InformationPanelModel.FleetRepairKey,
            InformationPanelModel.FleetSupplyKey,
            InformationPanelModel.FleetMoneyKey,
            InformationPanelModel.FleetCapacityKey,
            InformationPanelModel.FleetSeaKey,
        };

        Assert.Equal(expected, ownOrder);
        Assert.Equal(expected, foreignOrder);
    }

    /// <summary>The keys in <paramref name="lines"/> that fall between <paramref name="startKey"/>
    /// and <paramref name="stopKey"/> (exclusive), in the order they appear.</summary>
    private static IReadOnlyList<string> FieldOrder(
        IReadOnlyList<InformationPanelLine> lines, string? startKey, string? stopKey)
    {
        var started = startKey is null;
        var result = new List<string>();
        foreach (var line in lines)
        {
            if (!started && string.Equals(line.Key, startKey, StringComparison.Ordinal))
            {
                started = true;
                continue;
            }

            if (started)
            {
                if (stopKey is not null && string.Equals(line.Key, stopKey, StringComparison.Ordinal))
                {
                    break;
                }

                if (line.Key.Length > 0
                    && !string.Equals(line.Key, InformationPanelModel.ArmyBlankKey, StringComparison.Ordinal))
                {
                    result.Add(line.Key);
                }
            }
        }

        return result;
    }

    [Fact]
    public void A_fleet_carrying_an_army_lists_the_army_block_after_a_blank_line()
    {
        var session = RomeSession();
        var state = session.State;
        var army = Army(state, RomanArmyId);
        var ruleset = session.Ruleset;

        // Build an own fleet (Rome) that carries Rome's army-0. With Rome as the viewer the
        // embarked army is the own-army panel from Supply on.
        var arranged = state with
        {
            Fleets = ValueList.From(state.Fleets.Append(
                new FleetState(
                    Id: "rome-fleet-army",
                    Nation: RomeId,
                    X: 100, Y: 37,
                    Moves: 25, Ships: 30, ConditionPercent: 85, Money: 200, SupplyTons: 120,
                    ConstructionTicksRemaining: null, BuildCityId: null,
                    CarriedArmyId: RomanArmyId, CoveredTileCode: 0))),
            Armies = ValueList.From(state.Armies.Select(a =>
                string.Equals(a.Id, RomanArmyId, StringComparison.Ordinal)
                    ? a with { AboardFleetId = "rome-fleet-army", CoveredTileCode = null }
                    : a)),
        };

        var fleet = arranged.FleetById("rome-fleet-army")!;
        var lines = InformationPanelModel.OwnFleet(arranged, session.World, ruleset, fleet);

        // The "Army" heading appears, then the army's own-lines-from-Supply-on, with a blank
        // spacer between.
        var hasArmyHeader = lines.Any(line =>
            string.Equals(line.Key, InformationPanelModel.FleetArmyHeaderKey, StringComparison.Ordinal)
            && line.Text == "Army");
        Assert.True(hasArmyHeader, "the fleet panel emits an 'Army' header for the embarked army");

        var headerIndex = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (string.Equals(lines[i].Key, InformationPanelModel.FleetArmyHeaderKey, StringComparison.Ordinal))
            {
                headerIndex = i;
                break;
            }
        }

        Assert.True(headerIndex >= 0, "the 'Army' header exists in the rendered lines");
        var slice = lines.Skip(headerIndex + 1).ToList();
        Assert.Contains(slice, line => line.Key == InformationPanelModel.ArmySupplyKey);
        Assert.Contains(slice, line => line.Key == InformationPanelModel.ArmyMoraleKey);
        Assert.Contains(slice, line => line.Key == InformationPanelModel.ArmyMoneyKey);
        Assert.Contains(slice, line => line.Key == InformationPanelModel.ArmyTotalTroopsKey);
        Assert.Contains(slice, line => line.Key == InformationPanelModel.ArmyNoOfUnitsKey);
        Assert.Contains(slice, line => line.Key == InformationPanelModel.ArmyRegularsCostKey);
        Assert.Contains(slice, line => line.Key == InformationPanelModel.ArmyMercenaryPayKey);

        // No Terrain line in the embarked-army block: the embarked sentinel nulls CoveredTileCode.
        Assert.DoesNotContain(slice, line => line.Key == InformationPanelModel.ArmyTerrainKey);
    }

    /// <summary>
    /// R2 of the R1 review: a Carthaginian fleet carrying a Carthaginian army, viewed by Rome. The
    /// active seat (Rome) is the viewer, not the fleet's owner, so the embarked army is a foreign
    /// army in the panel's eyes: Supply/Morale/Money are "withheld" captions, and there is no
    /// No. of units / Regulars cost / Mercenary pay line. A regression that classifies against
    /// the fleet's owner would render the full own-army block and expose Carthaginian facts to
    /// Rome.
    /// </summary>
    [Fact]
    public void A_foreign_fleet_carrying_its_own_army_withholds_the_embarked_armys_facts()
    {
        var session = RomeSession();
        var state = session.State;
        var army = Army(state, CarthaginianArmyId);
        var ruleset = session.Ruleset;

        // Build a Carthaginian fleet carrying Carthage's army-2, with Rome still the active seat
        // (the RomeSession's seed places Rome as the human seat). The embarked army is foreign
        // to Rome.
        var arranged = state with
        {
            Fleets = ValueList.From(state.Fleets.Append(
                new FleetState(
                    Id: "carthage-fleet-army",
                    Nation: CarthageId,
                    X: 100, Y: 37,
                    Moves: 25, Ships: 30, ConditionPercent: 85, Money: 200, SupplyTons: 120,
                    ConstructionTicksRemaining: null, BuildCityId: null,
                    CarriedArmyId: CarthaginianArmyId, CoveredTileCode: 0))),
            Armies = ValueList.From(state.Armies.Select(a =>
                string.Equals(a.Id, CarthaginianArmyId, StringComparison.Ordinal)
                    ? a with { AboardFleetId = "carthage-fleet-army", CoveredTileCode = null }
                    : a)),
        };

        var fleet = arranged.FleetById("carthage-fleet-army")!;
        var lines = InformationPanelModel.ForeignFleet(arranged, session.World, ruleset, fleet);

        // The "Army" header exists in the lines.
        var headerIndex = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (string.Equals(lines[i].Key, InformationPanelModel.FleetArmyHeaderKey, StringComparison.Ordinal))
            {
                headerIndex = i;
                break;
            }
        }

        Assert.True(headerIndex >= 0, "the foreign fleet panel emits an 'Army' header for the embarked army");
        var slice = lines.Skip(headerIndex + 1).ToList();

        // The embarked army is foreign to Rome (the active seat): Supply, Morale, Money are
        // "withheld" captions.
        var supply = slice.Single(line => line.Key == InformationPanelModel.ArmySupplyKey);
        var morale = slice.Single(line => line.Key == InformationPanelModel.ArmyMoraleKey);
        var money = slice.Single(line => line.Key == InformationPanelModel.ArmyMoneyKey);
        Assert.Equal("Supply: withheld", supply.Text);
        Assert.Equal("Morale: withheld", morale.Text);
        Assert.Equal("Money: withheld", money.Text);

        // No No. of units, Regulars cost, Mercenary pay — those are own-only.
        Assert.DoesNotContain(slice, line => line.Key == InformationPanelModel.ArmyNoOfUnitsKey);
        Assert.DoesNotContain(slice, line => line.Key == InformationPanelModel.ArmyRegularsCostKey);
        Assert.DoesNotContain(slice, line => line.Key == InformationPanelModel.ArmyMercenaryPayKey);
    }

    // ========================================================================
    // Helpers — duplicated here only when the engine's own helper does not match.
    // ========================================================================

    private static IReadOnlyDictionary<string, int> SumsByType(ArmyState army)
    {
        var sums = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var slot in army.Units)
        {
            sums.TryGetValue(slot.UnitTypeId, out var current);
            sums[slot.UnitTypeId] = current + slot.Troops;
        }

        return sums;
    }

    private static string UnitDisplayName(string typeId) => typeId switch
    {
        "light_infantry" => "Light infantry",
        "heavy_infantry" => "Heavy infantry",
        "archers" => "Archers",
        "light_cavalry" => "Light cavalry",
        "heavy_cavalry" => "Heavy cavalry",
        _ => typeId,
    };

    private static int LastNonEmptyIndexPlusOne(ArmyState army)
    {
        for (var i = army.Units.Count - 1; i >= 0; i--)
        {
            if (army.Units[i].Troops > 0)
            {
                return i + 1;
            }
        }

        return 0;
    }

    private static (int Regulars, int Mercenaries) SumUpkeepByKind(ArmyState army, Ruleset ruleset)
    {
        var regulars = 0;
        var mercenaries = 0;
        foreach (var unit in army.Units)
        {
            var cost = ArmyUpkeep.ComputeUnit(unit, ruleset);
            if (unit.IsMercenary)
            {
                mercenaries += cost;
            }
            else
            {
                regulars += cost;
            }
        }

        return (regulars, mercenaries);
    }
}
