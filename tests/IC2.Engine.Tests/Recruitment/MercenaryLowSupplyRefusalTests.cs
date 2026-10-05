using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Tests.Cities.Capture;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// Bug #769, T143: the original's low-supply refusal. A player's hire is refused with
/// <c>mercenary.too-few-supplies</c> and <em>"No mercenaries will join an army with so few supplies."</em>
/// when <c>army.SupplyTons × 10000 / total &lt; 15</c> in integer arithmetic, where <c>total</c> is the
/// army's troops <strong>before</strong> the hire
/// <strong>[confirmed: code, decompiled-mercenary-offer-list-and-position.md §1,
/// <c>TUnitMap_RecruitMercenaries</c> @ <c>0x00446FF4</c>, :46858–46925]</strong>. It sits after the
/// enemy-city refusal and before the embarked fleet's capacity check, and an army with no troops is
/// refused without dividing <strong>[designed: the original's <c>div</c> by zero is no rule to copy]</strong>.
/// </summary>
/// <remarks>
/// The shipped toy ruleset's <c>economyPurses</c> is <c>perUnitPurses</c> — <c>classical-faithful</c> —
/// and the refusal does not read the flag, so both presets refuse alike. Every offer here has a
/// <c>(100 × 1 / 1000) × 6 = 0</c> gate and each army holds 100 talents, so only the supply floor can
/// refuse it; the three refused-but-would-otherwise-hire cases are the boundary values the DoD names
/// (14/15 at 10,000 troops, 10/11 at 6,700).
/// </remarks>
public sealed class MercenaryLowSupplyRefusalTests
{
    private const string ArmyId = "low-supply-army";

    private static MercenaryPoolSlot OfferAt(int x, int y, int troops = 100, int quality = 6) =>
        new(SlotIndex: 33, X: x, Y: y, NameLabel: 11, UnitTypeId: "light_infantry", Troops: troops, Quality: quality);

    private static ArmyState Army(int troops, int supply, int money = 100, string? aboardFleetId = null) =>
        new(
            ArmyId, "north", 3, 2, Moves: 5, Morale: 68, Money: money, SupplyTons: supply,
            CoveredTileCode: aboardFleetId is null ? 2 : null,
            AboardFleetId: aboardFleetId,
            Units: ValueList.Of(new UnitSlot(0, "light_infantry", troops, 6, "Test Battalion")));

    private static GameState WithArmy(GameState state, ArmyState army) =>
        state with { Armies = ValueList.From(state.Armies.Append(army)) };

    private static GameState WithEnemyTown(GameState state, int x, int y, string owner) =>
        state with
        {
            Cities = ValueList.From(state.Cities.Append(CaptureTestbed.City(
                "enemy-town", "enemy-town", x, y, owner, owner, loyalty: 50, fortificationCode: 0,
                populationThousands: 10, maxPopulationThousands: 20, tribute: 0))),
        };

    private static GameState AtWar(GameState state) =>
        state with
        {
            Relations = state.Relations.WithRelation(
                "north", "south", RecruitmentTestbed.Ruleset.Diplomacy.StateCodes.War),
        };

    [Fact]
    public void An_army_of_10000_troops_holding_14_tons_is_refused_and_changes_nothing()
    {
        Assert.Equal(EconomyPurseModel.PerUnitPurses, RecruitmentTestbed.Ruleset.Flags.EconomyPurses);
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var before = RecruitmentTestbed.WithMercenaryPool(
            WithArmy(RecruitmentTestbed.InitialState(), Army(10_000, supply: 14)), OfferAt(2, 1));

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, ArmyId, 33));

        Assert.Equal(HireMercenaryRejections.TooFewSupplies, result.Code);
        Assert.Equal("No mercenaries will join an army with so few supplies.", result.Rejection!.Message);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void An_army_of_10000_troops_holding_15_tons_hires()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var army = Army(10_000, supply: 15);
        var before = RecruitmentTestbed.WithMercenaryPool(
            WithArmy(RecruitmentTestbed.InitialState(), army), OfferAt(2, 1));

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, 33));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(army.Units.Count + 1, result.State.ArmyById(army.Id)!.Units.Count);
    }

    [Fact]
    public void An_army_of_6700_troops_holding_10_tons_is_refused_and_changes_nothing()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var before = RecruitmentTestbed.WithMercenaryPool(
            WithArmy(RecruitmentTestbed.InitialState(), Army(6_700, supply: 10)), OfferAt(2, 1));

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, ArmyId, 33));

        Assert.Equal(HireMercenaryRejections.TooFewSupplies, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void An_army_of_6700_troops_holding_11_tons_hires()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var army = Army(6_700, supply: 11);
        var before = RecruitmentTestbed.WithMercenaryPool(
            WithArmy(RecruitmentTestbed.InitialState(), army), OfferAt(2, 1));

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, 33));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(army.Units.Count + 1, result.State.ArmyById(army.Id)!.Units.Count);
    }

    [Fact]
    public void An_army_with_too_few_supplies_next_to_an_enemy_city_is_refused_by_the_enemy_city_first()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var withArmy = WithArmy(RecruitmentTestbed.InitialState(), Army(10_000, supply: 14));
        var before = RecruitmentTestbed.WithMercenaryPool(
            AtWar(WithEnemyTown(withArmy, 4, 2, "south")), OfferAt(4, 2));

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, ArmyId, 33));

        Assert.Equal(HireMercenaryRejections.EnemyCity, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void An_embarked_army_with_too_few_supplies_and_no_fleet_space_is_refused_by_the_supplies_first()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var initial = RecruitmentTestbed.InitialState();
        var fleet = initial.FleetById("north-fleet-1")!;
        var army = Army(4000, supply: 0, aboardFleetId: fleet.Id);
        var bigOffer = OfferAt(2, 1, troops: 2000, quality: 5);
        var before = RecruitmentTestbed.WithMercenaryPool(WithArmy(initial, army), bigOffer);

        var capacity = fleet.Ships * RecruitmentTestbed.Ruleset.Naval.TransportTroopsPerShip;
        Assert.True(army.TotalTroops + bigOffer.Troops > capacity); // the fleet check would refuse it too.

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, army.Id, bigOffer.SlotIndex));

        Assert.Equal(HireMercenaryRejections.TooFewSupplies, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void An_army_with_no_troops_is_refused_by_the_supply_floor_without_dividing()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var before = RecruitmentTestbed.WithMercenaryPool(
            WithArmy(RecruitmentTestbed.InitialState(), Army(0, supply: 0)), OfferAt(2, 1));

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, ArmyId, 33));

        Assert.Equal(HireMercenaryRejections.TooFewSupplies, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void An_army_with_no_troops_next_to_an_enemy_city_gets_the_enemy_city_refusal_first()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var withArmy = WithArmy(RecruitmentTestbed.InitialState(), Army(0, supply: 0));
        var before = RecruitmentTestbed.WithMercenaryPool(
            AtWar(WithEnemyTown(withArmy, 4, 2, "south")), OfferAt(4, 2));

        var result = dispatcher.Dispatch(before, new HireMercenaryCommand(before.ActiveNationId, ArmyId, 33));

        Assert.Equal(HireMercenaryRejections.EnemyCity, result.Code);
        Assert.Same(before, result.State);
    }
}
