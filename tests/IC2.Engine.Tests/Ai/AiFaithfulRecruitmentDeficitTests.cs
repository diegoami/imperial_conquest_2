using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Recruitment;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using F = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;
using M = IC2.Engine.Tests.Recruitment.MobilizationFixture;

namespace IC2.Engine.Tests.Ai;

/// <summary>T192: production budget flow, not a second implementation of the loop.</summary>
public sealed class AiFaithfulRecruitmentDeficitTests
{
    private const string Me = "north";
    private const string Foe = "south";
    private static World World => M.OpenWorld(30, 30);
    private static Ruleset Rules => BattleCommandTestbed.ToyRuleset with
    {
        Flags = BattleCommandTestbed.ToyRuleset.Flags with { AiEconomy = AiEconomyModel.Original },
    };
    private static RecruitmentSlot Slot(int troops, int ready = 0, string type = "light_infantry", string city = "home") => new(city, type, troops, ready);
    private static ArmyState Army(int troops = 56000, int morale = 100) => F.Army("own", Me, 6, 5, morale, F.Unit("light_infantry", troops));
    private static GameState State(ArmyState? army = null, params RecruitmentSlot[] slots)
    {
        var home = F.Nation(Me, treasury: 100000, wealth: 10_000_000, capitalCityId: "home") with { RecruitmentSlots = ValueList.From(slots) };
        var state = BattleCommandTestbed.StateWith(new[] { home, F.Nation(Foe, capitalCityId: "enemy") },
            new[] { M.City("home", 5, 5, Me), M.City("away", 20, 5, Me), M.City("third", 5, 20, Me), M.City("fourth", 20, 20, Me), M.City("enemy", 28, 28, Foe) },
            army is null ? Array.Empty<ArmyState>() : new[] { army });
        return state with
        {
            TurnOrder = ValueList.Of(Me, Foe),
            ActiveSeatIndex = 0,
            Relations = state.Relations.WithRelation(Me, Foe, Rules.Diplomacy.StateCodes.War)
        };
    }
    private static GameState Nation(GameState s, Func<NationState, NationState> change) => s with
    { Nations = ValueList.From(s.Nations.Select(n => n.Id == Me ? change(n) : n)) };
    private static void Native(GameState state, Ruleset rules)
    {
        // Every troop/type fixture is a genuine serialized WORD-sized input, never an out-of-range int shortcut.
        foreach (var q in state.Nations.SelectMany(n => n.RecruitmentSlots))
        { Assert.InRange(q.Troops, 0, ushort.MaxValue); Assert.NotNull(rules.UnitTypeById(q.UnitTypeId)); }
        foreach (var u in state.Armies.SelectMany(a => a.Units)) Assert.InRange(u.Troops, 0, ushort.MaxValue);
        GameDataValidation.Validate("budget-state", state);
        var save = new SaveGame(state.SchemaVersion, "budget", "Budget", state.ScenarioId, state.WorldId, state.RulesetId, state);
        var text = SaveManager.Serialize(save);
        var copy = SaveManager.Load("budget.json", text, World, rules);
        Assert.Equal(save, copy);
        Assert.Equal(state, copy.State);
        Assert.Equal(GameStateHash.Compute(state), GameStateHash.Compute(copy.State));
        Assert.Equal(text, SaveManager.Serialize(copy));
    }
    private sealed record RunResult(AiFaithfulEconomy.Result Result, List<string> Log, RecordingEventSink Events, Draws Rng);
    private static RunResult Run(GameState state, Ruleset? rules = null, Draws? rng = null, bool rejectMobilize = false)
    {
        rules ??= Rules; rng ??= new Draws(); Native(state, rules);
        var sink = new RecordingEventSink(); var log = new List<string>();
        ICommandDispatch dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), rules, World, sink);
        if (rejectMobilize) dispatcher = new RejectMobilize(dispatcher);
        var result = AiFaithfulEconomy.Run(state, rules, World, dispatcher, sink, rng, Me, log);
        Native(result.State, rules);
        Assert.Empty(rng.ParentDraws);
        return new(result, log, sink, rng);
    }
    private static RecruitmentSlot[] Queues(RunResult r) => r.Result.State.NationById(Me)!.RecruitmentSlots.ToArray();
    private static void NewResources(GameState before, RunResult run, int newCount)
    {
        // Controlled production draws always order LI5000: raw Q1000, price50, rate+1 at wealth10M.
        Assert.Equal(before.NationById(Me)!.Treasury - 50 * newCount, run.Result.State.NationById(Me)!.Treasury);
        Assert.Equal(before.NationById(Me)!.MobilizedPercent + newCount, run.Result.State.NationById(Me)!.MobilizedPercent);
        Assert.Equal(Enumerable.Range(0, newCount).SelectMany(_ => new[] { (100, 0), (12000, 0) }), run.Rng.ChildDraws);
    }

    [Fact]
    public void Issue_own_30000_morale90_is_6750_in_the_real_Run_budget()
    {
        var state = State(Army(30000, 90));
        Assert.Equal(6750, AiFaithfulEconomy.Own(state, Rules, Me));
        var run = Run(state);
        Assert.Contains("faithful economy: threat 15000, own 6750", run.Log);
        Assert.Equal(8, Queues(run).Length); // minimum Q1000 leaves250 deficit; max8 still applies
        NewResources(state, run, 8);
    }

    [Theory]
    [InlineData(399, 100, 0)] // (20*399/100)/80 truncates to zero
    [InlineData(400, 100, 100)]
    [InlineData(799, 51, 51)]
    [InlineData(800, 51, 102)]
    [InlineData(30000, 0, 0)]
    [InlineData(30000, 90, 6750)]
    public void Own_multiplies_before_each_slot_division_then_divides_by80_then_morale(int troops, int morale, int power)
    {
        var state = State(Army(troops, morale)); Native(state, Rules);
        Assert.Equal(power, AiFaithfulEconomy.Own(state, Rules, Me));
    }

    [Fact]
    public void Mixed_slots_pin_per_slot_truncation_and_shared_foreign_helper_with_deferred_boundaries()
    {
        var mixed = Army(0, 7) with { Units = ValueList.Of(F.Unit("heavy_infantry", 39), F.Unit("light_infantry", 199), F.Unit("archers", 7)) };
        // 39+39+2=80 ->1*7. Aggregating before slot truncation would incorrectly reach81 here;
        // the boundary below discriminates by removing one point:38+39+2=79 ->0.
        Assert.Equal(7, AiFaithfulEconomy.Own(State(mixed), Rules, Me));
        mixed = mixed with { Units = ValueList.Of(F.Unit("heavy_infantry", 38), F.Unit("light_infantry", 199), F.Unit("archers", 7)) };
        Assert.Equal(0, AiFaithfulEconomy.Own(State(mixed), Rules, Me));
        var foreign = mixed with
        {
            Id = "foreign",
            Nation = Foe,
            X = 15,
            Y = 5,
            Units = ValueList.Of(F.Unit("light_infantry", 30000)),
            Morale = 90
        };
        var state = State() with { Armies = ValueList.Of(foreign) };
        Assert.Equal(15000 + 6750 + 1687 + 3375, AiFaithfulEconomy.Threat(state, Rules, Me)); // inclusive distance10 retained
        state = state with { Relations = state.Relations.WithRelation(Me, Foe, Rules.Diplomacy.StateCodes.Alliance) };
        Assert.Equal(6000 + 3375, AiFaithfulEconomy.Own(state, Rules, Me));
        Assert.Equal(1687, AiFaithfulEconomy.Threat(state, Rules, Me));
        // Retain aggregate, not per-army, truncation while adapting the helper (issue1245 deferred).
        var odd = foreign with { Units = ValueList.Of(F.Unit("heavy_infantry", 80)), Morale = 7 };
        state = state with { Armies = ValueList.Of(odd, odd with { Id = "foreign2" }) };
        Assert.Equal(6000 + 7, AiFaithfulEconomy.Own(state, Rules, Me));
        Assert.Equal(3, AiFaithfulEconomy.Threat(state, Rules, Me));
    }

    [Theory]
    [InlineData(59600, 1)] // own14900, D100: one accepted overshoot
    [InlineData(60000, 0)] // exact equality: no child stream or next draw
    [InlineData(60400, 0)]
    public void No_equal_and_positive_initial_deficits_gate_the_full_pass(int troops, int expected)
    {
        var state = State(Army(troops)); var run = Run(state);
        Assert.Equal(expected, Queues(run).Length); NewResources(state, run, expected);
        Assert.Equal(expected != 0, run.Rng.Streams.Count != 0);
    }

    [Theory]
    [InlineData(56400, 1)] // D900, first Q1000 overshoots by100
    [InlineData(56000, 1)] // D1000, exactly met
    [InlineData(55600, 2)] // D1100, Q1000 below: another order must be placed
    public void The_first_successful_crossing_stops_before_another_roll_despite_resources_and_capacity(int troops, int expected)
    {
        var state = State(Army(troops)); var run = Run(state);
        Assert.Equal(expected, run.Result.Issued); Assert.Equal(0, run.Result.Rejected);
        Assert.All(Queues(run), q => { Assert.Equal(5000, q.Troops); Assert.Equal("light_infantry", q.UnitTypeId); });
        Assert.Equal(expected, Queues(run).Length); NewResources(state, run, expected);
        Assert.True(run.Result.State.NationById(Me)!.Treasury > 90000);
        Assert.True(run.Result.State.NationById(Me)!.MobilizedPercent < 100);
    }

    [Theory]
    [InlineData(4999, 1)] // trunc(troops/100)*20=980: remainder20 -> one new order
    [InlineData(5000, 0)] //1000 equals D: not even next type draw
    [InlineData(5099, 0)] //same raw Q1000
    [InlineData(5100, 0)] //above D
    public void Unready_visited_queue_alone_can_close_the_new_order_gate(int troops, int newCount)
    {
        var state = State(Army(), Slot(troops)); var run = Run(state);
        Assert.Empty(run.Events.Events.OfType<RecruitMobilized>());
        Assert.Equal(1 + newCount, Queues(run).Length); NewResources(state, run, newCount);
    }

    [Theory]
    [InlineData(32700, 0)] // signed positive32700 closes D1000
    [InlineData(32800, 8)] // signed -32736 leaves room, not int32800
    [InlineData(65535, 2)] //Q65500 stored short=-36 => remainder1036 -> two Q1000
    public void Existing_valid_WORD_troops_use_signed_short_storage_before_sign_extension(int troops, int newCount)
    {
        var state = State(Army(), Slot(troops, type: "heavy_infantry")); var run = Run(state);
        Assert.Equal(1 + newCount, Queues(run).Length); NewResources(state, run, newCount);
    }

    [Theory]
    [InlineData(24, 8)] // at most8 even for requested15000
    [InlineData(0, 8)]
    public void Closed_gate_and_unready_entries_count_without_successful_mobilization(int readiness, int newCount)
    {
        var state = State(null, Slot(1000, readiness)); var run = Run(state);
        Assert.Empty(run.Events.Events.OfType<RecruitMobilized>());
        Assert.Equal(1 + newCount, Queues(run).Length); NewResources(state, run, newCount);
    }

    [Fact]
    public void Gate_closed_ready_entry_and_rejected_mobilization_both_close_a_small_remainder()
    {
        var distant = State(Army() with { X = 15, Y = 15 }, Slot(5000, 24));
        var closed = Run(distant); Assert.Empty(closed.Events.Events.OfType<RecruitMobilized>()); Assert.Single(Queues(closed)); NewResources(distant, closed, 0);
        var before = State(Army(), Slot(5000, 24)); var rejected = Run(before, rejectMobilize: true);
        Assert.Equal(1, rejected.Result.Issued); Assert.Equal(1, rejected.Result.Rejected); Assert.Single(Queues(rejected)); NewResources(before, rejected, 0);
        Assert.Equal(before, rejected.Result.State);
    }

    [Fact]
    public void Partial_mobilization_accounts_visited_unready_entries_but_not_the_shifted_record()
    {
        // D1000. Actual visited sequence: original0 ready Q200 succeeds; original1 shifts/skips;
        // original2 unready Q600 counts. Remainder200 => one LI5000. Initial pre-sum1200
        // would stop; post-sum1000 would stop; successful-only200 is distinguished by the companion D1800 control below. Same scan, no snapshot recomputation.
        var slots = new[] { Slot(1000, 24), Slot(2000, 0), Slot(3000, 0) };
        var state = State(Army(), slots); var run = Run(state);
        Assert.Equal(1000, Assert.Single(run.Events.Events.OfType<RecruitMobilized>()).Troops);
        Assert.Equal(new[] { slots[1], slots[2] }, Queues(run).Take(2)); Assert.Equal(3, Queues(run).Length); NewResources(state, run, 1);
        var larger = State(Army(52800), slots); //own13200,D1800,visited800,remainder1000=>one; successful-only would place two
        var second = Run(larger); Assert.Equal(3, Queues(second).Length); NewResources(larger, second, 1);
    }

    [Fact]
    public void Accounting_continues_after_mobilization_target_is_met_without_recomputing_initial_own()
    {
        var slots = new[] { Slot(5000, 24), Slot(1000, 24), Slot(32800, 0, "heavy_infantry") };
        var state = State(Army(), slots); var run = Run(state);
        Assert.Single(run.Events.Events.OfType<RecruitMobilized>()); //first Q1000 meets initial D
        // Original1 is skipped on compaction; original2 still counts signed -32736.
        // local24=1000-32736, so all8 new orders are permitted despite local20 meeting D.
        Assert.Equal(10, Queues(run).Length); Assert.Equal(slots[1], Queues(run)[0]); Assert.Equal(slots[2], Queues(run)[1]); NewResources(state, run, 8);
    }

    [Fact]
    public void Fully_successful_mobilization_uses_initial_target_and_visited_sequence()
    {
        var slots = new[] { Slot(4000, 24), Slot(1000, 0), Slot(3000, 24) };
        var before = State(Army(54800), slots); //D1300; visited/mobilized800+600=1400, one overshoot
        var run = Run(before);
        Assert.Equal(new[] { 4000, 3000 }, run.Events.Events.OfType<RecruitMobilized>().Select(e => e.Troops));
        Assert.Equal(slots[1], Assert.Single(Queues(run))); NewResources(before, run, 0);
    }

    [Fact]
    public void Infeasible_ready_entries_count_even_when_the_latched_gate_is_open()
    {
        var rules = Rules with { ArmyManagement = Rules.ArmyManagement with { MaxArmies = 1 } };
        var before = State(Army() with { X = 15, Y = 15 }, Slot(5000, 24), Slot(5000, 24), Slot(5000, 24), Slot(5000, 24));
        var run = Run(before, rules);
        Assert.Empty(run.Events.Events.OfType<RecruitMobilized>()); Assert.Equal(4, Queues(run).Length); NewResources(before, run, 0);
        Assert.Equal(0, run.Result.Issued);
    }

    [Fact]
    public void Successful_new_queue_keeps_int_power_above_the_signed_WORD_boundary()
    {
        // Valid serialized test rules enlarge a standard battalion within WORD range; no shipped
        // data/policy changes. The new order's raw40000 must not become signed-short -25536.
        var rules = Rules with
        {
            UnitTypes = ValueList.From(Rules.UnitTypes.Select(t => t.Id == "light_infantry"
            ? t with { StandardBattalionSize = 40000, CombatPowerWeight = 100 } : t))
        };
        var json = System.Text.Json.JsonSerializer.Serialize(rules, GameJson.Options);
        rules = GameDataLoader.Load<Ruleset>("word-sized-test-rules.json", json);
        var before = State(); var run = Run(before, rules, new Draws(amountDraw: 30000));
        Assert.Equal(40000, Assert.Single(Queues(run)).Troops);
        Assert.Equal(1, run.Result.Issued); Assert.Equal(0, run.Result.Rejected);
        Assert.Equal(new[] { (100, 0), (32000, 30000) }, run.Rng.ChildDraws);
        Assert.Equal(99600, run.Result.State.NationById(Me)!.Treasury);
        Assert.Equal(5, run.Result.State.NationById(Me)!.MobilizedPercent);
    }

    [Fact]
    public void A_real_rejected_new_order_leaves_resources_and_queue_unchanged()
    {
        var rules = Rules with { Flags = Rules.Flags with { UnaffordableRecruitAndFortify = UnaffordableOrderPolicy.Refuse } };
        var state = Nation(State(Army()), n => n with { Treasury = 0 }); var run = Run(state, rules);
        Assert.Equal(1, run.Result.Issued); Assert.Equal(1, run.Result.Rejected); Assert.Equal(state, run.Result.State);
        Assert.Equal(new[] { (100, 0), (12000, 0) }, run.Rng.ChildDraws); Assert.Empty(Queues(run));
    }

    [Fact]
    public void State_log_and_ordered_draws_repeat_and_parent_stream_is_untouched()
    {
        var before = State(Army(55600), Slot(199)); var first = Run(before); var second = Run(before);
        Assert.Equal(first.Result, second.Result); Assert.Equal(first.Log, second.Log);
        Assert.Equal(first.Rng.ChildDraws, second.Rng.ChildDraws);
        Assert.Equal(new[] { "ai.faithful-economy:north:0" }, first.Rng.Streams);
        Assert.Empty(first.Rng.ParentDraws); Assert.Equal(3, Queues(first).Length); //existingQ20; D1100 leaves1080, requiring two Q1000 orders
    }

    [Fact]
    public void A_real_seed_repeats_full_state_log_and_ordered_economy_draws_without_parent_draws()
    {
        var before = State(Army(30000, 90), Slot(199));
        (AiFaithfulEconomy.Result Result, List<string> Log, TraceRng Rng) Execute()
        {
            var rng = new TraceRng(new SplitMix64Rng(73)); var log = new List<string>();
            var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), Rules, World, NullEventSink.Instance);
            var result = AiFaithfulEconomy.Run(before, Rules, World, dispatch, NullEventSink.Instance, rng, Me, log);
            Native(result.State, Rules); return (result, log, rng);
        }
        Native(before, Rules); var a = Execute(); var b = Execute();
        Assert.Equal(a.Result, b.Result); Assert.Equal(a.Log, b.Log);
        Assert.Equal(a.Rng.Child!.Values, b.Rng.Child!.Values);
        Assert.Empty(a.Rng.Values); Assert.Empty(b.Rng.Values);
        Assert.Equal(2 * (a.Result.State.NationById(Me)!.RecruitmentSlots.Count - 1), a.Rng.Child.Values.Count);
        Assert.Equal("ai.faithful-economy:north:0", a.Rng.Stream);
        Assert.Equal(0, a.Result.Rejected);
    }

    private sealed class TraceRng(IRng inner) : IRng
    {
        public List<(int Bound, int Value)> Values { get; } = new();
        public TraceRng? Child { get; private set; }
        public string? Stream { get; private set; }
        public ulong Seed => inner.Seed; public ulong State => inner.State;
        public ulong NextUInt64() => throw new InvalidOperationException("unexpected raw draw");
        public int NextInt(int bound) { var v = inner.NextInt(bound); Values.Add((bound, v)); return v; }
        public int NextInt(int low, int high) { var v = inner.NextInt(low, high); Values.Add((high - low, v - low)); return v; }
        public bool NextChance(int n, int d) => NextInt(d) < n;
        public IRng ForStream(string name) { Stream = name; return Child = new TraceRng(inner.ForStream(name)); }
    }

    private sealed class Draws : IRng
    {
        private readonly bool _child;
        private readonly int _amountDraw;
        public List<int> ParentDraws { get; } = new();
        public List<(int Bound, int Value)> ChildDraws { get; } = new();
        public List<string> Streams { get; } = new();
        public ulong Seed => 123; public ulong State => 123;
        public Draws(int amountDraw = 0, bool child = false) { _child = child; _amountDraw = amountDraw; }
        public ulong NextUInt64() { ParentDraws.Add(-1); throw new InvalidOperationException("unexpected direct draw"); }
        public int NextInt(int bound) { if (!_child) { ParentDraws.Add(bound); throw new InvalidOperationException("parent advanced"); } ChildDraws.Add((bound, 0)); return 0; }
        public int NextInt(int low, int high) => low + NextInt(high - low);
        public bool NextChance(int n, int d) => NextInt(d) < n;
        public IRng ForStream(string name) { Streams.Add(name); return new Child(this); }
        private sealed class Child(Draws owner) : IRng
        {
            public ulong Seed => 123; public ulong State => 123;
            public ulong NextUInt64() => throw new InvalidOperationException("unexpected child uint draw");
            public int NextInt(int bound) { var value = owner.ChildDraws.Count % 2 == 0 ? 0 : owner._amountDraw; Assert.InRange(value, 0, bound - 1); owner.ChildDraws.Add((bound, value)); return value; }
            public int NextInt(int low, int high) => low + NextInt(high - low);
            public bool NextChance(int n, int d) => NextInt(d) < n;
            public IRng ForStream(string name) => throw new InvalidOperationException("nested stream");
        }
    }
    // Explicit dispatch fault control: mobilization rejection counts no success, but visited Q
    // must still close recruitment. All other commands use the real dispatcher unchanged.
    private sealed class RejectMobilize(ICommandDispatch real) : ICommandDispatch
    {
        public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, NullEventSink.Instance);
        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events) => command is MobilizeRecruitSlotCommand
            ? UnavailableCommandDispatch.Instance.Dispatch(state, command, events) : real.Dispatch(state, command, events);
    }
}
