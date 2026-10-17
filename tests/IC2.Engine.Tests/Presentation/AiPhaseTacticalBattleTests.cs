using System.Reflection;
using IC2.Engine.Ai;
using IC2.Engine.Battle;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// T126: with <see cref="HumanBattleMode.Tactical"/> an AI seat's attack on a human's army opens the battle during the
/// AI phase, and the AI seat's turn resumes from the written-back state when it ends. The toy scenario's human is
/// <c>north</c>, its AI is <c>south</c>; the human declares war first, so the attack is the AI's own choice.
/// </summary>
public sealed class AiPhaseTacticalBattleTests
{
    /// <summary>Two south armies each next to one of north's, so the AI attacks twice in its one turn.</summary>
    private static List<StartingArmy> TwoPairs() => new()
    {
        TacticalSessionTestbed.Army(
            "north-army-1", "north", 3, 2, 68, TacticalSessionTestbed.Unit("light_infantry", 400, 6, "1st Foot")),
        TacticalSessionTestbed.Army(
            "north-army-2", "north", 6, 4, 68, TacticalSessionTestbed.Unit("light_infantry", 400, 6, "2nd Foot")),
        TacticalSessionTestbed.Army(
            "south-army-1", "south", 4, 2, 59, TacticalSessionTestbed.Unit("heavy_infantry", 6000, 6, "Guards")),
        TacticalSessionTestbed.Army(
            "south-army-2", "south", 6, 5, 59, TacticalSessionTestbed.Unit("heavy_infantry", 6000, 6, "Pikes")),
    };

    private static GameSession AtWar(HumanBattleMode mode, IEnumerable<StartingArmy>? armies = null)
    {
        var session = TacticalSessionTestbed.ToySession(mode, armies: armies ?? TacticalSessionTestbed.WeakAttacker());
        Assert.Contains("accepted", session.Submit("declare-war south").Lines[1]);
        return session;
    }

    [Fact]
    public void An_attack_on_the_humans_army_opens_the_battle_with_the_human_to_place_and_the_resumed_turn_matches_the_reference()
    {
        var session = AtWar(HumanBattleMode.Tactical);

        var opened = session.Submit("end");

        // The battle is open, the AI seat is still the active seat and its turn has not been committed.
        Assert.NotNull(opened.OpenBattleView);
        Assert.Equal("south-army-1", opened.OpenBattleView!.AttackerArmyId);
        Assert.Equal("north-army-1", opened.OpenBattleView.DefenderArmyId);
        Assert.Equal("north", opened.OpenBattleView.DefenderNationId);
        Assert.False(opened.OpenBattleView.Placed);
        Assert.Equal(TacticalBattleState.DefenderSide, opened.OpenBattleView.SideToMove);
        Assert.Contains(opened.Lines, l => l.Contains("the battle opens on the board", StringComparison.Ordinal));
        Assert.Equal("south", session.State.ActiveNationId);
        Assert.Equal(TacticalFrameKind.Initial, opened.TacticalTimeline[0].Kind);
        Assert.Equal(opened.OpenBattleView, opened.TacticalTimeline[^1].After.View);
        Assert.All(opened.TacticalTimeline, f => Assert.Equal(0, f.CommandId));
        Assert.Empty(session.Submit("battle show").TacticalTimeline);
        var preTurn = session.State;

        var events = new List<DomainEvent>();
        session.EventsPublished += batch => events.AddRange(batch);
        var closed = session.Submit("battle general on");

        Assert.Null(closed.OpenBattleView);
        Assert.NotNull(closed.EndedBattleReport);
        Assert.Contains(closed.TacticalTimeline, f => f.Kind == TacticalFrameKind.Terminal);
        Assert.Equal(TacticalFrameKind.Reconciliation, closed.TacticalTimeline[^1].Kind);
        Assert.Equal(opened.TacticalTimeline[0].BattleId, closed.TacticalTimeline[^1].BattleId);
        Assert.Equal("north", session.State.ActiveNationId);
        Assert.Contains(closed.Lines, l => l.StartsWith("Now: Week", StringComparison.Ordinal));
        Assert.Contains(closed.Lines, l => l.Contains("takes its turn", StringComparison.Ordinal));

        var reference = ReferenceTurn(session, preTurn, humanBattles: true);
        Assert.Equal(reference.State, session.State);
        Assert.Equal(DecidedLines(reference.Events), DecidedLines(events));
        Assert.Single(reference.BattlesFought);
    }

