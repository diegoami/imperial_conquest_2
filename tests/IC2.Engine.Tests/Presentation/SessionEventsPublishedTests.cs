using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// T149 (correction task for bug #790) Done-when 1, the <c>SessionEventsPublishedTests</c> half:
/// the new <see cref="GameSession.EventsPublished"/> event is the cue list's only source of
/// per-Submit events, so it must raise exactly once per <c>Submit</c> with the events the
/// call published, in publication order, and it must leave every line and every state change
/// exactly the same as a call without a subscriber. Sol's review of PR 793 (R1, R2, R4):
/// "a command submitted from any screen, and every seat an end of turn runs" must reach the
/// subscriber, in seat order, with the same lines and the same state.
/// </summary>
public sealed class SessionEventsPublishedTests
{
    private static GameSession NewSession(ulong? seed = null) =>
        new(CoreTestbed.Toy.World, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.Scenario, seed);

    /// <summary>
    /// T149 DoD 1, first half: one <c>Submit</c> of a human command raises <c>EventsPublished</c>
    /// exactly once, with the command's events. A move is the simplest accepted dispatch; its
    /// sole event is <see cref="ArmyMoved"/>, and a no-op "move to current tile" still raises
    /// once with whatever the walker does (the walker returns without moving, so
    /// <see cref="ArmyMoved"/> is not published -- the assertion targets the raise count, not
    /// the event list).
    /// </summary>
    [Fact]
    public void Human_command_submit_raises_EventsPublished_once_with_the_command_events()
    {
        var session = NewSession();
        var raises = new List<IReadOnlyList<DomainEvent>>();
        session.EventsPublished += events => raises.Add(events);

        // A move of north-army-1 from (3,2) to (3,3): one tile east, accepted by MoveArmyCommandHandler.
        session.Submit("move north-army-1 3 3");

        Assert.Single(raises);
        Assert.Contains(raises[0], e => e is ArmyMoved);
    }

    /// <summary>
    /// T149 DoD 1, second half: one <c>end</c> in a game with two AI seats raises
    /// <c>EventsPublished</c> exactly once, with both seats' <c>RunTurn</c> events in seat order.
    /// The toy scenario's two nations are flipped to <c>ai</c> (a watch-mode save, the same
    /// construction the Godot check uses for a hand-shaped state), so the single <c>end</c> runs
    /// both seats' AI turns. Sol's rework review of PR #886 (R5): the assertion pins the seat
    /// identities and the batch order, so reversing the two AI batches cannot leave this green.
    /// </summary>
    [Fact]
    public void End_of_turn_with_two_AI_seats_raises_EventsPublished_with_both_seats_events_in_seat_order()
    {
        var toy = CoreTestbed.Toy;
        var initial = GameStateFactory.CreateInitial(toy.World, toy.Ruleset, toy.Scenario);
        var allAi = initial with
        {
            Nations = ValueList.From(
                initial.Nations.Select(n => n with { Control = SeatControl.Ai }).ToList()),
        };
        var save = new SaveGame(
            SchemaVersion: allAi.SchemaVersion, Id: "two-ai-seats", Label: "Two AI seats",
            ScenarioId: allAi.ScenarioId, WorldId: allAi.WorldId, RulesetId: allAi.RulesetId,
            State: allAi);
        var session = new GameSession(toy.World, toy.Ruleset, toy.Scenario, save);

        var raises = new List<IReadOnlyList<DomainEvent>>();
        session.EventsPublished += events => raises.Add(events);

        session.Submit("end");

        Assert.Single(raises);
        var events = raises[0];

        // The AiTurnDecided events appear once per AI seat, in the scenario's seat order
        // (the same order PlayUntilOneFullLapOrRepeat walked), never in another order.
        var expectedSeatOrder = toy.Scenario.Seats.Select(s => s.Nation).ToList();
        var actualSeatOrder = events
            .Where(e => e is AiTurnDecided)
            .Cast<AiTurnDecided>()
            .Select(e => e.NationId)
            .ToList();
        Assert.Equal(expectedSeatOrder, actualSeatOrder);

        // Batch order (R5): every event between two consecutive AiTurnDecided events belongs to
        // the seat whose AiTurnDecided closes its batch -- the AI seat's own RunTurn published
        // its commands' events first and its AiTurnDecided last. Each seat's events are
        // contiguous and carry that seat's NationId, so a reversal of the two AI batches fails.
        var batchEnds = new List<(int Index, string Nation)>();
        for (var index = 0; index < events.Count; index++)
        {
            if (events[index] is AiTurnDecided decided)
            {
                batchEnds.Add((index, decided.NationId));
            }
        }

        Assert.Equal(expectedSeatOrder.Count, batchEnds.Count);
        var batchStart = 0;
        foreach (var (endIndex, nation) in batchEnds)
        {
            for (var index = batchStart; index <= endIndex; index++)
            {
                var nationId = events[index].GetType().GetProperty("NationId")?.GetValue(events[index]) as string;
                Assert.Equal(nation, nationId);
            }

            batchStart = endIndex + 1;
        }
    }

    /// <summary>
    /// T149 DoD 1, third half: the session's output lines and state are identical with and
    /// without a subscriber. Two sessions, one subscribed and one not, run the same
    /// <c>Submit</c> calls and produce the same lines and the same state -- the accumulator
    /// reads <see cref="CommandResult.Events"/> and <see cref="TurnResult.Events"/>, never
    /// re-derives anything the engine already publishes.
    /// </summary>
    [Fact]
    public void Lines_and_state_are_identical_with_and_without_a_subscriber()
    {
        var withSubscriber = NewSession();
        var subscriberCalls = 0;
        withSubscriber.EventsPublished += _ => subscriberCalls++;

        var withoutSubscriber = NewSession();

        // Run the same script through both: the move, the buy, and the end.
        var commands = new[] { "move north-army-1 3 3", "end" };
        var withLines = new List<string>();
        var withoutLines = new List<string>();
        foreach (var command in commands)
        {
            var withOutput = withSubscriber.Submit(command);
            var withoutOutput = withoutSubscriber.Submit(command);
            withLines.AddRange(withOutput.Lines);
            withoutLines.AddRange(withoutOutput.Lines);
        }

        Assert.Equal(withoutLines, withLines);
        Assert.True(subscriberCalls >= commands.Length,
            $"expected the subscriber to be raised at least once per Submit; got {subscriberCalls}");

        // State must match on every command's state: the same moves, the same cities, the same
        // news log. Compare the two states after each Submit rather than only at the end, so
        // a divergence at the first command is named where it happens.
        var withState = withSubscriber.State;
        var withoutState = withoutSubscriber.State;
        Assert.Equal(withoutState.Armies, withState.Armies);
        Assert.Equal(withoutState.Cities, withState.Cities);
        Assert.Equal(withoutState.Fleets, withState.Fleets);
        Assert.Equal(withoutState.Nations, withState.Nations);
        Assert.Equal(withoutState.NewsLog.Slots, withState.NewsLog.Slots);
    }
}
