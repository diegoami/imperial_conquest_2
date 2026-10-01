using System.Text.RegularExpressions;
using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/tasks/T104.md</c> "The balance sheet: a read-only projection of the quarter's budget",
/// Done-when 1 through 5. Every test starts from the real, committed <c>classical-mediterranean</c>
/// world/scenario — the same data the audit's Balance sheet row describes — never a hand-built state.
/// </summary>
public sealed class BalanceSheetTests
{
    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    /// <summary>
    /// A <c>classical-mediterranean</c> state on the last week before a quarter (week 11, the season
    /// boundary the calendar fires on), with every city held at its maximum population and maximum
    /// loyalty. That makes the quarter tick's city loop a no-op for population, ownership and the rebuilt
    /// wealth/tax base — so the projection taken before the tick is exactly the state the treasury credit
    /// reads when it runs.
    /// </summary>
    private static GameState ClassicalOnTheLastWeekBeforeAQuarter()
    {
        var scenario = Classical;
        var ruleset = scenario.Ruleset;
        var state = GameStateFactory.CreateInitial(scenario.World, scenario.Ruleset, scenario.Scenario);
        var coordinator = NewCoordinator(scenario, out _);

        // Advance the calendar to the last week before a quarter, a round at a time. No seat turns are
        // played, and the weekly tick moves no nation's treasury, so this only reaches the boundary.
        while (state.Calendar.Week != ruleset.Calendar.SeasonAdvanceFromWeek)
        {
            state = coordinator.RunRoundTick(state).State;
        }

        state = state with
        {
            Cities = ValueList.From(state.Cities.Select(city => city with
            {
                PopulationThousands = city.MaxPopulationThousands,
                Loyalty = 100,
            })),
        };

        // Keep the two budget lines discriminating (a positive tax base and a positive treasury) and make
        // Rome a stable human seat so the AI deposition path never touches it.
        state = state with
        {
            Nations = ValueList.From(state.Nations.Select(nation => nation.Id == "rome"
                ? nation with
                {
                    Control = SeatControl.Human,
                    Unity = 600,
                    TaxRatePercent = 15,
                    MobilizedPercent = 50,
                }
                : nation with { Unity = 600 })),
        };

        // Establish the exact wealth and tax base the city loop will rebuild, so the tick's rebuild is a
        // no-op rather than a shift under the projection.
        return NationTaxBaseRebuild.Rebuild(state, ruleset);
    }

    private static TurnCoordinator NewCoordinator(ResolvedScenario scenario, out CommandDispatcher dispatcher)
    {
        var registry = SystemRegistry.FromEngineAssembly();
        dispatcher = new CommandDispatcher(registry, scenario.Ruleset, scenario.World, NullEventSink.Instance);
        return new TurnCoordinator(registry, scenario.Ruleset, scenario.World, NullEventSink.Instance, dispatcher);
    }

    private static GameState FireQuarter(GameState state)
    {
        var scenario = Classical;
        var coordinator = NewCoordinator(scenario, out _);
        return coordinator.FireQuarterBoundary(state, state.Calendar.SeasonIndex);
    }

    // ---- Done-when 1 ----

    /// <summary>
    /// Rome's projected income total minus its expenditure total equals the change in Rome's treasury
    /// across the quarter tick, read from the state before and after it, with nothing else moving the
    /// treasury in between. Neither total alone equals the credit or the bill, because
    /// <see cref="NationTreasuryCredit"/> nets the city-count and wealth terms into the credit.
    /// </summary>
    [Fact]
    public void RomeIncomeMinusExpenditure_EqualsItsTreasuryChangeAcrossTheQuarter()
    {
        var ruleset = Classical.Ruleset;
        var before = ClassicalOnTheLastWeekBeforeAQuarter();
        var sheet = BalanceSheet.For(before, "rome", ruleset);

        var after = FireQuarter(before);
        var romeBefore = before.NationById("rome")!;
        var romeAfter = after.NationById("rome")!;

        Assert.Equal(romeAfter.Treasury - romeBefore.Treasury, sheet.IncomeTotal - sheet.ExpenditureTotal);

        var credit = NationTreasuryCredit.Compute(romeBefore, before, ruleset);
        var bill = sheet.ShipUpkeep + sheet.RecruitmentSlotUpkeep + sheet.RegularsUpkeep + sheet.MercenariesPay;

        Assert.True(sheet.CityAndWealthUpkeep > 0);
        Assert.NotEqual(credit, sheet.IncomeTotal);
        Assert.NotEqual(credit, sheet.ExpenditureTotal);
        Assert.NotEqual(bill, sheet.IncomeTotal);
        Assert.NotEqual(bill, sheet.ExpenditureTotal);
    }

    // ---- Done-when 2 ----

