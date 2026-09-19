using IC2.Engine.Armies;
using IC2.Engine.Armies.Commands;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// T55 Done-when 3's other half — <c>FUN_00449f08</c>, the army a mobilized recruit gets when no
/// existing one will take it (<c>decompiled-mobilization-and-mercenary-restock.md</c> §3).
/// </summary>
/// <remarks>
/// The toy world is the fixture: <c>arx</c> stands at <c>(2, 1)</c>, its north-west neighbour
/// <c>(1, 0)</c> is sea, and everything else in its 3×3 block is land
/// (<c>data/worlds/toy-3city.json</c>).
/// </remarks>
public sealed class MobilizationArmyCreationTests
{
    private static Ruleset Ruleset => ArmiesTestbed.Ruleset;

    private static World World => CoreTestbed.Toy.World;

    /// <summary>The toy state with no armies on the map at all, so occupancy never masks a cell.</summary>
    private static GameState EmptyMap() => ArmiesTestbed.WithArmies(ArmiesTestbed.InitialState());

    /// <summary>
    /// The placement scan takes the <em>last</em> qualifying cell of the 3×3 block, which for a city
    /// with open ground all round is its south-east neighbour — the rule behind the corpus pair's
    /// <c>(102, 44) = Rome (101, 43) + (+1, +1)</c>.
    /// </summary>
    [Fact]
    public void The_placement_cell_is_the_last_of_the_scan_which_is_the_south_east_neighbour()
    {
        var state = EmptyMap();
        var arx = state.CityById("arx")!;

        var cell = MobilizationArmyCreation.PlacementCell(state, World, arx);

        Assert.Equal(new GridPoint(arx.X + 1, arx.Y + 1), cell);
    }

    /// <summary>
    /// An occupied south-east neighbour drops out of the scan and the cell before it wins — the last
    /// <em>qualifying</em> cell, not the last cell. Driven three ways: an army standing there, the
    /// same army embarked (which covers no map cell and so does not block), and a city there.
    /// </summary>
    [Fact]
    public void An_occupied_cell_does_not_qualify_and_the_scan_falls_back_to_the_one_before_it()
    {
        var state = EmptyMap();
        var arx = state.CityById("arx")!;
        var southEast = new GridPoint(arx.X + 1, arx.Y + 1);
        var previous = new GridPoint(arx.X, arx.Y + 1);

        var blockingArmy = ArmiesTestbed.Army(
            "blocker", ArmiesTestbed.NorthNationId, southEast.X, southEast.Y,
            new[] { ArmiesTestbed.RegularUnit("1st Foot Battalion") });

        Assert.Equal(
            previous,
            MobilizationArmyCreation.PlacementCell(ArmiesTestbed.WithArmies(state, blockingArmy), World, arx));

        // An embarked army covers no map cell, so it does not block -- the same convention
        // MobilizationReceivingArmy uses to skip it as a candidate.
        var embarked = blockingArmy with { AboardFleetId = "some-fleet", CoveredTileCode = null };
        Assert.Equal(
            southEast,
            MobilizationArmyCreation.PlacementCell(ArmiesTestbed.WithArmies(state, embarked), World, arx));

        var blockingCity = state.CityById("meridia")! with { X = southEast.X, Y = southEast.Y };
        Assert.Equal(
            previous,
            MobilizationArmyCreation.PlacementCell(ArmiesTestbed.WithCity(state, blockingCity), World, arx));
    }

    /// <summary>
    /// Sea never qualifies, so a city with water on every side gets no cell and therefore no army —
    /// the branch behind <c>MobilizeRecruitSlotRejections.NoReceivingArmy</c>. No shipped city is
    /// marooned like that, so the fixture moves one onto the toy map's all-water bottom row.
    /// </summary>
    [Fact]
    public void A_city_with_no_land_neighbour_gets_no_cell_and_therefore_no_army()
    {
        var state = EmptyMap();

        // (7, 5) is deep sea with sea on every side it has (rivers-and-map-markers convention aside,
        // the toy map's bottom row is all water) -- no cell in the block is passable for an army.
        var marooned = state.CityById("arx")! with { X = 7, Y = 5 };
        state = ArmiesTestbed.WithCity(state, marooned);

        Assert.Null(MobilizationArmyCreation.PlacementCell(state, World, marooned));
        Assert.Null(MobilizationArmyCreation.Create(
            state, World, marooned, state.NationById(ArmiesTestbed.NorthNationId)!, Ruleset, "new-army"));
    }

