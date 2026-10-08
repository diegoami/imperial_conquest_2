using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Naval;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T109 (<c>docs/tasks/T109.md</c>) Done-when 1, the four Strategy dialogs' Godot-free model: the
/// Taxation preview's income for Rome matches <see cref="TaxIncome.Compute"/> at 15% and 20%; each
/// Build fleet refusal is pinned on a state built for it (plus the first refusal for Dacia and
/// Galatia on the shipped start); the Build fleet ship range and cost come from the ruleset; the
/// <c>order-fleet</c> line the dialog composes reaches the engine's own handler.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two refusals come from a state, the rest from the shipped world.</strong> "Only nations
/// with coastal cities can build fleets" and "You do not have a free coastal city at this time" are
/// arranged on a scripted state so the assertion names the exact path: every assertion names the
/// reason, not a tautology. The same refusal for Dacia and Galatia is read off the shipped
/// <c>classical-mediterranean</c> start — the world whose own data the corpus cites
/// (<c>2026-10-02-start-as-each-nation.md</c>, table 1, row 10 and row 12), so a change to the world
/// moves the expectation with it.
/// </para>
/// <para>
/// <strong>The tax preview asserts against the engine's own formula, not a corpus value.</strong>
/// "Pin … against <c>TaxIncome</c>" is the brief's wording: the test calls <see cref="TaxIncome.Compute"/>
/// and asserts the model's preview equals it, so a ruleset change to the divisor moves the test
/// with the engine. The two known data points
/// (<c>tests/fixtures/corpus.json</c> <c>tax.romeIncomeAt15Percent</c> = 366 and the 20% value = 488)
/// are reproduced by 2,440 — a value <c>decompiled-fleet-tax-and-mercenary-formulas.md</c> solved
/// from those two observations, and a third independent number this test does not need to assert.
/// </para>
/// </remarks>
public sealed class StrategyDialogModelsTests
{
    private const string RomeId = "rome";
    private const string DaciaId = "dacia";
    private const string GalatiaId = "galatia";
    private const string MediaId = "media";

    private static ResolvedScenario Classical() =>
        GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");

