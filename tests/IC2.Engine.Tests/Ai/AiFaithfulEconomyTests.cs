using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T157 (#926): the AI's economy under <c>classical-faithful</c>, pinned on hand-built states. Every
/// boundary has a test that fails when it moves by one.
/// </summary>
public sealed class AiFaithfulEconomyTests
{
    private const string Me = "a";
    private const string Foe = "b";
    private const string Friend = "c";
    private const int CapX = 50;
    private const int CapY = 50;

    private static Ruleset Rules(bool allowDebt = true)
    {
        var toy = BattleCommandTestbed.ToyRuleset;
        return toy with
        {
            Flags = toy.Flags with
            {
                AiEconomy = AiEconomyModel.Original,
                UnaffordableRecruitAndFortify = allowDebt ? UnaffordableOrderPolicy.AllowDebt : UnaffordableOrderPolicy.Refuse,
            },
        };
    }

    private static int War => Rules().Diplomacy.StateCodes.War;

    private static int Alliance => Rules().Diplomacy.StateCodes.Alliance;

    private static CityState Cap(string id = "a-cap", int x = CapX, int y = CapY, string owner = Me, int fort = 0) =>
        CaptureFixtures.City(id, id, x, y, owner, owner, 80, fort, 10, 100, 10);

    private static ArmyState Army(string id, string nation, int x, int y, string type, int troops) =>
        CaptureFixtures.Army(id, nation, x, y, 80, CaptureFixtures.Unit(type, troops));

    private static GameState Build(
        IEnumerable<ArmyState> armies,
        IEnumerable<CityState>? cities = null,
        Func<NationState, NationState>? me = null,
        int? foeRelation = null,
        int? friendRelation = null,
        ValueList<RecruitmentSlot>? slots = null)
    {
        var mine = CaptureFixtures.Nation(Me, treasury: 100000, unity: 600, wealth: 10_000_000, capitalCityId: "a-cap");
        mine = mine with { RecruitmentSlots = slots ?? ValueList<RecruitmentSlot>.Empty };
        if (me is not null)
        {
            mine = me(mine);
        }

        var nations = new[]
        {
            mine,
            CaptureFixtures.Nation(Foe, capitalCityId: "b-cap"),
            CaptureFixtures.Nation(Friend, capitalCityId: "c-cap"),
        };
        var allCities = (cities ?? new[] { Cap() }).Concat(new[]
        {
            Cap("b-cap", 90, 90, Foe),
            Cap("c-cap", 5, 90, Friend),
        });
        var state = BattleCommandTestbed.StateWith(nations, allCities, armies);
        if (foeRelation is { } f)
        {
            state = state with { Relations = state.Relations.WithRelation(Me, Foe, f) };
        }

        if (friendRelation is { } g)
        {
            state = state with { Relations = state.Relations.WithRelation(Me, Friend, g) };
        }

        return state with { TurnOrder = ValueList.From(new[] { Me, Foe, Friend }), ActiveSeatIndex = 0 };
    }

    // ------------------------------------------------------------------ Done-when 1: the threat budget

    // 100 troops of light infantry are worth (100/100) x 20 = 20; 10000 troops are worth 2000.
    private const int Army2000 = 10000;

    [Fact]
    public void Term_1_each_nation_at_war_adds_15000()
    {
        var state = Build(Array.Empty<ArmyState>(), foeRelation: War);
        Assert.Equal(15000, AiFaithfulEconomy.Threat(state, Rules(), Me));
        var two = state with { Relations = state.Relations.WithRelation(Me, Friend, War) };
        Assert.Equal(30000, AiFaithfulEconomy.Threat(two, Rules(), Me));
    }

    [Fact]
    public void Term_2_every_army_of_a_nation_at_war_adds_its_full_value()
    {
        var state = Build(new[] { Army("e", Foe, 5, 5, "light_infantry", Army2000) }, foeRelation: War);
        Assert.Equal(15000 + 2000, AiFaithfulEconomy.Threat(state, Rules(), Me));
    }

    [Fact]
    public void Term_2_ignores_armies_of_nations_not_at_war()
    {
        var state = Build(new[] { Army("e", Foe, 5, 5, "light_infantry", Army2000) });
        Assert.Equal(0, AiFaithfulEconomy.Threat(state, Rules(), Me));
    }

    [Fact]
    public void Term_3_a_foreign_army_within_10_of_the_capital_adds_a_quarter_of_its_value()
    {
        var inside = Build(new[] { Army("f", Friend, CapX + 10, CapY, "light_infantry", Army2000) });
        Assert.Equal(500, AiFaithfulEconomy.Threat(inside, Rules(), Me));
        var outside = Build(new[] { Army("f", Friend, CapX + 11, CapY, "light_infantry", Army2000) });
        Assert.Equal(0, AiFaithfulEconomy.Threat(outside, Rules(), Me));
    }

    [Fact]
    public void Term_4_an_army_at_war_within_20_of_the_capital_adds_half_its_value()
    {
        var inside = Build(new[] { Army("e", Foe, CapX, CapY - 20, "light_infantry", Army2000) }, foeRelation: War);
        Assert.Equal(15000 + 2000 + 1000, AiFaithfulEconomy.Threat(inside, Rules(), Me));
        var outside = Build(new[] { Army("e", Foe, CapX, CapY - 21, "light_infantry", Army2000) }, foeRelation: War);
        Assert.Equal(15000 + 2000, AiFaithfulEconomy.Threat(outside, Rules(), Me));
    }

    [Fact]
    public void Terms_2_3_and_4_stack_for_an_enemy_army_inside_10()
    {
        // value 2000: 2000 + 2000/4 + 2000/2.
        var state = Build(new[] { Army("e", Foe, CapX + 3, CapY, "light_infantry", Army2000) }, foeRelation: War);
        Assert.Equal(15000 + 2000 + 500 + 1000, AiFaithfulEconomy.Threat(state, Rules(), Me));
    }

    // T192 adapts the helper only. Issue 1245 separately owns per-army division; keep the current
    // aggregate division discriminating: two armies of power 80 give 160/3=53, not 26+26=52.
    private static Ruleset RulesWithDivisor3()
    {
        var rules = Rules();
        return rules with
        {
            Ai = rules.Ai with
            {
                FaithfulForeignNearDivisor = 3,
                FaithfulWarNearDivisor = 3,
                FaithfulAlliedArmyDivisor = 3,
            },
        };
    }

    // 400 LI at morale 80: ((20*400/100)/80)*80 = 80 per army.
    [Fact]
    public void The_foreign_near_sum_is_floored_once_not_per_army()
    {
        var state = Build(new[]
        {
            Army("f1", Friend, CapX + 1, CapY, "light_infantry", 400),
            Army("f2", Friend, CapX + 2, CapY, "light_infantry", 400),
        });
        Assert.Equal(53, AiFaithfulEconomy.Threat(state, RulesWithDivisor3(), Me));
    }

    [Fact]
    public void The_at_war_within_20_sum_is_floored_once_not_per_army()
    {
        // At distance 15: outside the 10-radius foreign sum, inside the 20-radius war sum.
        var state = Build(
            new[]
            {
                Army("e1", Foe, CapX + 15, CapY, "light_infantry", 400),
                Army("e2", Foe, CapX + 15, CapY + 1, "light_infantry", 400),
            },
            foeRelation: War);
        Assert.Equal(15000 + 160 + 53, AiFaithfulEconomy.Threat(state, RulesWithDivisor3(), Me));
    }

    [Fact]
    public void The_allied_sum_is_floored_once_not_per_army()
    {
        var state = Build(
            new[]
            {
                Army("f1", Friend, 1, 1, "light_infantry", 400),
                Army("f2", Friend, 2, 2, "light_infantry", 400),
            },
            friendRelation: Alliance);
        Assert.Equal(6000 + 53, AiFaithfulEconomy.Own(state, RulesWithDivisor3(), Me));
    }

    [Fact]
    public void Term_5_each_ally_adds_6000_to_the_own_strength()
    {
        var state = Build(Array.Empty<ArmyState>(), friendRelation: Alliance);
        Assert.Equal(6000, AiFaithfulEconomy.Own(state, Rules(), Me));
    }

    [Fact]
    public void Term_6_every_own_army_adds_its_full_value()
    {
        var state = Build(new[] { Army("m", Me, 1, 1, "light_infantry", Army2000), Army("n", Me, 2, 2, "archers", 3500) });
        Assert.Equal(2000 + 1360, AiFaithfulEconomy.Own(state, Rules(), Me));
    }

    [Fact]
    public void Term_7_allied_armies_add_half_their_value()
    {
        var state = Build(new[] { Army("f", Friend, 1, 1, "light_infantry", Army2000) }, friendRelation: Alliance);
        Assert.Equal(6000 + 1000, AiFaithfulEconomy.Own(state, Rules(), Me));
        var peace = Build(new[] { Army("f", Friend, 1, 1, "light_infantry", Army2000) });
        Assert.Equal(0, AiFaithfulEconomy.Own(peace, Rules(), Me));
    }

    // ------------------------------------------------------------------ Done-when 2: recruitment's gates

    private static (GameState State, AiFaithfulEconomy.Result Result) RunEconomy(
        GameState state, ulong seed = 7, Ruleset? rules = null)
    {
        rules ??= Rules();
        var dispatcher = new CommandDispatcher(
            SystemRegistry.FromEngineAssembly(), rules, BattleCommandTestbed.ToyWorld, NullEventSink.Instance);
        var log = new List<string>();
        var result = AiFaithfulEconomy.Run(
            state, rules, BattleCommandTestbed.ToyWorld, dispatcher, NullEventSink.Instance, new SplitMix64Rng(seed), Me, log);
        return (state, result);
    }

    private static int Slots(GameState s) => s.NationById(Me)!.RecruitmentSlots.Count;

    // Threat 15000 (a war), own 0: a deficit.
    private static GameState Deficit(int treasury = 100000, int wealth = 10_000_000) =>
        Build(Array.Empty<ArmyState>(), foeRelation: War, me: n => n with { Treasury = treasury, Wealth = wealth });

    [Fact]
    public void No_deficit_means_no_orders_and_equal_strength_is_no_deficit()
    {
        // own 15000 (one army of value 15000) == threat 15000 (one war): not a deficit.
        var equal = Build(new[] { Army("m", Me, 1, 1, "light_infantry", 60000) with { Morale = 100 } }, foeRelation: War);
        Assert.Equal(AiFaithfulEconomy.Own(equal, Rules(), Me), AiFaithfulEconomy.Threat(equal, Rules(), Me));
        Assert.Equal(0, Slots(RunEconomy(equal).Result.State));

        var surplus = Build(Array.Empty<ArmyState>());
        Assert.Equal(0, Slots(RunEconomy(surplus).Result.State));
    }

    [Fact]
    public void A_deficit_of_one_value_step_gives_orders()
    {
        var short1 = Build(new[] { Army("m", Me, 1, 1, "light_infantry", 59900) with { Morale = 100 } }, foeRelation: War);
        Assert.Equal(14900, AiFaithfulEconomy.Own(short1, Rules(), Me));
        Assert.True(Slots(RunEconomy(short1).Result.State) > 0);
    }

    [Fact]
    public void A_deficit_with_a_healthy_treasury_stops_at_the_first_crossing()
    {
        var run = RunEconomy(Deficit());
        Assert.Equal(4, Slots(run.Result.State)); //seed7 crosses the corrected15000 raw-power deficit on order4
        Assert.Equal(0, run.Result.Rejected);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public void The_8_order_cap_moves_by_one_at_8(int placed, bool allowed)
    {
        var nation = Build(Array.Empty<ArmyState>()).NationById(Me)!;
        Assert.Equal(allowed, AiFaithfulEconomy.MayPlaceAnother(nation, placed, Rules()));
    }

    [Theory]
    [InlineData(1, 2, true)] // one above the floor -2000 (wealth 1,000,000 / 500)
    [InlineData(0, 2, true)] // at the floor, under 3: continues
    [InlineData(0, 3, false)] // at the floor, 3 placed: stops (3 -> 4 boundary)
    [InlineData(-1, 3, false)]
    [InlineData(1, 3, true)] // above the floor, 3 placed: continues
    [InlineData(1, 7, true)]
    public void The_floor_comparison_and_the_3_order_cap(int offsetFromFloor, int placed, bool allowed)
    {
        var nation = Build(Array.Empty<ArmyState>(), me: n => n with { Wealth = 1_000_000 }).NationById(Me)!;
        nation = nation with { Treasury = -2000 + offsetFromFloor };
        Assert.Equal(allowed, AiFaithfulEconomy.MayPlaceAnother(nation, placed, Rules()));
    }

    [Fact]
    public void At_the_floor_a_turn_places_at_most_3_orders_and_above_it_the_deficit_stops_it()
    {
        Assert.Equal(3, Slots(RunEconomy(Deficit(treasury: -20000)).Result.State));
        Assert.Equal(4, Slots(RunEconomy(Deficit(treasury: 100000)).Result.State));
    }

    [Fact]
    public void Each_orders_cost_and_mobilisation_increment_equal_the_players()
    {
        var before = Deficit(treasury: 100000, wealth: 3_000_000);
        var run = RunEconomy(before);
        var after = run.Result.State.NationById(Me)!;
        var start = before.NationById(Me)!;
        var rules = Rules();

        var cost = 0;
        var mobilised = start.MobilizedPercent;
        foreach (var slot in after.RecruitmentSlots)
        {
            cost += StandingRecruitmentCost.InitialCost(slot.Troops, slot.UnitTypeId, rules);
            mobilised = MobilizationRate.AfterOrderPlaced(mobilised, slot.Troops, start.Wealth, rules.Recruitment);
        }

        Assert.True(after.RecruitmentSlots.Count > 0);
        Assert.Equal(start.Treasury - cost, after.Treasury);
        Assert.Equal(mobilised, after.MobilizedPercent);
    }

    // ------------------------------------------------------------------ Done-when 3: the order's city

    private static readonly CityState Fort = Cap("fort", 70, 50, fort: 75);

    private static GameState CityState(
        bool atWar = true,
        int armyDistance = 15,
        int atCapital = 3,
        int elsewhere = 0,
        IEnumerable<CityState>? towns = null)
    {
        var slots = new List<RecruitmentSlot>();
        for (var i = 0; i < atCapital; i++)
        {
            slots.Add(new RecruitmentSlot("a-cap", "light_infantry", 5000, 2));
        }

        for (var i = 0; i < elsewhere; i++)
        {
            slots.Add(new RecruitmentSlot("far", "light_infantry", 5000, 2));
        }

        return Build(
            new[] { Army("m", Me, CapX + armyDistance, CapY, "light_infantry", 1000) },
            cities: new[] { Cap() }.Concat(towns ?? new[] { Fort }).Append(Cap("far", 1, 1, fort: 0)),
            foeRelation: atWar ? War : null,
            slots: ValueList.From(slots));
    }

    private static string? Choose(GameState s) => AiFaithfulEconomy.ChooseCity(s, Rules(), s.NationById(Me)!)?.Id;

    [Fact]
    public void The_capital_is_the_default()
    {
        Assert.Equal("a-cap", Choose(CityState(atWar: false, atCapital: 0)));
    }

    [Fact]
    public void All_three_conditions_send_the_order_to_the_fortified_town()
    {
        Assert.Equal("fort", Choose(CityState()));
    }

    [Fact]
    public void Not_at_war_keeps_the_order_at_the_capital()
    {
        Assert.Equal("a-cap", Choose(CityState(atWar: false)));
    }

    [Theory]
    [InlineData(15, "fort")]
    [InlineData(16, "a-cap")]
    public void An_own_army_must_be_within_15_of_the_capital(int distance, string expected)
    {
        Assert.Equal(expected, Choose(CityState(armyDistance: distance)));
    }

    [Theory]
    [InlineData(3, 0, "fort")] // 3 of 3 queued at the capital
    [InlineData(2, 4, "a-cap")] // 2 of 6 is exactly a third: not more than a third
    [InlineData(3, 5, "fort")] // 3 of 8 is more than a third
    [InlineData(3, 6, "a-cap")] // 3 of 9 is exactly a third
    public void More_than_a_third_of_the_queue_must_sit_at_the_capital(int atCapital, int elsewhere, string expected)
    {
        Assert.Equal(expected, Choose(CityState(atCapital: atCapital, elsewhere: elsewhere)));
    }

    [Fact]
    public void The_town_needs_fortification_75_fewer_queued_orders_and_the_nearest_enemy_capital()
    {
        // 74 is below the gate.
        Assert.Equal("a-cap", Choose(CityState(towns: new[] { Cap("fort", 70, 50, fort: 74) })));

        // A town with as many queued orders as the capital is not "fewer".
        var queuedAsMuch = CityState(atCapital: 3);
        var slots = queuedAsMuch.NationById(Me)!.RecruitmentSlots
            .Concat(Enumerable.Repeat(new RecruitmentSlot("fort", "light_infantry", 5000, 2), 3));
        queuedAsMuch = queuedAsMuch with
        {
            Nations = ValueList.From(queuedAsMuch.Nations.Select(n =>
                n.Id == Me ? n with { RecruitmentSlots = ValueList.From(slots) } : n)),
        };
        Assert.Equal("a-cap", Choose(queuedAsMuch));

        // Of two eligible towns the one nearer the enemy capital (90,90) wins, whatever the list order.
        var near = Cap("near", 80, 80, fort: 80);
        var far = Cap("farther", 20, 20, fort: 80);
        Assert.Equal("near", Choose(CityState(towns: new[] { far, near })));
        Assert.Equal("near", Choose(CityState(towns: new[] { near, far })));
    }

    // ------------------------------------------------------------------ Done-when 4: type and amount

    private sealed class ScriptedRng : IRng
    {
        private readonly Queue<int> _values;

        public ScriptedRng(params int[] values) => _values = new Queue<int>(values);

        public int Draws { get; private set; }

        public List<int> Bounds { get; } = new();

        public ulong Seed => 0;

        public ulong State => 0;

        public ulong NextUInt64() => throw new NotSupportedException();

        public int NextInt(int exclusiveUpperBound)
        {
            Draws++;
            Bounds.Add(exclusiveUpperBound);
            return _values.Dequeue();
        }

        public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound) => throw new NotSupportedException();

        public bool NextChance(int numerator, int denominator) => throw new NotSupportedException();

        public IRng ForStream(string streamName) => this;
    }

    [Theory]
    [InlineData(0, "light_infantry")]
    [InlineData(34, "light_infantry")]
    [InlineData(35, "heavy_infantry")]
    [InlineData(59, "heavy_infantry")]
    [InlineData(60, "archers")]
    [InlineData(74, "archers")]
    [InlineData(75, "light_cavalry")]
    [InlineData(89, "light_cavalry")]
    [InlineData(90, "heavy_cavalry")]
    [InlineData(99, "heavy_cavalry")]
    public void The_type_buckets_have_their_boundaries(int draw, string expected)
    {
        var rng = new ScriptedRng(draw);
        Assert.Equal(expected, AiFaithfulEconomy.RollType(rng, Rules()));
        Assert.Equal(new[] { 100 }, rng.Bounds);
    }

    [Theory]
    [InlineData("light_infantry", 0, 5000)] // minimum: 15000/3
    [InlineData("heavy_cavalry", 0, 800)] // 2500/3 = 833 rounds down to 800
    [InlineData("archers", 0, 1100)] // 3500/3 = 1166 rounds down to 1100
    [InlineData("light_infantry", 1234, 6200)] // 5000 + 1234 = 6234 rounds down to 6200
    [InlineData("heavy_infantry", 199, 2100)] // 2000 + 199 = 2199 rounds down
    [InlineData("light_infantry", 11999, 15000)] // 16999 capped at the battalion
    [InlineData("heavy_cavalry", 1999, 2500)] // 2832 capped at 2500
    [InlineData("archers", 2799, 3500)] // 3965 capped at 3500
    public void An_amount_is_rounded_down_to_100_at_least_a_third_and_at_most_the_battalion(
        string typeId, int draw, int expected)
    {
        var rules = Rules();
        var type = rules.UnitTypeById(typeId)!;
        var rng = new ScriptedRng(draw);
        var troops = AiFaithfulEconomy.RollTroops(rng, type, rules);
        Assert.Equal(expected, troops);
        Assert.Equal(0, troops % 100);
        Assert.Equal(new[] { type.StandardBattalionSize * 4 / 5 }, rng.Bounds);
    }

    [Fact]
    public void A_turn_draws_twice_per_order_on_its_own_stream_and_none_from_the_seats_and_reproduces_for_a_seed()
    {
        var state = Deficit();
        var rules = Rules();
        var dispatcher = new CommandDispatcher(
            SystemRegistry.FromEngineAssembly(), rules, BattleCommandTestbed.ToyWorld, NullEventSink.Instance);

        var seat = new CountingRng(new SplitMix64Rng(11));
        var result = AiFaithfulEconomy.Run(state, rules, BattleCommandTestbed.ToyWorld, dispatcher, NullEventSink.Instance, seat, Me, new List<string>());
        Assert.Equal(0, seat.OwnDraws);
        Assert.Equal(6, Slots(result.State)); //seed11 crosses15000 on order6
        Assert.Equal(12, seat.Child!.OwnDraws);

        string Key(GameState s) => string.Join(
            ";", s.NationById(Me)!.RecruitmentSlots.Select(x => $"{x.TargetCityId}/{x.UnitTypeId}/{x.Troops}"));
        var again = RunEconomy(state, seed: 11).Result.State;
        Assert.Equal(Key(result.State), Key(again));
        Assert.NotEqual(Key(result.State), Key(RunEconomy(state, seed: 12).Result.State));
    }

    private sealed class CountingRng : IRng
    {
        private readonly IRng _inner;

        public CountingRng(IRng inner) => _inner = inner;

        public int OwnDraws { get; private set; }

        public CountingRng? Child { get; private set; }

        public ulong Seed => _inner.Seed;

        public ulong State => _inner.State;

        public ulong NextUInt64()
        {
            OwnDraws++;
            return _inner.NextUInt64();
        }

        public int NextInt(int exclusiveUpperBound)
        {
            OwnDraws++;
            return _inner.NextInt(exclusiveUpperBound);
        }

        public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound)
        {
            OwnDraws++;
            return _inner.NextInt(inclusiveLowerBound, exclusiveUpperBound);
        }

        public bool NextChance(int numerator, int denominator)
        {
            OwnDraws++;
            return _inner.NextChance(numerator, denominator);
        }

        public IRng ForStream(string streamName) => Child = new CountingRng(_inner.ForStream(streamName));
    }

    // ------------------------------------------------------------------ Done-when 5: the week-11 tax

    private static NationState TaxNation(int tax = 20, int unity = 700, int treasury = 100, int wealth = 1_000_000) =>
        CaptureFixtures.Nation("t", treasury: treasury, unity: unity, wealth: wealth) with { TaxRatePercent = tax };

    private static int Next(NationState n, long own = 1, long threat = 0) =>
        AiFaithfulEconomy.NextTaxRate(n, Rules(), own, threat);

    [Theory]
    [InlineData(649, 100, 14)] // unity < 650 cuts by 6
    [InlineData(650, 100, 20)] // 650 does not
    [InlineData(700, 500, 20)] // treasury == wealth/2000 = 500 does not
    [InlineData(700, 501, 14)] // treasury > wealth/2000 cuts
    public void Rule_1_cuts_by_6_on_low_unity_or_a_rich_treasury(int unity, int treasury, int expected)
    {
        Assert.Equal(expected, Next(TaxNation(unity: unity, treasury: treasury)));
    }

    [Theory]
    [InlineData(11, 5)]
    [InlineData(12, 6)]
    [InlineData(8, 5)]
    public void Rule_1_never_cuts_below_5(int tax, int expected)
    {
        Assert.Equal(expected, Next(TaxNation(tax: tax, unity: 600)));
    }

    [Theory]
    [InlineData(-1, 1, 0, 29)] // deficit raises by 9
    [InlineData(0, 1, 0, 20)] // zero treasury, own > threat: no raise
    [InlineData(999, 0, 0, 29)] // own <= threat and treasury < 1000
    [InlineData(1000, 0, 0, 20)] // 1000 is not < 1000
    [InlineData(999, 1, 0, 20)] // own > threat: no raise
    public void Rule_2_raises_by_9_on_a_deficit_or_a_threatened_poor_treasury(
        int treasury, long own, long threat, int expected)
    {
        // wealth large enough that rule 1's treasury arm never fires.
        Assert.Equal(expected, Next(TaxNation(treasury: treasury, wealth: 100_000_000), own, threat));
    }

    [Fact]
    public void Rule_2_never_raises_above_40()
    {
        Assert.Equal(40, Next(TaxNation(tax: 35, treasury: -5, wealth: 100_000_000)));
        Assert.Equal(40, Next(TaxNation(tax: 31, treasury: -5, wealth: 100_000_000)));
        Assert.Equal(39, Next(TaxNation(tax: 30, treasury: -5, wealth: 100_000_000)));
    }

    [Theory]
    [InlineData(499, 1, 0)] // positive treasury and unity < 500 zeroes
    [InlineData(500, 1, 14)] // unity 500: rule 1 (unity < 650) cuts 20 -> 14, rule 3 does not fire
    [InlineData(499, 0, 14)] // treasury 0: no zeroing (rule 1 cuts)
    public void Rule_3_zeroes_the_tax_with_money_and_low_unity(int unity, int treasury, int expected)
    {
        Assert.Equal(expected, Next(TaxNation(unity: unity, treasury: treasury, wealth: 100_000_000), own: 1, threat: 0));
    }

    [Fact]
    public void The_rules_apply_in_order_cut_then_raise_then_zero()
    {
        // 38 -> cut 32 -> raise 40 (reversed order would give 34).
        Assert.Equal(40, Next(TaxNation(tax: 38, unity: 600, treasury: -5, wealth: 100_000_000)));
        // Rule 3 comes last: a raise-eligible nation with money and unity < 500 still ends at 0.
        Assert.Equal(0, Next(TaxNation(tax: 20, unity: 400, treasury: 10, wealth: 100_000_000), own: 0, threat: 1));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(5, false)]
    [InlineData(7, false)]
    [InlineData(9, false)]
    [InlineData(11, true)]
    public void The_tax_changes_only_in_week_11(int week, bool changes)
    {
        // unity 600 < 650: rule 1 would cut 15 -> 9 whenever the policy runs.
        var state = Build(Array.Empty<ArmyState>(), me: n => n with { Unity = 600 });
        state = state with { Calendar = state.Calendar with { Week = week } };
        var after = RunEconomy(state).Result.State.NationById(Me)!;
        Assert.Equal(changes ? 9 : 15, after.TaxRatePercent);
    }

    // ------------------------------------------------------------------ Done-when 6: no fortifying

    [Fact]
    public void Under_the_original_economy_nothing_is_proposed_to_fortify_or_recruit_and_under_the_designed_one_a_fortify_decision_stands()
    {
        var designed = BattleCommandTestbed.ToyRuleset;
        Assert.Equal(AiEconomyModel.Designed, designed.Flags.AiEconomy);
        var state = Build(Array.Empty<ArmyState>(), cities: new[] { Cap(fort: 20) }, foeRelation: War);

        List<AiCandidate> Propose(Ruleset rules)
        {
            var view = new AiView(state, rules, BattleCommandTestbed.ToyWorld, Me);
            var list = new List<AiCandidate>();
            AiEconomyPhase.Propose(view, AiPersonalityProfile.For(state.NationById(Me)!, rules), list);
            return list;
        }

        Assert.Contains(Propose(designed), c => c.Kind == "fortify");
        var original = Propose(Rules());
        Assert.DoesNotContain(original, c => c.Kind is "fortify" or "recruit");
    }

    [Fact]
    public void A_seeded_all_ai_classical_mediterranean_run_orders_no_fortification_and_recruits()
    {
        var shipped = GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");
        Assert.Equal(AiEconomyModel.Original, shipped.Ruleset.Flags.AiEconomy);

        var result = AiGameRunner.Run(shipped.World, shipped.Ruleset, shipped.Scenario, seed: 3, turnCap: 200);

        Assert.Equal(0, result.CommandsRejected);
        Assert.DoesNotContain(result.Transcript, l => l.Contains("issued city.order", StringComparison.Ordinal));
        Assert.Contains(result.Transcript, l => l.Contains("faithful order", StringComparison.Ordinal));
    }

    [Fact]
    public void Equally_near_eligible_towns_resolve_to_the_lower_id_in_either_collection_order()
    {
        // (70,50) and (50,70) are both at Chebyshev distance 40 from the enemy capital (90,90).
        var one = Cap("one", 70, 50, fort: 80);
        var two = Cap("two", 50, 70, fort: 80);
        Assert.Equal("one", Choose(CityState(towns: new[] { one, two })));
        Assert.Equal("one", Choose(CityState(towns: new[] { two, one })));
    }

    [Fact]
    public void The_economy_reads_the_treasury_as_the_turn_starts_not_after_the_army_purse_top_up()
    {
        // Week 11, unity 400, treasury 1, tax 15, an own army beside a stocked capital with an empty purse.
        // Before the army's resupply rule 3 applies (treasury > 0, unity < 500) and the tax goes to 0; after
        // the purse top-up the treasury would be negative and the tax would rise instead (Sol's probe: 18).
        var capital = Cap() with { SupplyTons = 1000 };
        var state = Build(
            new[] { Army("m", Me, CapX + 1, CapY, "light_infantry", Army2000) with { Money = 0 } },
            cities: new[] { capital },
            me: n => n with { Treasury = 1, Unity = 400, TaxRatePercent = 15 });
        state = state with { Calendar = state.Calendar with { Week = 11 } };

        var rules = Rules();
        var dispatcher = new CommandDispatcher(
            SystemRegistry.FromEngineAssembly(), rules, BattleCommandTestbed.ToyWorld, NullEventSink.Instance);
        var outcome = AiTurn.Run(
            state, rules, BattleCommandTestbed.ToyWorld, dispatcher, new SplitMix64Rng(5), NullEventSink.Instance);

        Assert.Equal(0, outcome.CommandsRejected);
        Assert.Equal(0, outcome.State.NationById(Me)!.TaxRatePercent);
    }
}
