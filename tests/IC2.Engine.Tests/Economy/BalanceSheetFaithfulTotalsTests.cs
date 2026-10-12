using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/tasks/T167.md</c> Done-when 1 to 3: under <c>classical-faithful</c>
/// (<see cref="BalanceSheetTotalsPolicy.Original"/>) the Balance sheet totals as the original does; under
/// <c>improved</c> and the toy ruleset (<see cref="BalanceSheetTotalsPolicy.Reconciling"/>) it is unchanged.
/// The sheet is a projection, so no charge moves under either policy.
/// </summary>
public sealed class BalanceSheetFaithfulTotalsTests
{
    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    private static Ruleset Original => With(BalanceSheetTotalsPolicy.Original);

    private static Ruleset Reconciling => With(BalanceSheetTotalsPolicy.Reconciling);

    private static Ruleset With(BalanceSheetTotalsPolicy policy) =>
        Classical.Ruleset with { Flags = Classical.Ruleset.Flags with { BalanceSheetTotals = policy } };

    private static GameState InitialState() =>
        GameStateFactory.CreateInitial(Classical.World, Classical.Ruleset, Classical.Scenario);

    private static GameState WithoutRomeUnits(GameState state) => state with
    {
        Armies = ValueList.From(state.Armies.Where(a => !string.Equals(a.Nation, "rome", StringComparison.Ordinal))),
        Fleets = ValueList.From(state.Fleets.Where(f => !string.Equals(f.Nation, "rome", StringComparison.Ordinal))),
    };

    private static CityState RomeCity(GameState state) =>
        state.Cities.First(city => string.Equals(city.Owner, "rome", StringComparison.Ordinal));

    private static FleetState Fleet(GameState state, int ships, int? constructionTicks) => new(
        Id: "rome-faithful-fleet",
        Nation: "rome",
        X: RomeCity(state).X,
        Y: RomeCity(state).Y,
        Moves: 5,
        Ships: ships,
        ConditionPercent: 100,
        Money: 0,
        SupplyTons: 0,
        ConstructionTicksRemaining: constructionTicks,
        BuildCityId: constructionTicks is null ? null : RomeCity(state).Id,
        CarriedArmyId: null,
        CoveredTileCode: 0);

    private static ArmyState MercenaryArmy(GameState state) => new(
        Id: "rome-faithful-mercenaries",
        Nation: "rome",
        X: RomeCity(state).X,
        Y: RomeCity(state).Y,
        Moves: 1,
        Morale: 50,
        Money: 1000,
        SupplyTons: 0,
        CoveredTileCode: 0,
        AboardFleetId: null,
        Units: ValueList.Of(new UnitSlot(
            MercenaryLabel: 1, UnitTypeId: "light_infantry", Troops: 1000, Quality: 5, Name: "Faithful Mercenaries")));

    private static int OtherLines(BalanceSheet sheet) =>
        sheet.CityAndWealthUpkeep + sheet.ShipUpkeep + sheet.RecruitmentSlotUpkeep + sheet.RegularsUpkeep;

    private static GameState WithRomeWealth(GameState state, int wealth) => state with
    {
        Nations = ValueList.From(state.Nations.Select(n => n.Id == "rome" ? n with { Wealth = wealth } : n)),
    };

    // ---- Done-when 1 ----

    [Fact]
    public void Original_TotalIncludesTheMercenaryUnitsLine()
    {
        var state = WithoutRomeUnits(InitialState());
        state = state with { Armies = ValueList.From(state.Armies.Append(MercenaryArmy(state))) };

        var sheet = BalanceSheet.For(state, "rome", Original);

        Assert.True(sheet.MercenariesPay > 0);
        Assert.Equal(OtherLines(sheet) + sheet.MercenariesPay, sheet.ExpenditureTotal);
    }

    [Fact]
    public void Reconciling_TotalExcludesTheMercenaryUnitsLine()
    {
        var state = WithoutRomeUnits(InitialState());
        state = state with { Armies = ValueList.From(state.Armies.Append(MercenaryArmy(state))) };

        var sheet = BalanceSheet.For(state, "rome", Reconciling);

        Assert.True(sheet.MercenariesPay > 0);
        Assert.Equal(OtherLines(sheet), sheet.ExpenditureTotal);
    }