    /// <summary>
    /// The created army's stats: 0 supplies, 0 money, morale 59, and the covered tile code read from
    /// the terrain it stands on — the corpus pair's army 14 had <c>covered = 2</c>, a land code.
    /// </summary>
    [Fact]
    public void A_created_army_starts_empty_with_no_money_no_supply_and_the_rulesets_new_army_morale()
    {
        var state = EmptyMap();
        var arx = state.CityById("arx")!;
        var nation = state.NationById(ArmiesTestbed.NorthNationId)!;

        var created = MobilizationArmyCreation.Create(state, World, arx, nation, Ruleset, "army-14")!;

        Assert.Equal("army-14", created.Id);
        Assert.Equal(nation.Id, created.Nation);
        Assert.Equal(arx.X + 1, created.X);
        Assert.Equal(arx.Y + 1, created.Y);
        Assert.Equal(0, created.Money);
        Assert.Equal(0, created.SupplyTons);
        Assert.Equal(Ruleset.ArmyManagement.NewArmyMorale, created.Morale);
        Assert.Equal(59, Ruleset.ArmyManagement.NewArmyMorale);
        Assert.Empty(created.Units);
        Assert.Null(created.AboardFleetId);

        // The covered code is the terrain under it, and it is one of the codes an army may stand on.
        var tile = World.TileTypeByCode(created.CoveredTileCode!.Value);
        Assert.NotNull(tile);
        Assert.True(tile!.PassableByArmies);
    }

    /// <summary>
    /// The seat asymmetry on moves: a player's freshly mobilized army cannot act in the week it
    /// appears, an AI's can move one tile. Under <c>improved</c> every seat gets the AI's value, the
    /// same generalisation <see cref="SplitArmyCommandHandler"/> makes.
    /// </summary>
    [Fact]
    public void A_created_army_has_no_moves_for_a_human_seat_and_one_for_an_ai_seat()
    {
        var state = EmptyMap();
        var arx = state.CityById("arx")!;
        var human = state.NationById(ArmiesTestbed.NorthNationId)!;
        var ai = human with { Control = SeatControl.Ai };

        Assert.Equal(
            Ruleset.ArmyManagement.NewArmyMovesHumanSeat,
            MobilizationArmyCreation.Create(state, World, arx, human, Ruleset, "a")!.Moves);
        Assert.Equal(
            Ruleset.ArmyManagement.NewArmyMovesAiSeat,
            MobilizationArmyCreation.Create(state, World, arx, ai, Ruleset, "a")!.Moves);
        Assert.Equal(0, Ruleset.ArmyManagement.NewArmyMovesHumanSeat);
        Assert.Equal(1, Ruleset.ArmyManagement.NewArmyMovesAiSeat);

        var normalized = Ruleset with { Flags = Ruleset.Flags with { SeatAsymmetry = SeatAsymmetryModel.Normalized } };
        Assert.Equal(
            Ruleset.ArmyManagement.NewArmyMovesAiSeat,
            MobilizationArmyCreation.Create(state, World, arx, human, normalized, "a")!.Moves);
    }

    /// <summary>
    /// The hazard "do not re-derive the army-creation stats", made checkable: a mobilized army and a
    /// split army — T15's already-merged path, run here through its own command — are created with the
    /// same morale and the same moves, because both read the same two rules.
    /// </summary>
    [Fact]
    public void A_mobilized_army_and_a_split_army_are_created_with_the_same_stats()
    {
        var state = ArmiesTestbed.InitialState();
        var nation = state.NationById(ArmiesTestbed.NorthNationId)!;
        var parent = ArmiesTestbed.Army(
            "parent", nation.Id, 5, 3,
            new[] { ArmiesTestbed.RegularUnit("1st Foot Battalion"), ArmiesTestbed.RegularUnit("2nd Foot Battalion") });
        state = ArmiesTestbed.WithArmies(state, parent);

        var split = ArmiesTestbed.Dispatcher().Dispatch(
            state, new SplitArmyCommand(nation.Id, "parent", "split-army", ValueList.Of(1)));
        Assert.True(split.IsAccepted);
        var splitArmy = split.State.ArmyById("split-army")!;

        var mobilized = MobilizationArmyCreation.Create(
            state, World, state.CityById("arx")!, nation, Ruleset, "mobilized-army")!;

        Assert.Equal(splitArmy.Morale, mobilized.Morale);
        Assert.Equal(splitArmy.Moves, mobilized.Moves);
        Assert.Equal(splitArmy.Money, mobilized.Money);
        Assert.Equal(splitArmy.SupplyTons, mobilized.SupplyTons);
    }

    /// <summary>
    /// The 198-army cap: at the cap no army is created, so the recruit has nowhere to go and the
    /// command refuses rather than silently growing the table.
    /// </summary>
    [Fact]
    public void No_army_is_created_once_the_army_table_is_at_its_cap()
    {
        var state = EmptyMap();
        var arx = state.CityById("arx")!;
        var nation = state.NationById(ArmiesTestbed.NorthNationId)!;
        var cap = Ruleset.ArmyManagement.MaxArmies;

        // Armies parked far from arx so only the count, never the placement, can be the reason.
        var crowd = Enumerable.Range(0, cap)
            .Select(i => ArmiesTestbed.Army($"filler-{i}", nation.Id, 0, 5, Array.Empty<UnitSlot>()))
            .ToArray();

        var atCap = ArmiesTestbed.WithArmies(state, crowd);
        Assert.Null(MobilizationArmyCreation.Create(atCap, World, arx, nation, Ruleset, "one-too-many"));

        var belowCap = ArmiesTestbed.WithArmies(state, crowd[..(cap - 1)]);
        Assert.NotNull(MobilizationArmyCreation.Create(belowCap, World, arx, nation, Ruleset, "the-last-one"));
    }
}
