using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Model;

/// <summary>
/// The task entry's Scope line: the model must carry, from day one, every field later tasks need so
/// they do not have to widen it. One test per named field, so a later removal is caught here rather
/// than in whichever task discovers it missing.
/// </summary>
public class ModelCoverageTests
{
    [Fact]
    public void Armies_and_fleets_each_carry_their_own_money_purse_and_supply_stock()
    {
        var state = ToyFixtures.NonTrivialState();
        var ruleset = ToyFixtures.Toy.Ruleset;

        var army = state.ArmyById("north-army-1")!;
        var fleet = state.FleetById("south-fleet-1")!;

        Assert.Equal(ruleset.Economy.PurseCapPerUnit, army.Money);
        Assert.True(army.SupplyTons > 0);
        Assert.True(fleet.Money > 0);
        Assert.True(fleet.SupplyTons > 0);

        // The cap is ruleset data, and the supply capacities are formulas later tasks own; the model
        // only has to carry the fields those formulas read.
        Assert.Equal(1000, ruleset.Economy.PurseCapPerUnit);
        Assert.Equal(8, ruleset.Economy.FleetSupplyTonsPerShip);
        Assert.Equal(100, ruleset.Economy.ArmySupplyTonsPerTroops);
    }

    [Fact]
    public void Army_morale_is_the_strategic_plus_14_value_and_is_not_the_tactical_one()
    {
        var world = ToyFixtures.Toy.World;

        var army = world.StartingArmies.FindById(a => a.Id, "north-army-1")!;

        // 68 is the morale read out of 1_rome_270_winter_7.sav for both Roman armies; a freshly split
        // army starts at 59. Both are army record +14 values, not per-unit tactical morale, which the
        // model deliberately does not persist (docs/design-audit.md §2.9).
        Assert.Equal(68, army.Morale);
        Assert.Equal(59, world.StartingArmies.FindById(a => a.Id, "south-army-1")!.Morale);
        Assert.Equal(59, ToyFixtures.Toy.Ruleset.ArmyManagement.NewArmyMorale);
    }

    [Fact]
    public void A_unit_slot_carries_the_regular_versus_mercenary_marker()
    {
        var army = ToyFixtures.Toy.World.StartingArmies.FindById(a => a.Id, "north-army-1")!;

        var regular = army.Units[0];
        var mercenary = army.Units[1];

        Assert.True(regular.IsRegular);
        Assert.False(regular.IsMercenary);
        Assert.Equal(0, regular.MercenaryLabel);

        Assert.True(mercenary.IsMercenary);
        Assert.Equal(11, mercenary.MercenaryLabel); // the confirmed name-table index for "Gallic"
    }

    [Fact]
    public void City_fortification_carries_the_dual_encoding_including_an_order_in_progress()
    {
        var world = ToyFixtures.Toy.World;
        var fortify = ToyFixtures.Toy.Ruleset.CityOrders.Orders.FindById(o => o.Id, "fortify")!;

        var finished = world.CityById("arx")!;
        var inProgress = world.CityById("portus")!;
        var complete = world.CityById("meridia")!;

        Assert.False(FortificationCode.IsOrderInProgress(finished.FortificationCode, fortify));
        Assert.Equal(60, FortificationCode.FinishedPercent(finished.FortificationCode, fortify));
        Assert.Equal(0, FortificationCode.PendingPoints(finished.FortificationCode, fortify));

        Assert.True(FortificationCode.IsOrderInProgress(inProgress.FortificationCode, fortify));
        Assert.Equal(40, FortificationCode.FinishedPercent(inProgress.FortificationCode, fortify));
        Assert.Equal(3, FortificationCode.PendingPoints(inProgress.FortificationCode, fortify));

        // A value of exactly the maximum is finished, not in progress - the boundary the "% radix"
        // shortcut gets wrong if it is applied unconditionally.
        Assert.False(FortificationCode.IsOrderInProgress(complete.FortificationCode, fortify));
        Assert.Equal(100, FortificationCode.FinishedPercent(complete.FortificationCode, fortify));
        Assert.Equal(0, FortificationCode.MaxOrderablePoints(complete.FortificationCode, fortify));
    }