    // ---- Done-when 2 ----

    [Fact]
    public void Original_AFleetUnderConstructionAddsShipsTimesRateToTheFleetLineAndTheTotal()
    {
        var state = WithoutRomeUnits(InitialState());
        var rate = Original.Economy.ShipUpkeepPerQuarter;
        var before = BalanceSheet.For(state, "rome", Original);
        Assert.Equal(0, before.ShipUpkeep);

        state = state with { Fleets = ValueList.From(state.Fleets.Append(Fleet(state, 11, constructionTicks: 2))) };
        var after = BalanceSheet.For(state, "rome", Original);

        Assert.Equal(11 * rate, after.ShipUpkeep);
        Assert.Equal(before.ExpenditureTotal + (11 * rate), after.ExpenditureTotal);
    }

    [Fact]
    public void Original_ALaunchedFleetStillCountsAtTheSameRate()
    {
        var state = WithoutRomeUnits(InitialState());
        state = state with { Fleets = ValueList.From(state.Fleets.Append(Fleet(state, 10, constructionTicks: null))) };

        Assert.Equal(10 * Original.Economy.ShipUpkeepPerQuarter, BalanceSheet.For(state, "rome", Original).ShipUpkeep);
    }

    [Fact]
    public void Reconciling_AFleetUnderConstructionAddsNothing()
    {
        var state = WithoutRomeUnits(InitialState());
        var before = BalanceSheet.For(state, "rome", Reconciling);

        state = state with { Fleets = ValueList.From(state.Fleets.Append(Fleet(state, 11, constructionTicks: 2))) };
        var after = BalanceSheet.For(state, "rome", Reconciling);

        Assert.Equal(before.ShipUpkeep, after.ShipUpkeep);
        Assert.Equal(before.ExpenditureTotal, after.ExpenditureTotal);
    }

    [Fact]
    public void TheQuartersTreasuryChargeIsTheSameUnderBothPolicies()
    {
        var state = WithoutRomeUnits(InitialState());
        state = state with
        {
            Fleets = ValueList.From(state.Fleets.Append(Fleet(state, 11, constructionTicks: 2))),
            Armies = ValueList.From(state.Armies.Append(MercenaryArmy(state))),
        };

        // The charge helper skips fleets under construction whatever the policy.
        Assert.Equal(0, ShipUpkeep.ComputeForNation(state, "rome", Original));
        Assert.Equal(0, ShipUpkeep.ComputeForNation(state, "rome", Reconciling));

        GameState FireQuarter(Ruleset ruleset)
        {
            var registry = SystemRegistry.FromEngineAssembly();
            var dispatcher = new CommandDispatcher(registry, ruleset, Classical.World, NullEventSink.Instance);
            var coordinator = new TurnCoordinator(registry, ruleset, Classical.World, NullEventSink.Instance, dispatcher);
            return coordinator.FireQuarterBoundary(state, state.Calendar.SeasonIndex);
        }

        var original = FireQuarter(Original);
        var reconciling = FireQuarter(Reconciling);
        Assert.Equal(reconciling.NationById("rome")!.Treasury, original.NationById("rome")!.Treasury);
        Assert.Equal(
            reconciling.Armies.Select(a => (a.Id, a.Money)).ToList(),
            original.Armies.Select(a => (a.Id, a.Money)).ToList());
    }

    // ---- Done-when 3 ----

    /// <summary>The nine played rows of research 89c37ff, G2: wealth, unrounded limit, shown limit.</summary>
    public static IEnumerable<object[]> PlayedRows() => new[]
    {
        new object[] { 2_577_000, 5_154, 5_000 },
        new object[] { 2_400_000, 4_800, 4_800 },
        new object[] { 2_499_999, 4_999, 4_900 },
        new object[] { 2_500_500, 5_001, 5_000 },
        new object[] { 3_000_000, 6_000, 6_000 },
        new object[] { 6_000_000, 12_000, 12_000 },
        new object[] { 9_999_999, 19_999, 19_000 },
        new object[] { 10_000_000, 20_000, 20_000 },
        new object[] { 12_000_000, 24_000, 20_000 },
    };

