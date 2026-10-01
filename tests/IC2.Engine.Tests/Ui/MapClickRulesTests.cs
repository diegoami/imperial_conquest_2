using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T99 Done-when 1: every row of the audit's §2.1 tables
/// (<c>docs/investigations/original-ui-command-audit.md</c>, read from <c>TUnitMap_SelectUnit</c> and
/// <c>TUnitMap_CheckForMove</c> [derived: code]) plus the user's two-button decision of 2026-10-01,
/// one test per row, pinned against the <em>composed order</em> — never the engine's acceptance of it,
/// which is exactly the embark hazard the task entry spells out (the engine still wants the same tile,
/// bug #453). The relation codes come from the real <c>classical-faithful</c> ruleset, never literals.
/// </summary>
public sealed class MapClickRulesTests
{
    private const string Rome = "rome";
    private const string Carthage = "carthage";

    private static Ruleset Ruleset() =>
        GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean").Ruleset;

    /// <summary>
    /// A left-click context with the common defaults: an own (Rome) selection, a Carthaginian target,
    /// distance 1, and the shipped ruleset's peace code as the relation.
    /// </summary>
    private static MapClickContext Click(
        MapClickTarget? target = null,
        MapClickSelection? selection = null,
        int distance = 1,
        int? relation = null,
        MapClickButton button = MapClickButton.Left,
        int x = 10,
        int y = 10,
        bool land = true,
        bool sea = false)
    {
        var ruleset = Ruleset();
        return new MapClickContext(
            Button: button,
            X: x,
            Y: y,
            TileIsLand: land,
            TileIsSea: sea,
            Target: target,
            Selection: selection,
            Distance: distance,
            Relation: relation ?? ruleset.Diplomacy.StateCodes.Peace,
            WarCode: ruleset.Diplomacy.StateCodes.War,
            ActiveNationId: Rome);
    }

    private static MapClickSelection OwnArmy(string id = "army-a", int moves = 8) =>
        new(MapEntityKind.Army, id, moves);

    private static MapClickSelection OwnFleet(string id = "fleet-a", int moves = 30, string? carriedArmyId = null) =>
        new(MapEntityKind.Fleet, id, moves, carriedArmyId);

    private static MapClickTarget ForeignArmy(string id = "army-b", string nation = Carthage) =>
        new(MapEntityKind.Army, id, nation);

    private static MapClickTarget ForeignCity(string id = "carthago", string nation = Carthage) =>
        new(MapEntityKind.City, id, nation);

    private static MapClickTarget ForeignFleet(string id = "fleet-b", string nation = Carthage) =>
        new(MapEntityKind.Fleet, id, nation);

    private static MapClickTarget OwnCity(string id = "roma") =>
        new(MapEntityKind.City, id, Rome);

    private static MapClickTarget OwnFleetMarker(string id = "fleet-a", string? carriedArmyId = null) =>
        new(MapEntityKind.Fleet, id, Rome, carriedArmyId);

    // ---- TUnitMap_SelectUnit's own table, row by row ----

    [Fact]
    public void An_own_army_clicking_an_enemy_city_at_distance_1_with_moves_besieges_it()
    {
        var outcome = MapClickRules.Resolve(Click(target: ForeignCity(), selection: OwnArmy()));

        Assert.Equal(MapClickOutcomeKind.BesiegeCity, outcome.Kind);
        Assert.Equal("besiege-city army-a carthago", outcome.OrderLine);
        Assert.True(outcome.RequiresWarConfirmation);
    }

    [Fact]
    public void An_own_army_clicking_an_enemy_army_at_distance_1_with_moves_attacks_it()
    {
        var outcome = MapClickRules.Resolve(Click(target: ForeignArmy(), selection: OwnArmy()));

        Assert.Equal(MapClickOutcomeKind.AttackArmy, outcome.Kind);
        Assert.Equal("attack-army army-a army-b", outcome.OrderLine);
        Assert.True(outcome.RequiresWarConfirmation);
    }

    [Fact]
    public void An_own_army_clicking_an_own_empty_fleet_at_distance_1_with_moves_embarks_and_selects_the_fleet()
    {
        var outcome = MapClickRules.Resolve(Click(target: OwnFleetMarker(), selection: OwnArmy()));

        Assert.Equal(MapClickOutcomeKind.EmbarkArmy, outcome.Kind);
        // The composed command only — the engine's own acceptance of it is bug #453's, not this table's.
        Assert.Equal("embark-army army-a fleet-a", outcome.OrderLine);
        Assert.Equal(MapEntityKind.Fleet, outcome.FocusKind);
        Assert.Equal("fleet-a", outcome.FocusId);
        Assert.False(outcome.RequiresWarConfirmation);
    }

