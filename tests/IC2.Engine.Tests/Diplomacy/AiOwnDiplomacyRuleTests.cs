using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// T82 (#359, bug #357): <c>AiOwnDiplomacyRule</c>'s own gates -- the deterministic half of
/// <c>FUN_0044FB7C</c>, <c>decompiled-ai-offers-to-human-seats.md</c> §1a <strong>[confirmed]</strong>.
/// Each test pins one gate so that deleting it (loosening the condition back out) fails the test, per
/// the task entry's Done-when 3.
/// </summary>
/// <remarks>
/// Uses its own small hand-built <see cref="World"/>, not the shared two-nation toy world or
/// <see cref="DiplomacyTestbed"/>'s nation ids: <see cref="NeighbourGeography"/> only ever considers
/// nations actually present in <see cref="World.Nations"/>, and this file needs three or four of them
/// laid out with a wide-enough shared border to clear <c>NeighbourGeography</c>'s own 10-tile minimum
/// (see that type's own remarks) -- a 44 (wide) × 14 (tall) strip, split into vertical bands one per
/// nation, giving every adjacent pair a 14-tile border.
/// </remarks>
public sealed class AiOwnDiplomacyRuleTests
{
    private const int Width = 44;
    private const int Height = 14;

    private static Ruleset Ruleset => DiplomacyTestbed.Ruleset;

    /// <summary>One nation's fixture spec, every field defaulted except the ones a test varies.</summary>
    private sealed record NationSpec(
        string Id,
        int Band,
        int Unity = 500,
        int Wealth = 0,
        int TaxBase = 0,
        int MobilizedPercent = 0,
        int Cities = 0,
        bool Eliminated = false,
        SeatControl Control = SeatControl.Ai);

    // ---- IsBusy ----

    [Fact]
    public void Not_at_war_low_mobilization_not_winter_is_not_busy()
    {
        var state = Fixture(new NationSpec("me", 0), new NationSpec("j", 1));

        Assert.False(AiOwnDiplomacyRule.IsBusy(state, Ruleset, "me"));
    }

    [Fact]
    public void At_war_with_anyone_is_busy()
    {
        var state = Fixture(new NationSpec("me", 0), new NationSpec("j", 1));
        state = AtWar(state, "me", "j");

        Assert.True(AiOwnDiplomacyRule.IsBusy(state, Ruleset, "me"));
    }

    [Fact]
    public void Mobilization_over_the_threshold_is_busy()
    {
        var threshold = Ruleset.Diplomacy.AiOwnDiplomacy.BusyMobilizationThreshold;
        var state = Fixture(new NationSpec("me", 0, MobilizedPercent: threshold + 1), new NationSpec("j", 1));

        Assert.True(AiOwnDiplomacyRule.IsBusy(state, Ruleset, "me"));
    }

    [Fact]
    public void Mobilization_at_the_threshold_is_not_busy()
    {
        var threshold = Ruleset.Diplomacy.AiOwnDiplomacy.BusyMobilizationThreshold;
        var state = Fixture(new NationSpec("me", 0, MobilizedPercent: threshold), new NationSpec("j", 1));

        Assert.False(AiOwnDiplomacyRule.IsBusy(state, Ruleset, "me"));
    }

    [Fact]
    public void Winter_is_busy()
    {
        var state = Fixture(new NationSpec("me", 0), new NationSpec("j", 1));
        state = state with { Calendar = state.Calendar with { SeasonIndex = Ruleset.Calendar.SeasonsPerYear - 1 } };

        Assert.True(AiOwnDiplomacyRule.IsBusy(state, Ruleset, "me"));
    }

    // ---- IsProtected ----

    [Fact]
    public void A_candidate_with_no_allies_is_not_protected()
    {
        var state = Fixture(new NationSpec("me", 0, Cities: 1), new NationSpec("k", 1, Cities: 1));

        Assert.False(AiOwnDiplomacyRule.IsProtected(state, Ruleset, "k", "me"));
    }

    [Fact]
    public void A_candidate_whose_ally_pushes_combined_cities_over_the_reference_is_protected()
    {
        var state = Fixture(
            new NationSpec("me", 0, Cities: 3), new NationSpec("k", 1, Cities: 1), new NationSpec("a", 2, Cities: 3));
        state = AllyOf(state, "k", "a");

        // cities[k] (1) + cities[a] (3) = 4 > cities[me] (3).
        Assert.True(AiOwnDiplomacyRule.IsProtected(state, Ruleset, "k", "me"));
    }

