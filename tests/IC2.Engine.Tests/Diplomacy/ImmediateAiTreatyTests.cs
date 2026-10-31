using IC2.Engine.Ai;
using IC2.Engine.Battle;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using IC2.Engine.Tests.Ai;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using F = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Diplomacy;

public sealed class ImmediateAiTreatyTests
{
    [DomainEvent("test.treaty-relation-observed")]
    public sealed record ObservedRelation(int Value) : DomainEvent;

    [GameSystem(TurnPhase.Orders, "test.immediate-treaty.attack")]
    public sealed class AttackInOrders : IGameSystem
    {
        public GameState Execute(SystemContext context)
        {
            var result = context.Commands.Dispatch(context.State, new AttackArmyCommand("north", "actor", "target"));
            if (result.IsRejected) throw new InvalidOperationException(result.Rejection!.Message);
            context.Events.Publish(new ObservedRelation(result.State.Relations.Get("north", "south")));
            if (result.State.ArmyById("actor2") is not null)
            {
                result = context.Commands.Dispatch(result.State, new AttackArmyCommand("north", "actor2", "target2"));
                if (result.IsRejected) throw new InvalidOperationException(result.Rejection!.Message);
            }
            return result.State;
        }
    }
    internal static (GameState State, Ruleset Rules, World World) Fixture(bool faithful)
    {
        var toy = AiScriptedStates.Ruleset;
        var rules = toy with { Flags = toy.Flags with { AiEconomy = faithful ? AiEconomyModel.Original : AiEconomyModel.Designed },
            Combat = toy.Combat with { AutoPeaceChanceNumerator = toy.Combat.AutoPeaceChanceDenominator } };
        var world = AiScriptedStates.World with { Id = "classical-mediterranean", Width = 320, Height = 140,
            Terrain = new(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(2, 320 * 140))) };
        var cities = new[] { F.City("home", "Home", 200, 80, "north", "north", 80, 100, 100, 100, 1),
            F.City("offer", "Offer", 232, 80, "south", "south", 80, 75, 10, 10, 1) }
            .Concat(F.FillerCities("south", 9, 260, 90));
        var state = BattleCommandTestbed.StateWith(
            [F.Nation("north", unity: 900, capitalCityId: "home"), F.Nation("south", unity: 900), F.Nation("third")], cities,
            [F.Army("actor", "north", 230, 80, 80, F.Unit("light_infantry", 10000)) with { Moves = 8, Money = 500, SupplyTons = 500 },
             F.Army("target", "south", 231, 80, 80, F.Unit("light_infantry", 1000)),
             F.Army("reserve", "south", 280, 100, 80, F.Unit("light_infantry", 50000))]);
        state = AiScriptedStates.WithActiveSeat(state, "north");
        state = state with { Relations = state.Relations.WithRelation("north", "south", rules.Diplomacy.StateCodes.War)
                .WithRelation("north", "third", rules.Diplomacy.StateCodes.War),
            MercenaryPool = ValueList.Of(new MercenaryPoolSlot(0, 232, 80, 1, "light_infantry", 1000, 6)) };
        return (state, rules, world);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_real_AI_battle_treaty_is_visible_to_the_following_hire_only_in_Faithful(bool faithful)
    {
        var (state, rules, world) = Fixture(faithful);
        var sink = new RecordingEventSink();
        var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), rules, world, sink);
        var result = dispatch.Dispatch(state, new AttackArmyCommand("north", "actor", "target"));
        Assert.True(result.IsAccepted, result.Rejection?.Message);
        Assert.Single(sink.Events.OfType<PeaceTreatyTriggered>()); // A genuine resolver trigger, no injected offer.
        Assert.Equal(faithful ? rules.Diplomacy.CooldownAfterEndedWar : rules.Diplomacy.StateCodes.War,
            result.State.Relations.Get("north", "south"));
        var hire = AiMercenaryHirePass.Run(result.State, rules, "north");
        Assert.Equal(faithful ? 1 : 0, hire.OffersHired);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void SeatEnd_consumes_each_trigger_once_and_preserves_destroyed_then_treaty_news(bool faithful, bool two)
    {
        var (state, rules, world) = Fixture(faithful);
        if (two)
        {
            state = state with { Cities = ValueList.From(state.Cities.Concat(F.FillerCities("third", 9, 280, 110))),
                Armies = ValueList.From(state.Armies.Concat(new[] {
                    F.Army("actor2", "north", 240, 80, 80, F.Unit("light_infantry", 10000)) with { Moves = 8 },
                    F.Army("target2", "third", 241, 80, 80, F.Unit("light_infantry", 1000)),
                    F.Army("reserve2", "third", 290, 110, 80, F.Unit("light_infantry", 50000))
                })) };
        }
        var engine = typeof(SystemRegistry).Assembly;
        var registry = SystemRegistry.FromAssemblies([engine, typeof(AttackInOrders).Assembly],
            type => type == typeof(AttackInOrders) || (type.Assembly == engine && type != typeof(AiTurnSystem)));
        var sink = new RecordingEventSink();
        var dispatch = new CommandDispatcher(registry, rules, world, sink);
        var result = new TurnCoordinator(registry, rules, world, sink, dispatch).RunTurn(state);
        Assert.Equal(faithful ? rules.Diplomacy.CooldownAfterEndedWar : rules.Diplomacy.StateCodes.War,
            Assert.Single(result.Events.OfType<ObservedRelation>()).Value);
        Assert.Equal(two ? 2 : 1, result.Events.OfType<PeaceTreatyTriggered>().Count());
        Assert.Equal(faithful ? (two ? 2 : 1) : 0, result.Events.OfType<AiPeaceTreatyApplied>().Count());
        Assert.Equal(rules.Diplomacy.CooldownAfterEndedWar, result.State.Relations.Get("north", "south"));
        if (two) Assert.Equal(rules.Diplomacy.CooldownAfterEndedWar, result.State.Relations.Get("north", "third"));
        var lines = result.State.NewsLog.Slots.Select(e => e.Text).ToList();
        Assert.Equal(two ? 2 : 1, lines.Count(l => l.Contains("destroys army of", StringComparison.Ordinal)));
        var endings = lines.Select((s, i) => (s, i)).Where(x => x.s.Contains("sues", StringComparison.Ordinal)
            || x.s.Contains("have agreed to end their war", StringComparison.Ordinal)).ToArray();
        Assert.Equal(two ? 2 : 1, endings.Length);
        Assert.All(endings, x => Assert.Contains("destroys army of", lines[x.i - 1]));
    }
}