    [Fact]
    public void An_own_fleet_clicking_an_enemy_fleet_at_distance_1_with_moves_attacks_it()
    {
        var outcome = MapClickRules.Resolve(Click(target: ForeignFleet(), selection: OwnFleet()));

        Assert.Equal(MapClickOutcomeKind.AttackFleet, outcome.Kind);
        Assert.Equal("attack-fleet fleet-a fleet-b", outcome.OrderLine);
        Assert.True(outcome.RequiresWarConfirmation);
    }

    [Fact]
    public void An_own_army_clicking_an_own_city_selects_the_city_and_orders_nothing()
    {
        // The audit's own consequence: no order moves an army onto a city tile -- the city is selected
        // and the army is simply deselected.
        var outcome = MapClickRules.Resolve(Click(target: OwnCity(), selection: OwnArmy()));

        Assert.Equal(MapClickOutcomeKind.Select, outcome.Kind);
        Assert.Equal(MapEntityKind.City, outcome.FocusKind);
        Assert.Equal("roma", outcome.FocusId);
        Assert.Null(outcome.OrderLine);
    }

    [Fact]
    public void Anything_clicking_an_own_army_makes_it_the_selection()
    {
        var outcome = MapClickRules.Resolve(Click(
            target: new MapClickTarget(MapEntityKind.Army, "army-c", Rome),
            selection: OwnArmy()));

        Assert.Equal(MapClickOutcomeKind.Select, outcome.Kind);
        Assert.Equal(MapEntityKind.Army, outcome.FocusKind);
        Assert.Equal("army-c", outcome.FocusId);
        Assert.Null(outcome.OrderLine);
    }

    [Fact]
    public void Anything_clicking_an_own_fleet_that_is_not_an_embark_target_makes_it_the_selection()
    {
        // An own fleet at distance 2 (or with an army already aboard, or clicked by an army without
        // moves) is not the embark row -- it is the plain selection row.
        var outcome = MapClickRules.Resolve(Click(target: OwnFleetMarker(), selection: OwnArmy(), distance: 2));

        Assert.Equal(MapClickOutcomeKind.Select, outcome.Kind);
        Assert.Null(outcome.OrderLine);
    }

    [Fact]
    public void An_own_army_without_moves_clicking_an_own_fleet_selects_the_fleet_instead_of_embarking()
    {
        var outcome = MapClickRules.Resolve(Click(target: OwnFleetMarker(), selection: OwnArmy(moves: 0)));

        Assert.Equal(MapClickOutcomeKind.Select, outcome.Kind);
        Assert.Null(outcome.OrderLine);
    }

    [Fact]
    public void An_own_army_clicking_an_own_fleet_already_carrying_an_army_selects_the_fleet_instead_of_embarking()
    {
        var outcome = MapClickRules.Resolve(Click(
            target: OwnFleetMarker(carriedArmyId: "army-c"),
            selection: OwnArmy()));

        Assert.Equal(MapClickOutcomeKind.Select, outcome.Kind);
        Assert.Null(outcome.OrderLine);
    }

    [Fact]
    public void An_own_army_without_moves_clicking_an_adjacent_enemy_army_drops_the_selection_and_shows_the_target()
    {
        var outcome = MapClickRules.Resolve(Click(target: ForeignArmy(), selection: OwnArmy(moves: 0)));

        Assert.Equal(MapClickOutcomeKind.DropSelectionAndShowTarget, outcome.Kind);
        Assert.Null(outcome.OrderLine);
        Assert.Equal(MapEntityKind.Army, outcome.FocusKind);
        Assert.Equal("army-b", outcome.FocusId);
    }

    [Fact]
    public void An_own_army_clicking_an_enemy_army_at_distance_2_drops_the_selection_and_shows_the_target()
    {
        var outcome = MapClickRules.Resolve(Click(target: ForeignArmy(), selection: OwnArmy(), distance: 2));

        Assert.Equal(MapClickOutcomeKind.DropSelectionAndShowTarget, outcome.Kind);
        Assert.Null(outcome.OrderLine);
    }

    [Fact]
    public void A_foreign_target_clicked_with_nothing_selected_drops_nothing_and_shows_the_target()
    {
        var outcome = MapClickRules.Resolve(Click(target: ForeignArmy(), selection: null));

        Assert.Equal(MapClickOutcomeKind.DropSelectionAndShowTarget, outcome.Kind);
        Assert.Null(outcome.OrderLine);
    }