    [Fact]
    public void Two_attacks_on_the_human_in_one_turn_open_two_battles_in_order()
    {
        var session = AtWar(HumanBattleMode.Tactical, TwoPairs());

        var first = session.Submit("end");
        Assert.NotNull(first.OpenBattleView);
        var firstView = first.OpenBattleView!;
        var preTurn = session.State;
        Assert.Equal("south", preTurn.ActiveNationId);

        var second = session.Submit("battle general on");
        Assert.NotNull(second.EndedBattleReport);
        Assert.NotNull(second.OpenBattleView);
        var secondView = second.OpenBattleView!;
        var nextOpening = Assert.Single(second.TacticalTimeline, f => f.Kind == TacticalFrameKind.Initial);
        Assert.NotEqual(first.TacticalTimeline[0].BattleId, nextOpening.BattleId);
        Assert.Contains(second.TacticalTimeline, f => f.Kind == TacticalFrameKind.Terminal && f.BattleId == first.TacticalTimeline[0].BattleId);
        Assert.Equal(secondView, second.TacticalTimeline[^1].After.View);
        Assert.NotEqual(firstView.DefenderArmyId, secondView.DefenderArmyId);
        Assert.Equal("south", session.State.ActiveNationId);
        Assert.Equal(preTurn, session.State);

        var events = new List<DomainEvent>();
        session.EventsPublished += batch => events.AddRange(batch);
        var last = session.Submit("battle general on");
        Assert.Null(last.OpenBattleView);
        Assert.Equal("north", session.State.ActiveNationId);

        var reference = ReferenceTurn(session, preTurn, humanBattles: true);
        Assert.Equal(reference.State, session.State);
        Assert.Equal(DecidedLines(reference.Events), DecidedLines(events));
        Assert.Equal(
            new[] { firstView.DefenderArmyId, secondView.DefenderArmyId },
            reference.BattlesFought.Select(b => b.Defender).ToArray());
    }

    [Fact]
    public void Under_instant_the_same_scenario_resolves_at_once_as_the_plain_pipeline_does()
    {
        var tactical = AtWar(HumanBattleMode.Tactical);
        tactical.Submit("end");
        var preTurn = tactical.State;

        var instant = AtWar(HumanBattleMode.Instant);
        var output = instant.Submit("end");

        Assert.Null(output.OpenBattleView);
        Assert.Single(output.Battles);
        var reference = ReferenceTurn(instant, preTurn, humanBattles: false);
        Assert.Equal(reference.State, instant.State);
        Assert.Single(output.Lines, l => l.Contains("takes its turn", StringComparison.Ordinal));
    }

    [Fact]
    public void An_ai_versus_ai_battle_in_the_same_turn_resolves_at_once_in_both_modes()
    {
        var toy = CoreTestbed.Toy;
        var codes = toy.Ruleset.Diplomacy.StateCodes;
        var east = toy.World.NationById("south")! with { Id = "east", Name = "Eastern League", CapitalCityId = "orient" };
        var world = toy.World with
        {
            Nations = ValueList.Of(toy.World.NationById("north")!, toy.World.NationById("south")!, east),
            Cities = ValueList.Of(
                toy.World.CityById("arx")!,
                toy.World.CityById("portus")!,
                toy.World.CityById("meridia")!,
                toy.World.CityById("meridia")! with { Id = "orient", Name = "Orient", X = 7, Y = 0, Owner = "east", Allegiance = "east" }),
            StartingArmies = ValueList.From(new List<StartingArmy>
            {
                TacticalSessionTestbed.Army(
                    "south-army-1", "south", 4, 2, 59, TacticalSessionTestbed.Unit("heavy_infantry", 6000, 6, "Guards")),
                TacticalSessionTestbed.Army(
                    "east-army-1", "east", 5, 2, 68, TacticalSessionTestbed.Unit("light_infantry", 400, 6, "1st Foot")),
            }),
            StartingFleets = ValueList<StartingFleet>.Empty,
            TurnOrder = ValueList.Of("north", "south", "east"),
            StartingRelations = DiplomaticRelations.Uniform(ValueList.Of("north", "south", "east"), codes.Peace)
                .WithRelation("south", "east", codes.War),
        };
        var scenario = toy.Scenario with
        {
            Seats = ValueList.Of(
                toy.Scenario.Seats[0],
                toy.Scenario.Seats[1],
                toy.Scenario.Seats[1] with { Nation = "east" }),
        };

        var outputs = new List<SessionOutput>();
        var states = new List<GameState>();
        foreach (var mode in new[] { HumanBattleMode.Tactical, HumanBattleMode.Instant })
        {
            var session = new GameSession(world, toy.Ruleset, scenario, seedOverride: 1) { HumanBattles = mode };
            outputs.Add(session.Submit("end"));
            states.Add(session.State);
        }

        foreach (var output in outputs)
        {
            Assert.Null(output.OpenBattleView);
            Assert.Contains(output.Battles, b => b.AttackerNationId == "south" && b.DefenderNationId == "east");
        }

        Assert.Equal(states[1], states[0]);
        Assert.Equal(outputs[1].Lines, outputs[0].Lines);
    }