    [Fact]
    public void A_candidate_whose_ally_does_not_push_combined_cities_over_the_reference_is_not_protected()
    {
        var state = Fixture(
            new NationSpec("me", 0, Cities: 10), new NationSpec("k", 1, Cities: 1), new NationSpec("a", 2, Cities: 1));
        state = AllyOf(state, "k", "a");

        // cities[k] (1) + cities[a] (1) = 2, not > cities[me] (10).
        Assert.False(AiOwnDiplomacyRule.IsProtected(state, Ruleset, "k", "me"));
    }

    // ---- BestWarTarget ----

    [Fact]
    public void A_busy_nation_has_no_war_target()
    {
        var world = TestWorld("me", "k");
        var state = Fixture(new NationSpec("me", 0, Wealth: 40000, Unity: 500), new NationSpec("k", 1, Wealth: 1000, Unity: 100));
        state = AtWar(state, "me", "k");

        Assert.Null(AiOwnDiplomacyRule.BestWarTarget(state, Ruleset, world, "me"));
    }

    [Fact]
    public void A_neighbouring_unprotected_nation_with_a_high_enough_ratio_is_the_war_target()
    {
        // P(me) = (40000/20000) * (500/100) = 10; P(k) = (1000/20000) * (100/100) = 0 (integer division).
        // ratio = 8 * 10 / max(1, 0) = 80, comfortably over the base of 10.
        var world = TestWorld("me", "k");
        var state = Fixture(new NationSpec("me", 0, Wealth: 40000, Unity: 500), new NationSpec("k", 1, Wealth: 1000, Unity: 100));

        Assert.Equal("k", AiOwnDiplomacyRule.BestWarTarget(state, Ruleset, world, "me"));
    }

    [Fact]
    public void A_protected_neighbour_is_never_the_war_target()
    {
        var world = TestWorld("me", "k", "a");
        var state = Fixture(
            new NationSpec("me", 0, Wealth: 40000, Unity: 500, Cities: 3),
            new NationSpec("k", 1, Wealth: 1000, Unity: 100, Cities: 1),
            new NationSpec("a", 2, Wealth: 1000, Unity: 100, Cities: 3));
        state = AllyOf(state, "k", "a");

        Assert.Null(AiOwnDiplomacyRule.BestWarTarget(state, Ruleset, world, "me"));
    }

    [Fact]
    public void A_non_neighbouring_nation_is_never_the_war_target()
    {
        // Four nations in a row (me, buffer, buffer2, k): me and k share no border. The two buffers
        // match me's own power (ratio 8, at the base) so neither is a valid target itself -- k is the
        // only nation weak enough to clear the ratio gate, isolating the neighbour test.
        var world = TestWorld("me", "buffer", "buffer2", "k");
        var state = Fixture(
            new NationSpec("me", 0, Wealth: 40000, Unity: 500), new NationSpec("buffer", 1, Wealth: 40000, Unity: 500),
            new NationSpec("buffer2", 2, Wealth: 40000, Unity: 500), new NationSpec("k", 3, Wealth: 1000, Unity: 100));

        Assert.Null(AiOwnDiplomacyRule.BestWarTarget(state, Ruleset, world, "me"));
    }

    [Fact]
    public void A_ratio_at_or_below_the_base_is_never_the_war_target()
    {
        // Equal power on both sides: ratio = 8 * P / P = 8, at or under the base of 10.
        var world = TestWorld("me", "k");
        var state = Fixture(new NationSpec("me", 0, Wealth: 10000, Unity: 500), new NationSpec("k", 1, Wealth: 10000, Unity: 500));

        Assert.Null(AiOwnDiplomacyRule.BestWarTarget(state, Ruleset, world, "me"));
    }

    [Fact]
    public void An_already_eliminated_neighbour_is_never_the_war_target()
    {
        var world = TestWorld("me", "k");
        var state = Fixture(
            new NationSpec("me", 0, Wealth: 40000, Unity: 500),
            new NationSpec("k", 1, Wealth: 1000, Unity: 100, Eliminated: true));

        Assert.Null(AiOwnDiplomacyRule.BestWarTarget(state, Ruleset, world, "me"));
    }

    // ---- FindAlliancePartner ----

    [Fact]
    public void The_first_ai_at_war_with_a_shared_neighbour_and_room_for_more_wars_is_the_partner()
    {
        var world = TestWorld("me", "j", "m");
        var state = Fixture(new NationSpec("me", 0, Cities: 5), new NationSpec("j", 1, Cities: 1), new NationSpec("m", 2, Cities: 1));
        state = AtWar(state, "m", "j");

        Assert.Equal("m", AiOwnDiplomacyRule.FindAlliancePartner(state, Ruleset, world, "me"));
    }