    [Fact]
    public void An_own_fleet_clicking_a_foreign_army_never_attacks_it()
    {
        // The audit's first consequence: the army and city branches test only the selected army, so a
        // fleet never attacks an army (or besieges a city).
        var outcome = MapClickRules.Resolve(Click(target: ForeignArmy(), selection: OwnFleet()));

        Assert.Equal(MapClickOutcomeKind.DropSelectionAndShowTarget, outcome.Kind);
        Assert.Null(outcome.OrderLine);
    }

    [Fact]
    public void An_own_fleet_clicking_a_foreign_city_never_besieges_it()
    {
        var outcome = MapClickRules.Resolve(Click(target: ForeignCity(), selection: OwnFleet()));

        Assert.Equal(MapClickOutcomeKind.DropSelectionAndShowTarget, outcome.Kind);
        Assert.Null(outcome.OrderLine);
    }

    // ---- TUnitMap_CheckForMove's table, row by row ----

    [Fact]
    public void An_own_army_clicking_a_land_tile_moves_it_at_any_distance_and_stays_selected_while_moves_remain()
    {
        var outcome = MapClickRules.Resolve(Click(
            selection: OwnArmy(),
            target: null,
            distance: 9,
            x: 100,
            y: 40,
            land: true));

        Assert.Equal(MapClickOutcomeKind.MoveArmy, outcome.Kind);
        Assert.Equal("move army-a 100 40", outcome.OrderLine);
        Assert.True(outcome.KeepSelectionWhileMovesRemain);
    }

    [Fact]
    public void An_own_fleet_clicking_a_sea_tile_moves_it_at_any_distance_and_stays_selected_while_moves_remain()
    {
        var outcome = MapClickRules.Resolve(Click(
            selection: OwnFleet(),
            target: null,
            distance: 12,
            x: 47,
            y: 60,
            land: false,
            sea: true));

        Assert.Equal(MapClickOutcomeKind.MoveFleet, outcome.Kind);
        Assert.Equal("move-fleet fleet-a 47 60", outcome.OrderLine);
        Assert.True(outcome.KeepSelectionWhileMovesRemain);
    }

    [Fact]
    public void An_own_fleet_carrying_an_army_clicking_a_land_tile_at_distance_1_disembarks_and_shows_the_army()
    {
        var outcome = MapClickRules.Resolve(Click(
            selection: OwnFleet(carriedArmyId: "army-a"),
            target: null,
            land: true,
            sea: false));

        Assert.Equal(MapClickOutcomeKind.DisembarkArmy, outcome.Kind);
        Assert.Equal("disembark-army army-a", outcome.OrderLine);
        Assert.Equal(MapEntityKind.Army, outcome.FocusKind);
        Assert.Equal("army-a", outcome.FocusId);
        Assert.False(outcome.KeepSelectionWhileMovesRemain);
    }

    [Fact]
    public void An_own_fleet_carrying_an_army_clicking_a_land_tile_beyond_distance_1_orders_nothing()
    {
        var outcome = MapClickRules.Resolve(Click(
            selection: OwnFleet(carriedArmyId: "army-a"),
            target: null,
            distance: 2,
            land: true,
            sea: false));

        Assert.Equal(MapClickOutcomeKind.Nothing, outcome.Kind);
        Assert.Null(outcome.OrderLine);
    }

    [Fact]
    public void An_own_army_clicking_a_sea_tile_orders_nothing()
    {
        var outcome = MapClickRules.Resolve(Click(
            selection: OwnArmy(),
            target: null,
            land: false,
            sea: true));

        Assert.Equal(MapClickOutcomeKind.Nothing, outcome.Kind);
        Assert.Null(outcome.OrderLine);
    }

    [Fact]
    public void An_own_fleet_clicking_a_land_tile_without_an_army_aboard_orders_nothing()
    {
        var outcome = MapClickRules.Resolve(Click(
            selection: OwnFleet(),
            target: null,
            land: true,
            sea: false));

        Assert.Equal(MapClickOutcomeKind.Nothing, outcome.Kind);
        Assert.Null(outcome.OrderLine);
    }

    [Fact]
    public void A_left_click_on_open_terrain_with_nothing_selected_orders_nothing()
    {
        var outcome = MapClickRules.Resolve(Click(selection: null, target: null));

        Assert.Equal(MapClickOutcomeKind.Nothing, outcome.Kind);
    }

    // ---- the user's two-button decision: the right click ----