    [Theory]
    [MemberData(nameof(PlayedRows))]
    public void Original_DebtLimitIsRoundedAsTheOriginalShowsIt(int wealth, int unrounded, int shown)
    {
        var state = WithRomeWealth(InitialState(), wealth);
        var cap = -Original.Economy.DebtTreasuryFloor;

        // The unrounded figure is the deposition's, capped.
        Assert.Equal(Math.Min(unrounded, cap), Deposition.DebtLimit(state.NationById("rome")!, Original));
        Assert.Equal(shown, BalanceSheet.For(state, "rome", Original).DebtLimit);
    }

    [Theory]
    [MemberData(nameof(PlayedRows))]
    public void Reconciling_DebtLimitIsTheUnroundedFigure(int wealth, int unrounded, int shown)
    {
        _ = shown;
        var state = WithRomeWealth(InitialState(), wealth);
        var rome = state.NationById("rome")!;
        var cap = -Reconciling.Economy.DebtTreasuryFloor;

        Assert.Equal(Math.Min(unrounded, cap), BalanceSheet.For(state, "rome", Reconciling).DebtLimit);
        Assert.Equal(Deposition.DebtLimit(rome, Reconciling), BalanceSheet.For(state, "rome", Reconciling).DebtLimit);
    }

    [Fact]
    public void Original_TheDepositionTestKeepsTheUnroundedLimit()
    {
        var state = WithRomeWealth(InitialState(), 2_499_999);   // unrounded 4,999, shown 4,900
        var rome = state.NationById("rome")! with { Unity = 600 };

        Assert.Equal(4_999, Deposition.DebtLimit(rome, Original));
        Assert.False(Deposition.InDebt(rome with { Treasury = -4_999 }, Original));
        Assert.True(Deposition.InDebt(rome with { Treasury = -5_000 }, Original));
    }

    /// <summary>
    /// Each threshold and step is read from the ruleset: moving the low maximum or the mid maximum down by
    /// one flips the band of the limit one above it (4,999 and 9,999), and moving a step by one changes the
    /// rounded figure. (A threshold moved up by one cannot change a figure, since the neighbouring bands
    /// round a multiple of both steps identically.)
    /// </summary>
    [Theory]
    [InlineData(4_999, 4_998, 0, 0, 4_500)]
    [InlineData(9_999, 0, 9_998, 0, 9_000)]
    [InlineData(4_999, 0, 0, 101, 4_949)]
    [InlineData(5_100, 0, 0, 0, 5_000)]
    public void Original_EachThresholdAndStepIsReadFromTheRuleset(
        int limit, int lowMax, int midMax, int lowStep, int expected)
    {
        var ruleset = Original;
        var economy = ruleset.Economy;
        ruleset = ruleset with
        {
            Economy = economy with
            {
                BalanceSheetDebtLimitLowMax = lowMax != 0 ? lowMax : economy.BalanceSheetDebtLimitLowMax,
                BalanceSheetDebtLimitMidMax = midMax != 0 ? midMax : economy.BalanceSheetDebtLimitMidMax,
                BalanceSheetDebtLimitLowStep = lowStep != 0 ? lowStep : economy.BalanceSheetDebtLimitLowStep,
            },
        };

        var state = WithRomeWealth(InitialState(), limit * ruleset.Economy.DebtWealthDivisor);

        Assert.Equal(expected, BalanceSheet.For(state, "rome", ruleset).DebtLimit);
    }

    [Fact]
    public void Original_TheMidStepIsReadFromTheRuleset()
    {
        var ruleset = Original;
        ruleset = ruleset with { Economy = ruleset.Economy with { BalanceSheetDebtLimitMidStep = 501 } };
        var state = WithRomeWealth(InitialState(), 9_999 * ruleset.Economy.DebtWealthDivisor);

        Assert.Equal(9_519, BalanceSheet.For(state, "rome", ruleset).DebtLimit);
    }

    [Fact]
    public void Original_TheHighStepIsReadFromTheRuleset()
    {
        var ruleset = Original;
        ruleset = ruleset with { Economy = ruleset.Economy with { BalanceSheetDebtLimitHighStep = 1_001 } };
        var state = WithRomeWealth(InitialState(), 19_999 * ruleset.Economy.DebtWealthDivisor);

        Assert.Equal(19_019, BalanceSheet.For(state, "rome", ruleset).DebtLimit);
    }
}
