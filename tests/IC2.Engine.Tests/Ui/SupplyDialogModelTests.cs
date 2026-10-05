using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T134 Done-when 4: <see cref="SupplyDialogModel"/>'s provider rows, its free and paid paths, the cost
/// it shows, its staged clamps and the command lines it composes — every limit read from the ruleset,
/// never a literal.
/// </summary>
/// <remarks>
/// <strong>The 48,173-troop/403-ton army is the report's own fill.</strong>
/// <c>supply-capacity-rounding.md</c>'s <c>11.sav → 11_supply.sav</c> episode is a 48,173-troop army at
/// 403 tons, whose dialog room is <c>48173 / 100 + 1 − 403 = 79</c> — the example Done-when 4 names. The
/// ruleset is the shipped <c>classical-faithful</c> one, so the constants (including the per-unit-purse
/// flag) are the real ones; only the state is scripted.
/// </remarks>
public sealed class SupplyDialogModelTests
{
    private const string ArmyId = "t134-army";
    private const string ArmyNationId = "north";
    private const string OwnCityId = "t134-own-city";
    private const string ForeignCityId = "t134-foreign-city";
    private const string OwnFleetId = "t134-own-fleet";

    private static readonly Lazy<Ruleset> LazyClassicalRuleset = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean").Ruleset);

    private static Ruleset ClassicalRuleset => LazyClassicalRuleset.Value;

    /// <summary>The 48,173-troop army at 403 tons, an own city, a foreign city and an own fleet, one tile apart.</summary>
    private static GameState Scripted(int armyMoney = 100, int foreignCityStock = 330)
    {
        var initial = CoreTestbed.InitialState();
        var army = new ArmyState(
            ArmyId, ArmyNationId, X: 3, Y: 2, Moves: 4, Morale: 70, Money: armyMoney, SupplyTons: 403,
            CoveredTileCode: null, AboardFleetId: null,
            Units: ValueList.Of(new UnitSlot(0, "heavy_infantry", 48_173, 6, "T134 Test Battalion")));

        return initial with
        {
            Armies = ValueList.Of(new[] { army }),
            Cities = ValueList.Of(new[]
            {
                City(OwnCityId, "Antium", X: 4, Y: 2, Owner: ArmyNationId, supplyTons: 330),
                City(ForeignCityId, "Capua", X: 2, Y: 2, Owner: "south", supplyTons: foreignCityStock),
            }),
            Fleets = ValueList.Of(new[] { Fleet(OwnFleetId, X: 3, Y: 3, supplyTons: 200) }),
        };
    }

    private static CityState City(string id, string name, int X, int Y, string Owner, int supplyTons) => new(
        id, name, X, Y, Owner, Owner, Loyalty: 90, SupplyTons: supplyTons, FortificationCode: 0,
        PopulationThousands: 100, MaxPopulationThousands: 100, Tribute: 0, UnderSiege: false,
        Garrison: ValueList<UnitSlot>.Empty);

    private static FleetState Fleet(string id, int X, int Y, int supplyTons) => new(
        id, ArmyNationId, X, Y, Moves: 4, Ships: 10, ConditionPercent: 100, Money: 0, SupplyTons: supplyTons,
        ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: null, CoveredTileCode: null);

    private static SupplyDialogModel Model(GameState state, Ruleset? ruleset = null)
    {
        var effective = ruleset ?? ClassicalRuleset;
        var army = state.ArmyById(ArmyId)!;
        return SupplyDialogModel.ForArmy(state, army, effective);
    }

    private static void SelectById(SupplyDialogModel model, string providerId)
    {
        for (var i = 0; i < model.Providers.Count; i++)
        {
            if (string.Equals(model.Providers[i].Id, providerId, StringComparison.Ordinal))
            {
                model.SelectProvider(i);
                return;
            }
        }

        Assert.Fail($"Provider '{providerId}' is not listed.");
    }

    [Fact]
    public void The_provider_rows_show_the_stock_and_the_first_is_selected_on_opening()
    {
        var model = Model(Scripted());

        Assert.Equal(3, model.Providers.Count);
        Assert.Equal(OwnCityId, model.SelectedProvider!.Id);
        Assert.True(model.SelectedProvider.IsFree);
        Assert.Equal("Supplies at Antium: 330", model.Providers[0].Label);
        Assert.Equal(ForeignCityId, model.Providers[1].Id);
        Assert.False(model.Providers[1].IsFree);
        Assert.Equal(OwnFleetId, model.Providers[2].Id);
        Assert.True(model.Providers[2].IsFree);
    }

    [Fact]
    public void The_free_city_path_composes_one_buy_per_press()
    {
        var model = Model(Scripted());
        SelectById(model, OwnCityId);

        Assert.Equal($"buy {ArmyId} {OwnCityId} 10", model.PressSupply(SupplyDialogModel.SupplyStepTons));
        Assert.Equal($"buy {ArmyId} {OwnCityId} 10", model.PressSupply(SupplyDialogModel.SupplyStepTons));
        Assert.Equal(0, model.StagedTons);
    }