    [Fact]
    public void Save_is_refused_while_the_battle_is_open_and_allowed_again_after_it()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ic2-t126-{Guid.NewGuid():N}.json");
        try
        {
            var session = AtWar(HumanBattleMode.Tactical);
            session.Submit("end");
            var before = session.State;

            var refused = session.Submit($"save {path}");

            Assert.Contains(refused.Lines, l => l == "Finish the battle first: the turn is in progress.");
            Assert.False(File.Exists(path));
            Assert.Equal(before, session.State);
            Assert.NotNull(refused.OpenBattleView);

            session.Submit("battle general on");
            var saved = session.Submit($"save {path}");

            Assert.Contains(saved.Lines, l => l.StartsWith("Saved to", StringComparison.Ordinal));
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    // ---- the reference: the same pre-turn state, the same battle result injected at the same attack ----

    private sealed record Reference(
        GameState State,
        IReadOnlyList<DomainEvent> Events,
        IReadOnlyList<(string Attacker, string Defender)> BattlesFought);

    private static Reference ReferenceTurn(GameSession session, GameState preTurn, bool humanBattles)
    {
        var registry = SystemRegistry.FromEngineAssembly();
        var inner = new CommandDispatcher(registry, session.Ruleset, session.World, NullEventSink.Instance);
        var fought = new List<(string Attacker, string Defender)>();
        ICommandDispatch dispatch = humanBattles ? new ReferenceDispatch(inner, session.Ruleset, fought) : inner;
        var coordinator = new TurnCoordinator(registry, session.Ruleset, session.World, NullEventSink.Instance, dispatch);
        var result = coordinator.RunTurn(preTurn);
        return new Reference(result.State, result.Events, fought);
    }

    /// <summary>Every decision line of the AI turn(s) in <paramref name="events"/>, command by command.</summary>
    private static string[] DecidedLines(IEnumerable<DomainEvent> events) =>
        events.OfType<AiTurnDecided>().SelectMany(e => e.Lines).ToArray();

    private sealed class ReferenceDispatch : ICommandDispatch
    {
        private readonly ICommandDispatch _inner;
        private readonly Ruleset _ruleset;
        private readonly List<(string Attacker, string Defender)> _fought;

        public ReferenceDispatch(ICommandDispatch inner, Ruleset ruleset, List<(string Attacker, string Defender)> fought)
        {
            _inner = inner;
            _ruleset = ruleset;
            _fought = fought;
        }

        public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, NullEventSink.Instance);

        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
        {
            if (command is AttackArmyCommand a
                && state.NationById(a.IssuingNationId)!.Control == SeatControl.Ai
                && state.ArmyById(a.TargetArmyId) is { } target
                && state.NationById(target.Nation)!.Control == SeatControl.Human
                && AttackLegality.Check(state, _ruleset, a) is null)
            {
                var battle = TacticalBattleSession.Open(state, a.AttackerArmyId, a.TargetArmyId, _ruleset, out var opened);
                battle.GeneralOn();
                Assert.True(battle.IsOver);
                var conclusion = battle.Conclude(opened, _ruleset);
                _fought.Add((a.AttackerArmyId, a.TargetArmyId));
                foreach (var e in conclusion.Events)
                {
                    events.Publish(e);
                }

                var accepted = typeof(CommandResult).GetMethod("Accepted", BindingFlags.NonPublic | BindingFlags.Static)!;
                return (CommandResult)accepted.Invoke(
                    null, new object[] { conclusion.State!, command.Kind, ValueList<DomainEvent>.Of(conclusion.Events.ToArray()) })!;
            }

            return _inner.Dispatch(state, command, events);
        }
    }
}