    /// <summary>
    /// The same identity holds for a nation with a launched fleet and a funded mercenary army, so the
    /// ship and mercenary terms are non-zero, and the three income lines match
    /// <see cref="NationTreasuryCredit"/>'s three income terms one for one.
    /// </summary>
    [Fact]
    public void AFleetAndMercenaryNation_ReconcilesAndItsIncomeLinesMatchTheCredit()
    {
        var ruleset = Classical.Ruleset;
        var before = ClassicalOnTheLastWeekBeforeAQuarter();
        var romeCity = before.Cities.First(city => string.Equals(city.Owner, "rome", StringComparison.Ordinal));

        var fleet = new FleetState(
            Id: "rome-balance-fleet",
            Nation: "rome",
            X: romeCity.X,
            Y: romeCity.Y,
            Moves: 5,
            Ships: 10,
            ConditionPercent: 100,
            Money: 0,
            SupplyTons: 0,
            ConstructionTicksRemaining: null,
            BuildCityId: null,
            CarriedArmyId: null,
            CoveredTileCode: 0);

        var mercenaryArmy = new ArmyState(
            Id: "rome-balance-mercenaries",
            Nation: "rome",
            X: romeCity.X,
            Y: romeCity.Y,
            Moves: 1,
            Morale: 50,
            Money: 1000,
            SupplyTons: 0,
            CoveredTileCode: 0,
            AboardFleetId: null,
            Units: ValueList.Of(new UnitSlot(
                MercenaryLabel: 1, UnitTypeId: "light_infantry", Troops: 1000, Quality: 5, Name: "Balance Mercenaries")));

        before = before with
        {
            Fleets = ValueList.From(before.Fleets.Append(fleet)),
            Armies = ValueList.From(before.Armies.Append(mercenaryArmy)),
        };

        var sheet = BalanceSheet.For(before, "rome", ruleset);
        var rome = before.NationById("rome")!;

        Assert.True(sheet.ShipUpkeep > 0);
        Assert.True(sheet.MercenariesPay > 0);

        Assert.Equal(TaxIncome.Compute(rome.TaxBase, rome.TaxRatePercent, ruleset), sheet.TaxIncome);
        Assert.Equal(NationTreasuryCredit.TaxBaseQuarterShare(rome, ruleset), sheet.TaxBaseQuarterShare);
        Assert.Equal(NationTreasuryCredit.TradeIncome(rome, before, ruleset), sheet.TradeIncome);
        Assert.Equal(sheet.TaxIncome + sheet.TaxBaseQuarterShare + sheet.TradeIncome, sheet.IncomeTotal);

        var after = FireQuarter(before);
        var romeAfter = after.NationById("rome")!;
        Assert.Equal(romeAfter.Treasury - rome.Treasury, sheet.IncomeTotal - sheet.ExpenditureTotal);
    }

    // ---- Done-when 3 ----

    /// <summary>
    /// The debt-limit term equals <c>min(wealth / 500, 20000)</c> and equals the value
    /// <see cref="Deposition"/> tests the treasury against, on both sides of the cap.
    /// </summary>
    [Fact]
    public void DebtLimit_IsTheCappedWealthTermAndTheDepositionBoundary()
    {
        var ruleset = Classical.Ruleset;
        var divisor = ruleset.Economy.DebtWealthDivisor;
        var cap = -ruleset.Economy.DebtTreasuryFloor;
        var state = ClassicalOnTheLastWeekBeforeAQuarter();

        foreach (var wealth in new[] { (cap - 1) * divisor, (cap + 1) * divisor })
        {
            var withWealth = state with
            {
                Nations = ValueList.From(state.Nations.Select(nation => nation.Id == "rome"
                    ? nation with { Wealth = wealth }
                    : nation)),
            };

            var rome = withWealth.NationById("rome")!;
            var sheet = BalanceSheet.For(withWealth, "rome", ruleset);

            Assert.Equal(Math.Min(wealth / divisor, cap), sheet.DebtLimit);
            Assert.Equal(Deposition.DebtLimit(rome, ruleset), sheet.DebtLimit);
        }

        // The figure Deposition tests against is exactly this limit: at treasury = -limit the nation is
        // not in debt, one talent past it is.
        var wealthy = state.NationById("rome")! with { Wealth = (cap + 1) * divisor, Unity = 600 };
        var limit = Deposition.DebtLimit(wealthy, ruleset);
        Assert.Equal(cap, limit);
        Assert.False(Deposition.InDebt(wealthy with { Treasury = -limit }, ruleset));
        Assert.True(Deposition.InDebt(wealthy with { Treasury = -limit - 1 }, ruleset));
    }

    // ---- Done-when 4 ----

    private static readonly Regex ForbiddenLiterals = new(@"(?<!\w)(7|20000|500)(?!\w)", RegexOptions.Compiled);

    /// <summary>
    /// A source scan of <c>BalanceSheet.cs</c> finds none of the city-upkeep, wealth or debt literals:
    /// those terms come from the shared functions or the ruleset.
    /// </summary>
    [Fact]
    public void BalanceSheetSource_ContainsNoHardcodedNumbers()
    {
        var path = Path.Combine(
            ModelTestPaths.RepositoryRoot, "src", "IC2.Engine", "Economy", "BalanceSheet.cs");
        Assert.True(File.Exists(path), $"Expected '{path}' to exist.");

        var offenders = ForbiddenLiterals.Matches(File.ReadAllText(path))
            .Select(match => match.Value)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "BalanceSheet.cs contains hardcoded gameplay literal(s): " + string.Join(", ", offenders));
    }

    // ---- Done-when 5 ----

    /// <summary>
    /// <c>balance</c> prints every line of the sheet, and the state is unchanged after it, asserted by
    /// serialising the state before and after.
    /// </summary>
    [Fact]
    public void BalanceVerb_PrintsEveryLineOfTheSheet_AndLeavesTheStateUnchanged()
    {
        var classical = Classical;
        var session = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario,
            seedOverride: null, humanSeatNationId: "rome");

        var before = GameJson.Serialize(session.State);
        var output = session.Submit("balance");
        var after = GameJson.Serialize(session.State);

        Assert.Equal(before, after);

        var labels = new[]
        {
            "Balance sheet for Rome",
            "Income:",
            "Tax income:",
            "Tax base quarter share:",
            "Trade income:",
            "Income total:",
            "Expenditure:",
            "City and wealth upkeep:",
            "Ship upkeep:",
            "Recruitment slot upkeep:",
            "Regulars' upkeep:",
            "Mercenaries' pay:",
            "Expenditure total:",
            "Treasury:",
            "Debt limit:",
        };

        foreach (var label in labels)
        {
            Assert.Contains(output.Lines, line => line.Contains(label, StringComparison.Ordinal));
        }
    }
}