    [Fact]
    public void The_free_fleet_path_composes_one_buy_with_the_fleet_token_per_press()
    {
        var model = Model(Scripted());
        SelectById(model, OwnFleetId);

        Assert.Equal(
            $"buy {ArmyId} fleet {OwnFleetId} 100",
            model.PressSupply(SupplyDialogModel.SupplyLargeStepTons));
    }

    [Fact]
    public void The_room_is_the_reports_79_for_a_48173_troop_army_at_403_tons()
    {
        var model = Model(Scripted());

        Assert.Equal(79, model.ArmyRoomTons);
    }

    [Fact]
    public void The_paid_path_stages_the_step_and_clamps_to_the_room()
    {
        var model = Model(Scripted(armyMoney: 100));
        SelectById(model, ForeignCityId);

        Assert.Null(model.PressSupply(SupplyDialogModel.SupplyLargeStepTons)); // staged, not submitted.
        Assert.Equal(79, model.StagedTons);
        Assert.Equal(15, model.StagedCostTalents); // 79 / 5.
        Assert.Equal($"buy {ArmyId} {ForeignCityId} 79", model.TransferStaged());
    }

    [Fact]
    public void The_staged_amount_never_goes_below_zero()
    {
        var model = Model(Scripted());
        SelectById(model, ForeignCityId);

        model.AdjustStaged(-10);
        Assert.Equal(0, model.StagedTons);
        model.AdjustStaged(30);
        model.AdjustStaged(-100);
        Assert.Equal(0, model.StagedTons);
    }

    [Fact]
    public void The_purse_clamps_the_staged_amount_under_classical_faithful()
    {
        var model = Model(Scripted(armyMoney: 12));
        SelectById(model, ForeignCityId);

        model.AdjustStaged(1000);

        Assert.Equal(60, model.StagedTons); // money 12 x supplyTonsPerTalent 5.
        Assert.Equal(12, model.StagedCostTalents);
    }

    [Fact]
    public void There_is_no_money_clamp_under_improved()
    {
        var improved = ClassicalRuleset with
        {
            Flags = ClassicalRuleset.Flags with { EconomyPurses = EconomyPurseModel.CentralTreasury },
        };
        var model = Model(Scripted(armyMoney: 0), improved);
        SelectById(model, ForeignCityId);

        model.AdjustStaged(1000);

        // No purse touched: the only clamps are the room (79) and the stock (330).
        Assert.Equal(79, model.StagedTons);
    }

    [Fact]
    public void Every_limit_reads_the_ruleset_not_a_literal()
    {
        // Widen the capacity bonus and change the tons-per-talent rate: the room clamp and the cost both
        // move, so neither can be a hardcoded 79 or /5.
        var widened = ClassicalRuleset with
        {
            Economy = ClassicalRuleset.Economy with
            {
                SupplyDialogArmyCapacityBonus = 100,
                SupplyTonsPerTalent = 10,
            },
        };
        var model = Model(Scripted(armyMoney: 100, foreignCityStock: 1000), widened);

        Assert.Equal(48173 / 100 + 100 - 403, model.ArmyRoomTons); // 178.
        SelectById(model, ForeignCityId);
        model.AdjustStaged(1000);

        Assert.Equal(178, model.StagedTons);
        Assert.Equal(17, model.StagedCostTalents); // 178 / 10.
    }

    [Fact]
    public void Transfer_composes_one_buy_of_the_staged_amount_and_close_composes_nothing()
    {
        var model = Model(Scripted());
        SelectById(model, ForeignCityId);
        model.AdjustStaged(50);

        Assert.Equal($"buy {ArmyId} {ForeignCityId} 50", model.TransferStaged());

        model.AdjustStaged(50);
        Assert.Null(model.Close());
        Assert.Equal(0, model.StagedTons);
        Assert.Null(model.TransferStaged());
    }

    [Fact]
    public void The_money_panel_composes_transfer_money_with_and_without_a_via_fleet()
    {
        var model = Model(Scripted());

        Assert.Equal($"transfer-money {ArmyId} 100", model.PressMoney(100, null));
        Assert.Equal($"transfer-money {ArmyId} -10 via {OwnFleetId}", model.PressMoney(-10, OwnFleetId));
        Assert.Null(model.PressMoney(0, null));
    }

    [Fact]
    public void The_via_picker_starts_with_the_treasury_and_lists_the_own_fleet_within_one_tile()
    {
        var model = Model(Scripted());

        Assert.Equal(2, model.MoneyViaChoices.Count);
        Assert.Equal("Treasury", model.MoneyViaChoices[0].Label);
        Assert.Null(model.MoneyViaChoices[0].FleetId);
        Assert.Equal(OwnFleetId, model.MoneyViaChoices[1].FleetId);
    }
}
