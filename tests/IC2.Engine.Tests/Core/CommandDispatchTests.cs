using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// Definition of Done item 3: "An illegal command returns a typed rejection with a reason code; a test
/// asserts no exception is thrown and no state changed."
/// </summary>
public class CommandDispatchTests
{
    [Fact]
    public void An_illegal_command_is_rejected_with_a_code_and_changes_nothing()
    {
        var sink = new RecordingEventSink();
        var dispatcher = CoreTestbed.DispatcherFor(CommandFixtures.Group, sink);
        var before = CoreTestbed.InitialState();
        var beforeHash = GameStateHash.Compute(before);

        // Reaching the next line at all is the "no exception is thrown" half: an illegal order is an
        // outcome, never an error. Wrapped in Record.Exception as well so a throw fails with a message
        // about the contract rather than as an unhandled test error.
        CommandResult? result = null;
        var thrown = Record.Exception(() =>
            result = dispatcher.Dispatch(before, new OverloadCommand(before.ActiveNationId)));

        Assert.Null(thrown);
        Assert.NotNull(result);
        Assert.True(result!.IsRejected);
        Assert.Equal(TestRejections.OverCapacity, result.Code);
        Assert.NotEmpty(result.Rejection!.Message);

        // "No state changed", asserted three ways: the same object came back, it still compares equal to
        // the state that went in, and its canonical fingerprint is unchanged.
        Assert.Same(before, result.State);
        Assert.Equal(before, result.State);
        Assert.Equal(beforeHash, GameStateHash.Compute(result.State));

        // The handler published an event before refusing. A refused order leaves no trace anywhere.
        Assert.Empty(sink.Events);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void A_rejection_does_not_advance_the_random_stream()
    {
        var dispatcher = CoreTestbed.DispatcherFor(CommandFixtures.Group);
        var before = CoreTestbed.InitialState();

        var result = dispatcher.Dispatch(before, new OverloadCommand(before.ActiveNationId));

        // The root random value is the engine's only persisted randomness. A refused command must not
        // move it, or replaying a game from its command log would drift.
        Assert.Equal(before.RandomSeed, result.State.RandomSeed);
    }

    [Fact]
    public void A_command_with_no_handler_is_rejected_rather_than_ignored()
    {
        var dispatcher = CoreTestbed.DispatcherFor(CommandFixtures.Group);
        var before = CoreTestbed.InitialState();

        var result = dispatcher.Dispatch(before, new UnclaimedCommand(before.ActiveNationId));

        Assert.Equal(CoreRejections.NoHandler, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void A_command_from_a_seat_that_is_not_active_is_rejected()
    {
        var dispatcher = CoreTestbed.DispatcherFor(CommandFixtures.Group);
        var before = CoreTestbed.InitialState();
        var otherSeat = before.TurnOrder[(before.ActiveSeatIndex + 1) % before.TurnOrder.Count];

        var result = dispatcher.Dispatch(before, new OverloadCommand(otherSeat));

        // Checked by the dispatcher, before the handler runs, so no handler has to remember to.
        Assert.Equal(CoreRejections.NotActiveSeat, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void A_command_from_a_nation_that_does_not_exist_is_rejected()
    {
        var dispatcher = CoreTestbed.DispatcherFor(CommandFixtures.Group);
        var before = CoreTestbed.InitialState();

        var result = dispatcher.Dispatch(before, new OverloadCommand("no-such-nation"));

        Assert.Equal(CoreRejections.UnknownNation, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void A_command_from_an_eliminated_nation_is_rejected()
    {
        var dispatcher = CoreTestbed.DispatcherFor(CommandFixtures.Group);
        var initial = CoreTestbed.InitialState();
        var before = initial with
        {
            Nations = ValueList.From(initial.Nations.Select(nation =>
                string.Equals(nation.Id, initial.ActiveNationId, StringComparison.Ordinal)
                    ? nation with { Eliminated = true }
                    : nation)),
        };

        var result = dispatcher.Dispatch(before, new OverloadCommand(before.ActiveNationId));

        Assert.Equal(CoreRejections.NationEliminated, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void An_accepted_command_returns_the_new_state_its_events_and_an_advanced_seed()
    {
        var sink = new RecordingEventSink();
        var dispatcher = CoreTestbed.DispatcherFor(CommandFixtures.Group, sink);
        var before = CoreTestbed.InitialState();
        var issuer = before.ActiveNationId;
        var treasuryBefore = before.NationById(issuer)!.Treasury;

        // The amount is ruleset data, not a number chosen here.
        var levy = CoreTestbed.Toy.Ruleset.Economy.PurseCapPerUnit;
        var result = dispatcher.Dispatch(before, new LevyCommand(issuer, levy));

        Assert.True(result.IsAccepted);
        Assert.Null(result.Rejection);
        Assert.Equal(treasuryBefore + levy, result.State.NationById(issuer)!.Treasury);
        Assert.Equal(RngStreams.Advance(before.RandomSeed), result.State.RandomSeed);

        var published = Assert.Single(sink.Events);
        Assert.Equal("test.levy-collected", published.Kind);
        Assert.True(published.IsNewsWorthy);
        Assert.Equal(sink.Events.ToArray(), result.Events.ToArray());
    }

    [Fact]
    public void Two_dispatches_of_the_same_command_against_the_same_state_agree()
    {
        var dispatcher = CoreTestbed.DispatcherFor(CommandFixtures.Group);
        var before = CoreTestbed.InitialState();
        var levy = CoreTestbed.Toy.Ruleset.Economy.PurseCapPerUnit;

        var first = dispatcher.Dispatch(before, new LevyCommand(before.ActiveNationId, levy));
        var second = dispatcher.Dispatch(before, new LevyCommand(before.ActiveNationId, levy));

        Assert.Equal(GameStateHash.Compute(first.State), GameStateHash.Compute(second.State));
    }

    [Fact]
    public void A_structurally_invalid_command_is_a_contract_violation_and_throws()
    {
        var dispatcher = CoreTestbed.DispatcherFor(CommandFixtures.Group);
        var before = CoreTestbed.InitialState();

        // Deliberately distinguished from an illegal order: a blank issuing nation is a caller bug, and
        // turning it into a polite rejection would hide it. Documented on CommandDispatcher.
        Assert.Throws<ArgumentException>(() => dispatcher.Dispatch(before, new OverloadCommand("   ")));
        Assert.Throws<ArgumentNullException>(() => dispatcher.Dispatch(before, null!));
    }

    [Fact]
    public void A_rejection_code_must_be_a_dotted_lowercase_id()
    {
        // The convention is enforced once, at construction, so twenty tasks cannot drift into twenty
        // spellings of the same idea.
        Assert.Throws<ArgumentException>(() => new RejectionCode("OverCapacity"));
        Assert.Throws<ArgumentException>(() => new RejectionCode("nodot"));
        Assert.Throws<ArgumentException>(() => new RejectionCode(""));
        Assert.Equal("area.reason", new RejectionCode("area.reason").Value);
    }

    [Fact]
    public void A_system_can_play_its_seat_by_issuing_commands()
    {
        var sink = new RecordingEventSink();
        var coordinator = CoreTestbed.CoordinatorWithCommandsFor(CommandFixtures.Group, sink);
        var before = CoreTestbed.InitialState();
        var issuer = before.ActiveNationId;

        var result = coordinator.RunTurn(before);

        // The order went through the same dispatcher, and the same validation, a human seat's would.
        var levy = CoreTestbed.Toy.Ruleset.Economy.PurseCapPerUnit;
        Assert.Equal(
            before.NationById(issuer)!.Treasury + levy,
            result.State.NationById(issuer)!.Treasury);

        Assert.Contains(sink.Events, e => e.Kind == "test.levy-collected");
        Assert.DoesNotContain(sink.Events, e => e.Kind == "test.seat-agent-refused");

        // The finding this test used to miss: the dispatcher published to the external sink only, so
        // TurnResult.Events came back empty and every event from every order an AI seat ever placed was
        // lost. Both streams must carry it, and they must agree.
        Assert.Contains(result.Events, e => e.Kind == "test.levy-collected");
        Assert.Equal(
            sink.Events.Select(e => e.Kind).ToArray(),
            result.Events.Select(e => e.Kind).ToArray());
    }

    [Fact]
    public void A_commands_events_reach_TurnResult_even_with_a_null_external_sink()
    {
        // TurnCoordinator's own doc says "Pass NullEventSink.Instance if only TurnResult.Events is wanted;
        // the result carries its own copy either way". This is that exact configuration, asserted, so the
        // documented advice cannot silently stop being true.
        var coordinator = CoreTestbed.CoordinatorWithCommandsFor(CommandFixtures.Group);

        var result = coordinator.RunTurn(CoreTestbed.InitialState());

        var published = Assert.Single(result.Events);
        Assert.Equal("test.levy-collected", published.Kind);
    }

    [Fact]
    public void A_command_dispatched_outside_a_turn_still_publishes_to_the_dispatchers_own_sink()
    {
        // The other half of the binding: a dispatcher used standalone -- which is how T23's headless
        // harness will drive it -- keeps publishing to the sink it was constructed with.
        var sink = new RecordingEventSink();
        var dispatcher = CoreTestbed.DispatcherFor(CommandFixtures.Group, sink);
        var before = CoreTestbed.InitialState();

        dispatcher.Dispatch(before, new LevyCommand(before.ActiveNationId, 1));

        Assert.Contains(sink.Events, e => e.Kind == "test.levy-collected");
    }

    [Fact]
    public void A_rejection_needs_a_real_code()
    {
        // default(RejectionCode) skips the validating constructor -- C# does not let a struct suppress its
        // default value -- so the one place a handler produces a rejection catches it instead.
        Assert.True(default(RejectionCode).IsEmpty);
        Assert.Throws<ArgumentException>(() => CommandOutcome.Reject(default, "no code"));
    }

    [Fact]
    public void A_system_issuing_a_command_with_no_dispatcher_wired_up_is_refused_not_broken()
    {
        var sink = new RecordingEventSink();
        var coordinator = CoreTestbed.CoordinatorFor(CommandFixtures.Group, sink);
        var before = CoreTestbed.InitialState();
        var issuer = before.ActiveNationId;

        var result = coordinator.RunTurn(before);

        // A wiring mistake must not take a turn down: it comes back as a typed refusal, and the state is
        // exactly what it was.
        var refused = Assert.IsType<TestSeatAgentRefused>(
            Assert.Single(sink.Events, e => e.Kind == "test.seat-agent-refused"));

        Assert.Equal(CoreRejections.NoDispatcher.Value, refused.Code);
        Assert.Equal(before.NationById(issuer)!.Treasury, result.State.NationById(issuer)!.Treasury);
    }

    [Fact]
    public void Two_handlers_may_not_claim_the_same_command()
    {
        // Registration is by attribute, so a duplicate claim is possible to write; it fails loudly when
        // the registry is built rather than silently picking whichever the scan saw first.
        var error = Assert.Throws<InvalidOperationException>(
            () => CoreTestbed.RegistryFor(ConflictingCommandFixtures.Group));

        Assert.Contains("Two handlers claim the command", error.Message, StringComparison.Ordinal);
    }
}