    [Fact]
    public void A_human_at_war_with_a_shared_neighbour_is_never_the_partner()
    {
        var world = TestWorld("me", "j", "m");
        var state = Fixture(
            new NationSpec("me", 0, Cities: 5), new NationSpec("j", 1, Cities: 1),
            new NationSpec("m", 2, Cities: 1, Control: SeatControl.Human));
        state = AtWar(state, "m", "j");

        Assert.Null(AiOwnDiplomacyRule.FindAlliancePartner(state, Ruleset, world, "me"));
    }

    [Fact]
    public void A_partner_already_at_the_war_cap_is_never_chosen()
    {
        var cap = Ruleset.Diplomacy.AiOwnDiplomacy.AllianceMaxPartnerWars;
        var world = TestWorld("me", "j", "m", "x1");
        var state = Fixture(
            new NationSpec("me", 0, Cities: 5), new NationSpec("j", 1, Cities: 1), new NationSpec("m", 2, Cities: 1),
            new NationSpec("x1", 3, Cities: 1));
        state = AtWar(state, "m", "j");
        // Give m a second war so it reaches the cap -- x1 need not be m's own neighbour for this gate.
        state = AtWar(state, "m", "x1");

        Assert.True(cap <= 2);
        Assert.Null(AiOwnDiplomacyRule.FindAlliancePartner(state, Ruleset, world, "me"));
    }

    [Fact]
    public void The_shared_neighbour_must_not_out_city_the_pair()
    {
        var world = TestWorld("me", "j", "m");
        var state = Fixture(
            // cities[j] (10) is not < cities[me] (1) + cities[m] (1) = 2.
            new NationSpec("me", 0, Cities: 1), new NationSpec("j", 1, Cities: 10), new NationSpec("m", 2, Cities: 1));
        state = AtWar(state, "m", "j");

        Assert.Null(AiOwnDiplomacyRule.FindAlliancePartner(state, Ruleset, world, "me"));
    }

    [Fact]
    public void A_protected_shared_neighbour_is_never_a_partner_search_starting_point()
    {
        var world = TestWorld("me", "j", "m", "a");
        var state = Fixture(
            new NationSpec("me", 0, Cities: 3), new NationSpec("j", 1, Cities: 1), new NationSpec("m", 2, Cities: 1),
            new NationSpec("a", 3, Cities: 3));
        state = AtWar(state, "m", "j");
        state = AllyOf(state, "j", "a");

        // j is protected relative to me: cities[j] (1) + cities[a] (3) = 4 > cities[me] (3).
        Assert.Null(AiOwnDiplomacyRule.FindAlliancePartner(state, Ruleset, world, "me"));
    }

    // ---- EligibleTradePartners ----

    [Fact]
    public void An_ai_nation_at_peace_under_both_caps_is_eligible()
    {
        var state = Fixture(new NationSpec("me", 0), new NationSpec("k", 1));

        Assert.Equal(new[] { "k" }, AiOwnDiplomacyRule.EligibleTradePartners(state, Ruleset, "me"));
    }

    [Fact]
    public void A_human_nation_at_peace_is_never_an_eligible_trade_partner()
    {
        var state = Fixture(new NationSpec("me", 0), new NationSpec("k", 1, Control: SeatControl.Human));

        Assert.Empty(AiOwnDiplomacyRule.EligibleTradePartners(state, Ruleset, "me"));
    }

    [Fact]
    public void A_nation_not_at_peace_is_never_an_eligible_trade_partner()
    {
        var state = Fixture(new NationSpec("me", 0), new NationSpec("k", 1));
        state = AtWar(state, "me", "k");

        Assert.Empty(AiOwnDiplomacyRule.EligibleTradePartners(state, Ruleset, "me"));
    }

    [Fact]
    public void A_partner_already_at_its_own_trade_cap_is_never_eligible()
    {
        var cap = Ruleset.Diplomacy.MaxTradePartners;
        var specs = new List<NationSpec> { new("me", 0), new("k", 1) };
        for (var i = 0; i < cap; i++)
        {
            specs.Add(new NationSpec($"o{i}", i + 2));
        }

        var state = Fixture(specs.ToArray());
        for (var i = 0; i < cap; i++)
        {
            state = state with { Relations = state.Relations.WithRelation("k", $"o{i}", Ruleset.Diplomacy.StateCodes.Trade) };
        }

        Assert.DoesNotContain("k", AiOwnDiplomacyRule.EligibleTradePartners(state, Ruleset, "me"));
    }

    // ---- FindTradeSwap ----