    private static GameSession RomeSession(ulong seed = 1)
    {
        var classical = Classical();
        return new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: seed, humanSeatNationId: RomeId);
    }

    private static GameSession NationSession(string nationId, ulong seed = 1)
    {
        var classical = Classical();
        return new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: seed, humanSeatNationId: nationId);
    }

    /// <summary>
    /// Done-when 1, the tax preview's first half: at the nation's current rate the dialog shows the
    /// engine's own <see cref="TaxIncome.Compute"/> value, not a restated formula. Rome's tax base is
    /// read from the live state.
    /// </summary>
    [Fact]
    public void The_tax_preview_for_Rome_at_the_current_rate_equals_TaxIncome()
    {
        var session = RomeSession();
        var nation = session.State.NationById(RomeId)!;
        var model = TaxationDialogModel.ForActiveNation(session.State, session.Ruleset);

        Assert.Equal(nation.TaxRatePercent, model.CurrentRate);
        Assert.Equal(TaxIncome.Compute(nation.TaxBase, nation.TaxRatePercent, session.Ruleset), model.CurrentIncome);
    }

    /// <summary>
    /// Done-when 1, the tax preview's second half: at 15% and 20% the dialog shows the same figure
    /// the engine's <see cref="TaxIncome.Compute"/> returns for Rome, so the ruleset's
    /// <c>TaxRateDivisor</c> (100 in the shipped ruleset) and Rome's tax base move the dialog and the
    /// engine together.
    /// </summary>
    [Theory]
    [InlineData(15)]
    [InlineData(20)]
    public void The_tax_preview_for_Rome_at_15_and_20_percent_equals_TaxIncome(int percent)
    {
        var session = RomeSession();
        var nation = session.State.NationById(RomeId)!;
        var model = TaxationDialogModel.ForActiveNation(session.State, session.Ruleset);

        Assert.Equal(TaxIncome.Compute(nation.TaxBase, percent, session.Ruleset), model.IncomeFor(percent));
    }

    /// <summary>
    /// The slider's inclusive bounds are the ruleset's <c>economy.taxRateMinPercent</c> /
    /// <c>taxRateMaxPercent</c> (0 and 40 in the shipped ruleset), and the page-key clamp
    /// (<see cref="TaxationDialogModel.Clamp"/>) honours them both.
    /// </summary>
    [Fact]
    public void The_tax_slider_uses_the_ruleset_bounds_and_clamps_to_them()
    {
        var session = RomeSession();
        var model = TaxationDialogModel.ForActiveNation(session.State, session.Ruleset);

        Assert.Equal(session.Ruleset.Economy.TaxRateMinPercent, model.MinimumRate);
        Assert.Equal(session.Ruleset.Economy.TaxRateMaxPercent, model.MaximumRate);
        Assert.Equal(model.MinimumRate, model.Clamp(model.MinimumRate - 5));
        Assert.Equal(model.MaximumRate, model.Clamp(model.MaximumRate + 5));
    }

    /// <summary>
    /// The OK button composes <c>set-tax &lt;percent&gt;</c> with the clamped rate — the engine's own
    /// <see cref="IC2.Engine.Economy.Commands.SetTaxRateCommandHandler"/> reads the rate back from the
    /// second token and range-checks it against the same ruleset bounds.
    /// </summary>
    [Fact]
    public void The_tax_OK_composes_set_tax_with_the_clamps_value()
    {
        var session = RomeSession();
        var model = TaxationDialogModel.ForActiveNation(session.State, session.Ruleset);

        Assert.Equal("set-tax 25", model.SetTaxLine(25));
        Assert.Equal("set-tax 0", model.SetTaxLine(-1));
        Assert.Equal("set-tax 40", model.SetTaxLine(99));
    }

    /// <summary>
    /// Done-when 1, the "Only nations with coastal cities can build fleets" refusal on a scripted
    /// state whose nation owns no coastal city. The state is built by hand, not by playing the
    /// shipped world, so the assertion names the path it pins.
    /// </summary>
    [Fact]
    public void Build_fleet_refuses_with_no_coastal_cities_on_a_scripted_state()
    {
        var session = RomeSession();
        // Drop every Rome-owned city from the live state — Rome owns 25 of them, all on Roman tiles
        // some of which are coastal, so removing them gives Rome zero cities and therefore zero
        // coastal cities. The dialog's first refusal fires on its own.
        var withoutRome = session.State.Cities.Where(c => !string.Equals(c.Owner, RomeId, StringComparison.Ordinal));
        var state = session.State with
        {
            Cities = ValueList.From(withoutRome),
        };
        var model = BuildFleetDialogModel.ForActiveNation(state, session.Ruleset, session.World);

        Assert.Equal(StrategyDialogModels.NoCoastalCitiesRefusal, model.PreOpenRefusalMessage);
        Assert.Empty(model.FreeCoastalCities);
    }

    /// <summary>
    /// Done-when 1, the "You do not have a free coastal city at this time" refusal on a scripted
    /// state whose every coastal city is already building a fleet.
    /// </summary>
    [Fact]
    public void Build_fleet_refuses_with_no_free_coastal_city_when_every_port_is_busy()
    {
        var session = RomeSession();
        var romanCoastal = session.State.Cities
            .Where(c => string.Equals(c.Owner, RomeId, StringComparison.Ordinal))
            .Where(c => CoastalCity.IsCoastal(c, session.World))
            .ToList();
        Assert.NotEmpty(romanCoastal);

        // Mark every Roman coastal city as busy: each gets a fleet under construction. The dialog's
        // "free coastal city" list becomes empty, so the second refusal fires.
        var busy = new List<FleetState>();
        for (var i = 0; i < romanCoastal.Count; i++)
        {
            busy.Add(new FleetState(
                Id: $"t109-busy-{i}", Nation: RomeId, X: 0, Y: 0, Moves: 0, Ships: 10, ConditionPercent: 0,
                Money: 0, SupplyTons: 0,
                ConstructionTicksRemaining: session.Ruleset.Naval.ConstructionTicks,
                BuildCityId: romanCoastal[i].Id, CarriedArmyId: null, CoveredTileCode: null));
        }

        var state = session.State with
        {
            Fleets = ValueList.From(session.State.Fleets.Concat(busy)),
        };
        var model = BuildFleetDialogModel.ForActiveNation(state, session.Ruleset, session.World);

        Assert.NotEmpty(model.CoastalCities);
        Assert.Empty(model.FreeCoastalCities);
        Assert.Equal(StrategyDialogModels.NoFreeCoastalCityRefusal, model.PreOpenRefusalMessage);
    }

    /// <summary>
    /// Done-when 1, the third refusal — <em>"You cannot build a fleet at this time."</em> — on a
    /// scripted state whose fleet table holds the original's full 99 entries
    /// (<see cref="StrategyDialogModels.FleetTableCapacity"/>,
    /// <c>docs/investigations/original-ui-command-audit.md</c> §1.3: "the fleet table is full at 99").
    /// The nation still owns a free coastal city, so the first two refusals do not fire; only the
    /// catch-all can answer.
    /// </summary>
    [Fact]
    public void Build_fleet_refuses_with_the_catch_all_when_the_fleet_table_is_full()
    {
        var session = RomeSession();
        var fleets = new List<FleetState>();
        for (var i = 0; i < StrategyDialogModels.FleetTableCapacity; i++)
        {
            fleets.Add(new FleetState(
                Id: $"t109-full-{i}", Nation: RomeId, X: 0, Y: 0, Moves: 0, Ships: 10, ConditionPercent: 100,
                Money: 0, SupplyTons: 0, ConstructionTicksRemaining: null, BuildCityId: null,
                CarriedArmyId: null, CoveredTileCode: null));
        }

        var state = session.State with
        {
            Fleets = ValueList.From(fleets),
        };
        var model = BuildFleetDialogModel.ForActiveNation(state, session.Ruleset, session.World);

        Assert.NotEmpty(model.CoastalCities);
        Assert.NotEmpty(model.FreeCoastalCities);
        Assert.Equal(StrategyDialogModels.FleetTableCapacity, state.Fleets.Count);
        Assert.Equal(StrategyDialogModels.CannotBuildFleetRefusal, model.PreOpenRefusalMessage);
    }

    /// <summary>
    /// Done-when 1, the "Only nations with coastal cities can build fleets" refusal for Dacia on the
    /// shipped <c>classical-mediterranean</c> start — the same refusal the report lists
    /// (<c>2026-10-02-start-as-each-nation.md</c>, table 1, row 10).
    /// </summary>
    [Fact]
    public void Build_fleet_refuses_Dacia_with_no_coastal_cities_on_the_shipped_start()
    {
        var session = NationSession(DaciaId);
        var model = BuildFleetDialogModel.ForActiveNation(session.State, session.Ruleset, session.World);

        Assert.Empty(model.CoastalCities);
        Assert.Equal(StrategyDialogModels.NoCoastalCitiesRefusal, model.PreOpenRefusalMessage);
    }

    /// <summary>
    /// Done-when 1, the "Only nations with coastal cities can build fleets" refusal for Galatia on the
    /// shipped <c>classical-mediterranean</c> start — the same refusal the report lists (row 12).
    /// </summary>
    [Fact]
    public void Build_fleet_refuses_Galatia_with_no_coastal_cities_on_the_shipped_start()
    {
        var session = NationSession(GalatiaId);
        var model = BuildFleetDialogModel.ForActiveNation(session.State, session.Ruleset, session.World);

        Assert.Empty(model.CoastalCities);
        Assert.Equal(StrategyDialogModels.NoCoastalCitiesRefusal, model.PreOpenRefusalMessage);
    }

    /// <summary>
    /// The shipped world has Media with two Caspian cities that the engine's coastal test accepts
    /// (bug
    /// <see href="https://github.com/diegoami/imperial_conquest_2/issues/600">#600</see>). The dialog
    /// therefore offers Build fleet, never the first refusal, in the start the report pins.
    /// </summary>
    [Fact]
    public void Build_fleet_offers_Media_a_fleet_despite_the_originals_refusal()
    {
        var session = NationSession(MediaId);
        var model = BuildFleetDialogModel.ForActiveNation(session.State, session.Ruleset, session.World);

        Assert.NotEmpty(model.CoastalCities);
        Assert.Null(model.PreOpenRefusalMessage);
    }

    /// <summary>
    /// Done-when 1, the ship range and the per-ship cost: the dialog's minimum and maximum are the
    /// ruleset's <c>OrderMinShips</c> and <c>OrderMaxShips</c> (10 and 100 in the shipped ruleset),
    /// and the initial cost is <c>ships × BuildCostPerShip</c> (10 per ship, so 100 ships cost 1,000).
    /// </summary>
    [Fact]
    public void The_build_fleet_range_and_cost_come_from_the_ruleset()
    {
        var session = RomeSession();
        var model = BuildFleetDialogModel.ForActiveNation(session.State, session.Ruleset, session.World);

        Assert.Equal(session.Ruleset.Naval.OrderMinShips, model.MinShips);
        Assert.Equal(session.Ruleset.Naval.OrderMaxShips, model.MaxShips);
        Assert.Equal(100, model.InitialCostFor(10));
        Assert.Equal(1_000, model.InitialCostFor(100));
    }

    /// <summary>
    /// The OK button composes <c>order-fleet &lt;city&gt; &lt;ships&gt; &lt;new-fleet&gt;</c> with the
    /// engine's own verb, the chosen city id, the ship count and the new fleet id. The engine's
    /// <see cref="IC2.Engine.Naval.Commands.OrderFleetCommandHandler"/> reads the tokens back and
    /// applies its own refusals.
    /// </summary>
    [Fact]
    public void The_build_fleet_OK_composes_order_fleet_with_its_three_arguments()
    {
        var session = RomeSession();
        var model = BuildFleetDialogModel.ForActiveNation(session.State, session.Ruleset, session.World);
        var city = model.FreeCoastalCities[0];

        var line = model.OrderFleetLine(20, city.Id, "t109-new-fleet");

        Assert.Equal($"order-fleet {city.Id} 20 t109-new-fleet", line);
    }

    /// <summary>
    /// The dialog's "under construction" rows enumerate this nation's fleets whose
    /// <see cref="FleetState.ConstructionTicksRemaining"/> is non-null, with the build city's name
    /// and the ticks left. The "weeks" figure is the engine's own countdown, never a step of weeks
    /// per turn.
    /// </summary>
    [Fact]
    public void The_build_fleet_under_construction_rows_list_this_nations_pending_fleets()
    {
        var session = RomeSession();
        var ruleset = session.Ruleset;
        var coastalRome = session.State.Cities
            .Where(c => string.Equals(c.Owner, RomeId, StringComparison.Ordinal))
            .First(c => CoastalCity.IsCoastal(c, session.World));
        var pending = new FleetState(
            Id: "t109-pending", Nation: RomeId, X: 0, Y: 0, Moves: 0, Ships: 10, ConditionPercent: 0,
            Money: 0, SupplyTons: 0, ConstructionTicksRemaining: ruleset.Naval.ConstructionTicks,
            BuildCityId: coastalRome.Id, CarriedArmyId: null, CoveredTileCode: null);
        var state = session.State with
        {
            Fleets = ValueList.From(session.State.Fleets.Append(pending)),
        };
        var model = BuildFleetDialogModel.ForActiveNation(state, ruleset, session.World);

        var row = Assert.Single(model.UnderConstructionFleets);
        Assert.Equal("t109-pending", row.FleetId);
        Assert.Equal(10, row.Ships);
        Assert.Equal(ruleset.Naval.ConstructionTicks, row.WeeksRemaining);
        Assert.Equal(coastalRome.Name, row.CityName);
    }
}