    [Fact]
    public void Placing_and_wiping_a_fortification_order_follows_the_confirmed_encoding()
    {
        var fortify = ToyFixtures.Toy.Ruleset.CityOrders.Orders.FindById(o => o.Id, "fortify")!;

        var ordered = FortificationCode.WithOrder(40, points: 3, fortify);

        Assert.Equal(340, ordered);
        Assert.Equal(40, FortificationCode.AfterSiegeAttempt(ordered, fortify));
        Assert.Equal(60, FortificationCode.MaxOrderablePoints(ordered, fortify));
        Assert.True(fortify.WipedBySiegeAttempt);
        Assert.True(fortify.RefusedWhileUnderSiege);
    }

    [Fact]
    public void Fleets_carry_a_condition_percentage_and_a_construction_countdown()
    {
        var state = ToyFixtures.NonTrivialState();

        var launched = state.FleetById("north-fleet-1")!;
        var building = state.FleetById("north-fleet-2")!;

        Assert.False(launched.IsUnderConstruction);
        Assert.Equal(100, launched.ConditionPercent);

        Assert.True(building.IsUnderConstruction);
        Assert.Equal(12, building.ConstructionTicksRemaining);
        Assert.Equal("portus", building.BuildCityId);
    }

    [Fact]
    public void The_relation_matrix_is_symmetric_by_construction_and_holds_negative_cooldowns()
    {
        var ruleset = ToyFixtures.Toy.Ruleset;
        var relations = DiplomaticRelations.Uniform(
            ValueList.Of("north", "south", "east"),
            ruleset.Diplomacy.StateCodes.Peace);

        var atWar = relations.WithRelation("north", "south", ruleset.Diplomacy.StateCodes.War);
        Assert.Equal(ruleset.Diplomacy.StateCodes.War, atWar.Get("north", "south"));
        Assert.Equal(ruleset.Diplomacy.StateCodes.War, atWar.Get("south", "north"));
        Assert.True(atWar.IsWellFormed());

        var cooled = atWar.WithRelation("north", "south", ruleset.Diplomacy.CooldownAfterEndedWar);
        Assert.Equal(-18, cooled.Get("north", "south"));
        Assert.Equal(-18, cooled.Get("south", "north"));
        Assert.True(cooled.Get("north", "south") < 0);
        Assert.True(cooled.IsWellFormed());

        // Untouched pairs are unaffected.
        Assert.Equal(ruleset.Diplomacy.StateCodes.Peace, cooled.Get("north", "east"));
    }

    [Fact]
    public void The_relation_matrix_sizes_itself_to_the_world_rather_than_to_sixteen()
    {
        var state = ToyFixtures.NonTrivialState();

        Assert.Equal(2, state.Relations.NationIds.Count);
        Assert.Equal(2, state.Relations.Matrix.Count);
        Assert.Equal(2, state.Relations.Matrix[0].Count);
    }

    [Fact]
    public void The_news_log_is_a_ring_buffer_whose_capacity_comes_from_the_ruleset()
    {
        var rules = ToyFixtures.Toy.Ruleset.NewsLog;
        Assert.Equal(40, rules.RingBufferSlots);
        Assert.Equal(61, rules.MessageByteLength);

        var log = NewsLog.Empty;
        Assert.Equal(-1, log.MostRecentSlot);

        for (var i = 0; i < rules.RingBufferSlots; i++)
        {
            log = log.Append(new NewsEntry($"message {i}"), rules);
        }

        Assert.Equal(rules.RingBufferSlots, log.Slots.Count);
        Assert.Equal(rules.RingBufferSlots - 1, log.MostRecentSlot);
        Assert.Equal("message 0", log.Slots[0].Text);

        // One more drops the oldest, exactly as FUN_00449240 does once the log is full.
        log = log.Append(new NewsEntry("overflow"), rules);
        Assert.Equal(rules.RingBufferSlots, log.Slots.Count);
        Assert.Equal(rules.RingBufferSlots - 1, log.MostRecentSlot);
        Assert.Equal("message 1", log.Slots[0].Text);
        Assert.Equal("overflow", log.Slots[^1].Text);
    }