    [Fact]
    public void A_right_click_on_any_city_army_or_fleet_shows_its_unit_list_and_changes_nothing()
    {
        // Whatever is selected, and whether the marker is own or foreign: the right button opens the
        // list, and neither the selection nor any order changes.
        foreach (var target in new[] { OwnCity(), ForeignArmy(), ForeignFleet(), OwnFleetMarker() })
        {
            var withSelection = MapClickRules.Resolve(Click(
                target: target,
                selection: OwnArmy(),
                button: MapClickButton.Right));

            Assert.Equal(MapClickOutcomeKind.ShowUnitList, withSelection.Kind);
            Assert.Null(withSelection.OrderLine);
            Assert.False(withSelection.RequiresWarConfirmation);

            var withoutSelection = MapClickRules.Resolve(Click(
                target: target,
                selection: null,
                button: MapClickButton.Right));

            Assert.Equal(MapClickOutcomeKind.ShowUnitList, withoutSelection.Kind);
            Assert.Null(withoutSelection.OrderLine);
        }
    }

    [Fact]
    public void A_right_click_on_open_terrain_does_nothing()
    {
        var outcome = MapClickRules.Resolve(Click(
            selection: OwnArmy(),
            target: null,
            button: MapClickButton.Right));

        Assert.Equal(MapClickOutcomeKind.Nothing, outcome.Kind);
    }

    // ---- the war-confirmation rule (confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md) ----

    [Fact]
    public void An_attack_on_a_nation_at_war_carries_no_prompt()
    {
        var outcome = MapClickRules.Resolve(Click(
            target: ForeignArmy(),
            selection: OwnArmy(),
            relation: Ruleset().Diplomacy.StateCodes.War));

        Assert.Equal(MapClickOutcomeKind.AttackArmy, outcome.Kind);
        Assert.False(outcome.RequiresWarConfirmation);
        Assert.Null(outcome.ConfirmationText);
    }

    [Fact]
    public void A_besiege_on_a_nation_at_war_carries_no_prompt()
    {
        var outcome = MapClickRules.Resolve(Click(
            target: ForeignCity(),
            selection: OwnArmy(),
            relation: Ruleset().Diplomacy.StateCodes.War));

        Assert.Equal(MapClickOutcomeKind.BesiegeCity, outcome.Kind);
        Assert.False(outcome.RequiresWarConfirmation);
    }

    [Fact]
    public void An_attack_on_a_nation_at_peace_asks_the_originals_question()
    {
        var outcome = MapClickRules.Resolve(Click(
            target: ForeignArmy(),
            selection: OwnArmy(),
            relation: Ruleset().Diplomacy.StateCodes.Peace));

        Assert.True(outcome.RequiresWarConfirmation);
        // The original's own spacing: a space before the question mark.
        Assert.Equal("Are you sure you want to attack this army ?", outcome.ConfirmationText);
    }

    [Fact]
    public void A_besiege_on_a_nation_at_peace_asks_the_originals_question()
    {
        var outcome = MapClickRules.Resolve(Click(
            target: ForeignCity(),
            selection: OwnArmy(),
            relation: Ruleset().Diplomacy.StateCodes.Peace));

        Assert.True(outcome.RequiresWarConfirmation);
        Assert.Equal("Are you sure you want to attack this city ?", outcome.ConfirmationText);
    }

    [Fact]
    public void A_naval_attack_on_a_nation_at_peace_asks_the_originals_question()
    {
        var outcome = MapClickRules.Resolve(Click(
            target: ForeignFleet(),
            selection: OwnFleet(),
            relation: Ruleset().Diplomacy.StateCodes.Peace));

        Assert.True(outcome.RequiresWarConfirmation);
        Assert.Equal("Are you sure you want to attack this fleet ?", outcome.ConfirmationText);
    }

    [Fact]
    public void An_attack_on_a_trade_partner_still_asks()
    {
        var outcome = MapClickRules.Resolve(Click(
            target: ForeignArmy(),
            selection: OwnArmy(),
            relation: Ruleset().Diplomacy.StateCodes.Trade));

        Assert.Equal(MapClickOutcomeKind.AttackArmy, outcome.Kind);
        Assert.True(outcome.RequiresWarConfirmation);
    }

    [Fact]
    public void An_attack_on_an_ally_still_asks()
    {
        var outcome = MapClickRules.Resolve(Click(
            target: ForeignArmy(),
            selection: OwnArmy(),
            relation: Ruleset().Diplomacy.StateCodes.Alliance));

        Assert.Equal(MapClickOutcomeKind.AttackArmy, outcome.Kind);
        Assert.True(outcome.RequiresWarConfirmation);
    }
}