    [Fact]
    public void A_richer_ai_at_peace_swaps_in_for_a_poorer_current_partner()
    {
        var state = Fixture(
            new NationSpec("me", 0, TaxBase: 0), new NationSpec("poor", 1, TaxBase: 100),
            new NationSpec("rich", 2, TaxBase: 500));
        state = state with
        {
            Relations = state.Relations.WithRelation("me", "poor", Ruleset.Diplomacy.StateCodes.Trade),
        };

        var swap = AiOwnDiplomacyRule.FindTradeSwap(state, Ruleset, "me");

        Assert.Equal(("poor", "rich"), swap);
    }

    [Fact]
    public void A_candidate_no_richer_than_the_current_partner_is_never_a_swap()
    {
        var state = Fixture(
            new NationSpec("me", 0, TaxBase: 0), new NationSpec("partner", 1, TaxBase: 300),
            new NationSpec("notRicher", 2, TaxBase: 300));
        state = state with
        {
            Relations = state.Relations.WithRelation("me", "partner", Ruleset.Diplomacy.StateCodes.Trade),
        };

        Assert.Null(AiOwnDiplomacyRule.FindTradeSwap(state, Ruleset, "me"));
    }

    // ---- Fixture builders ----

    private static World TestWorld(params string[] nationIds)
    {
        var bandWidth = Width / nationIds.Length;
        var nationDefs = nationIds
            .Select(id => new NationDefinition(id, id, "#000000", id, id, 0, 500, 10000, 1000, 10, 0, 100))
            .ToArray();
        var cities = nationIds
            .Select((id, band) => new CityDefinition(
                id, id, (band * bandWidth) + (bandWidth / 2), Height / 2, id, id,
                80, 0, 50, 10, 10, 0, ValueList<UnitSlot>.Empty))
            .ToArray();

        return new World(
            GameDataSchema.CurrentVersion,
            "ai-own-diplomacy-test-world",
            "AI own diplomacy test world",
            Width,
            Height,
            new TerrainGrid(TerrainEncoding.RunLength, Runs: ValueList<TerrainRun>.Of(new TerrainRun(2, Width * Height))),
            ValueList<TileType>.Of(new TileType("plain", 2, "Plain", true, false)),
            ValueList<NationDefinition>.Of(nationDefs),
            ValueList<CityDefinition>.Of(cities),
            ValueList<StartingArmy>.Empty,
            ValueList<StartingFleet>.Empty,
            ValueList<string>.Of(nationIds));
    }

    private static GameState Fixture(params NationSpec[] specs)
    {
        var nations = specs
            .Select(s => DiplomacyTestbed.Nation(s.Id, s.Id, control: s.Control, unity: s.Unity, wealth: s.Wealth, taxBase: s.TaxBase)
                with { MobilizedPercent = s.MobilizedPercent, Eliminated = s.Eliminated })
            .ToArray();

        var cities = specs
            .SelectMany(s => Enumerable.Range(0, s.Cities).Select(i => new CityState(
                $"{s.Id}-city-{i}", $"{s.Id} City {i}", X: 0, Y: 0, Owner: s.Id, Allegiance: s.Id,
                Loyalty: 80, SupplyTons: 0, FortificationCode: 50, PopulationThousands: 10,
                MaxPopulationThousands: 10, Tribute: 0, UnderSiege: false, Garrison: ValueList<UnitSlot>.Empty)))
            .ToArray();

        var ids = ValueList.From(nations.Select(n => n.Id));
        var peace = Ruleset.Diplomacy.StateCodes.Peace;

        return new GameState(
            SchemaVersion: 1,
            WorldId: "ai-own-diplomacy-test-world",
            RulesetId: Ruleset.Id,
            ScenarioId: "ai-own-diplomacy-test-scenario",
            Calendar: new CalendarState(Week: Ruleset.Calendar.StartWeek, SeasonIndex: 0, YearBc: 270, TurnIndex: 0),
            TurnOrder: ids,
            ActiveSeatIndex: 0,
            RandomSeed: 12345UL,
            Nations: ValueList.From(nations),
            Cities: ValueList.From(cities),
            Armies: ValueList<ArmyState>.Empty,
            Fleets: ValueList<FleetState>.Empty,
            MercenaryPool: ValueList<MercenaryPoolSlot>.Empty,
            Relations: DiplomaticRelations.Uniform(ids, peace),
            NewsLog: NewsLog.Empty,
            PendingOffer: null);
    }

    private static GameState AtWar(GameState state, string a, string b) =>
        state with { Relations = state.Relations.WithRelation(a, b, Ruleset.Diplomacy.StateCodes.War) };

    private static GameState AllyOf(GameState state, string a, string b) =>
        state with { Relations = state.Relations.WithRelation(a, b, Ruleset.Diplomacy.StateCodes.Alliance) };
}