    [Fact]
    public void Terrain_tile_types_are_an_open_list_not_a_fixed_twelve()
    {
        var world = ToyFixtures.Toy.World;
        var ruleset = ToyFixtures.Toy.Ruleset;

        Assert.Equal(12, world.TileTypes.Count);
        Assert.Equal(12, ruleset.Terrain.MoveCosts.Count);
        Assert.Equal(1, ruleset.MoveCostFor("plain"));
        Assert.Equal(2, ruleset.MoveCostFor("forest"));
        Assert.Equal(4, ruleset.MoveCostFor("mountains"));
        Assert.Equal(4, ruleset.MoveCostFor("river_1"));
        Assert.Equal(1, ruleset.MoveCostFor("sea_coastal"));
        Assert.Equal(3, ruleset.MoveCostFor("sea_deep"));

        // A thirteenth type is loadable without any engine change, and a ruleset that does not price it
        // falls back to the documented default rather than refusing the world.
        var extended = world with
        {
            TileTypes = ValueList.From(world.TileTypes.Append(
                new TileType("swamp", 12, "Swamp", PassableByArmies: true, PassableByFleets: false))),
        };
        var reloaded = GameDataLoader.Load<World>("extended.json", GameJson.Serialize(extended));

        Assert.Equal(13, reloaded.TileTypes.Count);
        Assert.Equal("Swamp", reloaded.TileTypeByCode(12)!.Name);
        Assert.Equal(ruleset.Terrain.DefaultMoveCost, ruleset.MoveCostFor("swamp"));
    }

    [Fact]
    public void The_terrain_grid_decodes_to_the_map_size_and_both_encodings_agree()
    {
        var world = ToyFixtures.Toy.World;

        var fromRuns = world.Terrain.Decode(world.Width, world.Height);
        Assert.Equal(world.Width * world.Height, fromRuns.Length);

        var bytes = new byte[fromRuns.Length * 2];
        for (var i = 0; i < fromRuns.Length; i++)
        {
            bytes[i * 2] = (byte)(fromRuns[i] & 0xFF);
            bytes[(i * 2) + 1] = (byte)((fromRuns[i] >> 8) & 0xFF);
        }

        var asBase64 = world with
        {
            Terrain = new TerrainGrid(TerrainEncoding.Base64, Runs: null, Data: Convert.ToBase64String(bytes)),
        };
        var reloaded = GameDataLoader.Load<World>("base64.json", GameJson.Serialize(asBase64));

        Assert.Equal(fromRuns, reloaded.Terrain.Decode(world.Width, world.Height));
    }

    [Fact]
    public void The_ruleset_flags_select_formula_variants_by_name()
    {
        var flags = ToyFixtures.Toy.Ruleset.Flags;

        Assert.Equal(DiplomacyModel.ConfirmedStateMachine, flags.DiplomacyModel);
        Assert.Equal(EconomyPurseModel.PerUnitPurses, flags.EconomyPurses);
        Assert.Equal(SeatAsymmetryModel.Faithful, flags.SeatAsymmetry);
        Assert.Equal(DiplomaticThawPolicy.ReproduceEightColumnBug, flags.BugPolicyDiplomaticThaw);
        Assert.Equal(DefeatOutcome.Destroyed, flags.CombatOnDefeat);
    }

    [Fact]
    public void Mercenary_pool_slots_are_carried_with_their_slot_index()
    {
        var state = ToyFixtures.NonTrivialState();

        Assert.Equal(50, ToyFixtures.Toy.Ruleset.Recruitment.MercenaryPoolSlots);
        Assert.Equal(2, state.MercenaryPool.Count);
        Assert.Equal(0, state.MercenaryPool[0].SlotIndex);
        Assert.Equal(7, state.MercenaryPool[1].SlotIndex);
        Assert.Equal(6438, state.MercenaryPool[0].Troops);
    }

    [Fact]
    public void The_turn_order_table_is_world_data_sized_to_the_worlds_nations()
    {
        var world = ToyFixtures.Toy.World;
        var state = ToyFixtures.NonTrivialState();

        Assert.Equal(world.Nations.Count, world.TurnOrder.Count);
        Assert.Equal(world.TurnOrder, state.TurnOrder);
        Assert.Equal("south", state.ActiveNationId);
    }
}
