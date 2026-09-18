using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T35 Model: nation tax base, recruitment slots, and the pending
/// diplomatic offer", Done-when 6: the quarterly treasury credit, one test per term, on a scripted
/// <see cref="GameState"/>.
/// </summary>
public sealed class NationTreasuryCreditTests
{
    private static readonly Ruleset Ruleset = EconomyTestbed.Ruleset;

    private static GameState MakeState(IReadOnlyList<NationState> nations, IReadOnlyList<CityState> cities)
    {
        var nationIds = ValueList.From(nations.Select(n => n.Id));
        return new GameState(
            SchemaVersion: GameDataSchema.CurrentVersion,
            WorldId: "test", RulesetId: "test", ScenarioId: "test",
            Calendar: new CalendarState(0, 0, 0, 0),
            TurnOrder: nationIds,
            ActiveSeatIndex: 0,
            RandomSeed: 0,
            Nations: ValueList.From(nations),
            Cities: ValueList.From(cities),
            Armies: ValueList<ArmyState>.Empty,
            Fleets: ValueList<FleetState>.Empty,
            MercenaryPool: ValueList<MercenaryPoolSlot>.Empty,
            Relations: DiplomaticRelations.Uniform(nationIds, Ruleset.Diplomacy.StateCodes.Peace),
            NewsLog: NewsLog.Empty,
            PendingOffer: null);
    }

    private static NationState MakeNation(string id, int taxBase, int taxRatePercent, int wealth) => new(
        Id: id, Name: id, ColorHex: "#000", LeaderName: "Leader", CapitalCityId: null,
        Control: SeatControl.Ai, Personality: null,
        Treasury: 0, Unity: 600, Wealth: wealth, TaxBase: taxBase, TaxRatePercent: taxRatePercent,
        MobilizedPercent: 0, Population: 100, PopulationAtStart: 100, TreasuryAtStart: 0,
        CityCountAtStart: 0, RecruitmentSlots: ValueList<RecruitmentSlot>.Empty, Eliminated: false);

    private static CityState MakeCity(string id, string owner) => new(
        id, id, 0, 0, owner, owner, 80, 0, 0, 10, 10, 0, false, ValueList<UnitSlot>.Empty);

    /// <summary>The tax income term: <c>taxBase × taxRate / 100</c>.</summary>
    [Fact]
    public void TaxIncomeTerm()
    {
        var nation = MakeNation("a", taxBase: 1000, taxRatePercent: 15, wealth: 0);
        var state = MakeState(new[] { nation }, cities: Array.Empty<CityState>());

        // 1000*15/100 + 1000/4 - 0*7 - 0/20000 + 0 = 150 + 250 = 400.
        Assert.Equal(400, NationTreasuryCredit.Compute(nation, state, Ruleset));
    }

    /// <summary>The second term is <c>taxBase / 4</c>, not <c>mobilization / 4</c>.</summary>
    [Fact]
    public void TaxBaseQuarterShareTerm_IsTaxBaseNotMobilization()
    {
        var nation = MakeNation("a", taxBase: 1000, taxRatePercent: 0, wealth: 0) with { MobilizedPercent = 999 };
        var state = MakeState(new[] { nation }, cities: Array.Empty<CityState>());

        // 0 + 1000/4 - 0 - 0 + 0 = 250, regardless of the (deliberately absurd) mobilization value.
        Assert.Equal(250, NationTreasuryCredit.Compute(nation, state, Ruleset));
    }

    /// <summary>The city-count term is <c>cityCount × 7</c>, per city, not a flat charge.</summary>
    [Fact]
    public void CityCountTerm_IsPerCity()
    {
        var nation = MakeNation("a", taxBase: 0, taxRatePercent: 0, wealth: 0);
        var cities = new[] { MakeCity("c1", "a"), MakeCity("c2", "a"), MakeCity("c3", "a") };
        var state = MakeState(new[] { nation }, cities);

        Assert.Equal(-21, NationTreasuryCredit.Compute(nation, state, Ruleset));
    }

    /// <summary>The wealth term.</summary>
    [Fact]
    public void WealthTerm()
    {
        var nation = MakeNation("a", taxBase: 0, taxRatePercent: 0, wealth: 40_000);
        var state = MakeState(new[] { nation }, cities: Array.Empty<CityState>());

        Assert.Equal(-2, NationTreasuryCredit.Compute(nation, state, Ruleset));
    }

    /// <summary>
    /// The trade term: <c>Σ</c> over partners at trade or alliance of their tax base / 12. A partner at
    /// peace or war contributes nothing.
    /// </summary>
    [Fact]
    public void TradeIncomeTerm_SumsTradeAndAlliancePartnersOnly()
    {
        var a = MakeNation("a", taxBase: 0, taxRatePercent: 0, wealth: 0);
        var tradePartner = MakeNation("trade-partner", taxBase: 240, taxRatePercent: 0, wealth: 0);
        var alliancePartner = MakeNation("alliance-partner", taxBase: 120, taxRatePercent: 0, wealth: 0);
        var peacePartner = MakeNation("peace-partner", taxBase: 1_000_000, taxRatePercent: 0, wealth: 0);
        var warPartner = MakeNation("war-partner", taxBase: 1_000_000, taxRatePercent: 0, wealth: 0);

        var state = MakeState(new[] { a, tradePartner, alliancePartner, peacePartner, warPartner }, Array.Empty<CityState>());
        state = state with
        {
            Relations = state.Relations
                .WithRelation("a", "trade-partner", Ruleset.Diplomacy.StateCodes.Trade)
                .WithRelation("a", "alliance-partner", Ruleset.Diplomacy.StateCodes.Alliance)
                .WithRelation("a", "war-partner", Ruleset.Diplomacy.StateCodes.War),
        };

        // trade: 240/12 = 20; alliance: 120/12 = 10; peace and war partners contribute 0 regardless of
        // their (deliberately huge) tax base. Every other term is 0 for "a" itself.
        Assert.Equal(30, NationTreasuryCredit.Compute(a, state, Ruleset));
    }

    /// <summary>Every term together, hand-computed.</summary>
    [Fact]
    public void ComputesTheFullFormula_AllTermsTogether()
    {
        var a = MakeNation("a", taxBase: 1000, taxRatePercent: 15, wealth: 40_000);
        var partner = MakeNation("partner", taxBase: 240, taxRatePercent: 0, wealth: 0);
        var cities = new[] { MakeCity("c1", "a"), MakeCity("c2", "a"), MakeCity("c3", "a") };

        var state = MakeState(new[] { a, partner }, cities);
        state = state with { Relations = state.Relations.WithRelation("a", "partner", Ruleset.Diplomacy.StateCodes.Trade) };

        // 1000*15/100 + 1000/4 - 3*7 - 40000/20000 + 240/12 = 150 + 250 - 21 - 2 + 20 = 397.
        Assert.Equal(397, NationTreasuryCredit.Compute(a, state, Ruleset));
    }
}
