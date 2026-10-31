using IC2.Engine.Ai;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using F = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

public sealed class FaithfulArmyOrderTests
{
    private static (GameState State, Ruleset Rules, World World) Fixture(bool faithful)
    {
        var toy = AiScriptedStates.Ruleset;
        var rules = toy with { Flags = toy.Flags with {
            AiEconomy = faithful ? AiEconomyModel.Original : AiEconomyModel.Designed,
            CombatOnDefeat = faithful ? DefeatOutcome.Destroyed : toy.Flags.CombatOnDefeat,
            UnitTableCompaction = faithful ? UnitTableCompactionPolicy.SwapRemoveSweep : toy.Flags.UnitTableCompaction } };
        var world = AiScriptedStates.World with { Id = "classical-mediterranean", Width = 320, Height = 140,
            Terrain = new(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(2, 320 * 140))) };
        ArmyState Army(string id, string owner, int x, int troops) => F.Army(id, owner, x, 80, 80,
            F.Unit("light_infantry", troops)) with { Moves = 8, SupplyTons = 500, Money = 500 };
        var state = BattleCommandTestbed.StateWith(
            [F.Nation("north", capitalCityId: "home"), F.Nation("south", capitalCityId: "enemy-home")],
            [F.City("home", "Home", 200, 80, "north", "north", 80, 100, 100, 100, 1),
             F.City("enemy-home", "Enemy", 260, 90, "south", "south", 80, 100, 100, 100, 1)],
            [Army("z-first", "north", 230, 25000), Army("a-later", "north", 250, 25000),
             Army("target-first", "south", 232, 1000), Army("target-later", "south", 251, 1000)]);
        state = AiScriptedStates.WithActiveSeat(state, "north");
        return (state with { Relations = state.Relations.WithRelation("north", "south", rules.Diplomacy.StateCodes.War),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == "north"
                ? n with { MobilizedPercent = rules.Recruitment.MobilizationCapPercent } : n)) }, rules, world);
    }

    private sealed class Trace(ICommandDispatch inner) : ICommandDispatch
    {
        public List<ICommand> Commands { get; } = [];
        public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, NullEventSink.Instance);
        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
        {
            Commands.Add(command);
            return inner.Dispatch(state, command, events);
        }
    }

    private static (AiTurnOutcome Outcome, Trace Trace) Run(GameState state, Ruleset rules, World world, IRng? rng = null)
    {
        var sink = new RecordingEventSink();
        var trace = new Trace(new CommandDispatcher(SystemRegistry.FromEngineAssembly(), rules, world, sink));
        return (AiTurn.Run(state, rules, world, trace, rng ?? SplitMix64Rng.ForStream(1, "ai.turn"), sink), trace);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_earlier_army_finishes_approach_and_contact_before_a_later_higher_score_only_in_Faithful(bool faithful)
    {
        var (state, rules, world) = Fixture(faithful);
        var (outcome, trace) = Run(state, rules, world);
        var orders = trace.Commands.Where(c => c is MoveArmyCommand or AttackArmyCommand).ToArray();
        if (faithful)
        {
            Assert.Equal("z-first", Assert.IsType<MoveArmyCommand>(orders[0]).ArmyId);
            Assert.Equal("z-first", Assert.IsType<AttackArmyCommand>(orders[1]).AttackerArmyId);
            Assert.Equal("a-later", Assert.IsType<AttackArmyCommand>(orders[2]).AttackerArmyId);
        }
        else Assert.Equal("a-later", Assert.IsType<AttackArmyCommand>(orders[0]).AttackerArmyId);
        Assert.True(outcome.CommandsRejected == 0, string.Join("\n", outcome.Log));
        Assert.Equal(0, outcome.ProjectionMismatches);
        var replay = Run(state, rules, world);
        Assert.Equal(trace.Commands, replay.Trace.Commands);
        Assert.Equal(outcome.State, replay.Outcome.State);
    }

    [Fact]
    public void Reversing_the_table_changes_order_without_sorting_ids()
    {
        var (state, rules, world) = Fixture(true);
        state = state with { Armies = ValueList.From(new[] { state.Armies[1], state.Armies[0] }.Concat(state.Armies.Skip(2))) };
        var result = Run(state, rules, world);
        Assert.Equal("a-later", Assert.IsType<AttackArmyCommand>(result.Trace.Commands[0]).AttackerArmyId);
        Assert.Equal(0, result.Outcome.CommandsRejected);
    }

    [Fact]
    public void A_later_army_reads_the_target_destroyed_by_the_earlier_army()
    {
        var (state, rules, world) = Fixture(true);
        state = state with { Armies = ValueList.From(state.Armies.Where(a => a.Id != "target-later")
            .Select(a => a.Id == "a-later" ? a with { X = 228 } : a)) };
        var result = Run(state, rules, world);
        var attack = Assert.Single(result.Trace.Commands.OfType<AttackArmyCommand>());
        Assert.Equal(("z-first", "target-first"), (attack.AttackerArmyId, attack.TargetArmyId));
        Assert.True(result.Outcome.State.ArmyById("target-first") is not { TotalTroops: > 0 });
        var later = Assert.Single(result.Trace.Commands.OfType<MoveArmyCommand>(), c => c.ArmyId == "a-later");
        Assert.NotEqual((232, 80), (later.TargetX ?? later.X, later.TargetY ?? later.Y));
        Assert.Equal(0, result.Outcome.CommandsRejected);
    }

    [Fact]
    public void Zero_dead_and_foreign_records_do_not_prevent_the_next_own_army()
    {
        var (state, rules, world) = Fixture(true);
        var first = state.Armies[0];
        state = state with { Armies = ValueList.From(new[] {
            first with { Id = "empty", X = 10, Y = 10, Units = ValueList<UnitSlot>.Empty },
            first with { Id = "no-moves", Moves = 0 }, state.Armies[2]
        }.Concat(state.Armies.Where(a => a.Id != "target-first"))) };
        var result = Run(state, rules, world);
        Assert.Equal("z-first", Assert.IsType<MoveArmyCommand>(result.Trace.Commands[0]).ArmyId);
        Assert.DoesNotContain(result.Trace.Commands.OfType<MoveArmyCommand>(), c => c.ArmyId is "empty" or "no-moves" or "target-first");
        Assert.Equal(0, result.Outcome.CommandsRejected);
    }

    private sealed class SeatDraws(IRng inner) : IRng
    {
        public int Draws { get; private set; }
        public ulong Seed => inner.Seed;
        public ulong State => inner.State;
        public ulong NextUInt64() { Draws++; return inner.NextUInt64(); }
        public int NextInt(int bound) { Draws++; return inner.NextInt(bound); }
        public int NextInt(int min, int max) { Draws++; return inner.NextInt(min, max); }
        public bool NextChance(int n, int d) { Draws++; return inner.NextChance(n, d); }
        public IRng ForStream(string name) => inner.ForStream(name);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Equal_marches_do_not_draw_a_cross_army_tie_in_Faithful(bool faithful)
    {
        var (state, rules, world) = Fixture(faithful);
        state = state with { Armies = ValueList.From(state.Armies.Select(a => a.Id == "a-later" ? a with { X = 249 } : a)) };
        var rng = new SeatDraws(SplitMix64Rng.ForStream(1, "ai.turn"));
        var result = Run(state, rules, world, rng);
        if (faithful) Assert.Equal(0, rng.Draws);
        else Assert.True(rng.Draws > 0);
        Assert.Equal(0, result.Outcome.CommandsRejected);
    }

    [Fact]
    public void The_existing_action_safety_cap_remains_in_force()
    {
        var (state, rules, world) = Fixture(true);
        rules = rules with { Ai = rules.Ai with { MaxActionsPerTurn = 1 } };
        var result = Run(state, rules, world);
        Assert.True(result.Outcome.HitActionCap);
        Assert.Equal("z-first", Assert.IsType<MoveArmyCommand>(Assert.Single(result.Trace.Commands)).ArmyId);
        Assert.Equal(0, result.Outcome.CommandsRejected);
    }

    [Fact]
    public void Initial_dead_slots_keep_the_live_armys_physical_table_position()
    {
        var (state, rules, world) = Fixture(true);
        var result = Run(state with { DeadArmySlots = ValueList.Of(0) }, rules, world);
        Assert.Equal("z-first", Assert.IsType<MoveArmyCommand>(result.Trace.Commands[0]).ArmyId);
        Assert.Contains(result.Trace.Commands.OfType<AttackArmyCommand>(), c => c.AttackerArmyId == "a-later");
        Assert.Equal(0, result.Outcome.CommandsRejected);
    }

    [Fact]
    public void A_first_armys_defeat_does_not_skip_the_next_table_record()
    {
        var (state, rules, world) = Fixture(true);
        state = state with { Armies = ValueList.From(state.Armies.Where(a => a.Id != "target-later").Select(a => a.Id == "target-first"
            ? a with { X = 231, Units = ValueList.Of(F.Unit("light_infantry", 26000)) } : a)) };
        var result = Run(state, rules, world);
        var attacks = result.Trace.Commands.OfType<AttackArmyCommand>().ToArray();
        Assert.Equal("z-first", attacks[0].AttackerArmyId);
        Assert.True(result.Trace.Commands.OfType<MoveArmyCommand>().Any(c => c.ArmyId == "a-later"),
            string.Join("\n", result.Outcome.Log) + "\n" + string.Join("\n", result.Trace.Commands));
        Assert.Equal(0, result.Outcome.CommandsRejected);
        Assert.True(result.Outcome.State.ArmyById("z-first") is not { TotalTroops: > 0 });
    }

    [CommandHandler]
    public sealed class AppendAfterMove : ICommandHandler<MoveArmyCommand>
    {
        public CommandOutcome Handle(MoveArmyCommand command, CommandContext context)
        {
            var outcome = new MoveArmyCommandHandler().Handle(command, context);
            if (outcome.AcceptedState is not { } state || state.ArmyById("appended") is not null) return outcome;
            var appended = F.Army("appended", command.IssuingNationId, 245, 85, 80, F.Unit("light_infantry", 25000))
                with { Moves = 8, Money = 500, SupplyTons = 500 };
            return CommandOutcome.Accept(state with { Armies = ValueList.From(state.Armies.Append(appended)) });
        }
    }

    [Fact]
    public void The_entry_count_excludes_an_army_appended_during_execution()
    {
        var (state, rules, world) = Fixture(true);
        var engine = typeof(SystemRegistry).Assembly;
        var registry = SystemRegistry.FromAssemblies([engine, typeof(AppendAfterMove).Assembly],
            type => type == typeof(AppendAfterMove) || (type.Assembly == engine && type != typeof(MoveArmyCommandHandler)));
        var sink = new RecordingEventSink();
        var trace = new Trace(new CommandDispatcher(registry, rules, world, sink));
        var outcome = AiTurn.Run(state, rules, world, trace, SplitMix64Rng.ForStream(1, "ai.turn"), sink);
        Assert.True(outcome.State.ArmyById("appended")!.Moves == 8,
            string.Join("\n", outcome.Log) + "\n" + string.Join("\n", trace.Commands));
        Assert.DoesNotContain(trace.Commands.OfType<MoveArmyCommand>(), c => c.ArmyId == "appended");
        Assert.DoesNotContain(trace.Commands.OfType<AttackArmyCommand>(), c => c.AttackerArmyId == "appended");
        Assert.Equal(0, outcome.CommandsRejected);
    }
}
