using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// The commands and handlers the dispatch tests use, and the rejection codes one of them declares.
/// </summary>
/// <remarks>
/// <see cref="TestRejections"/> is the shape every later task is meant to copy: a task declares its own
/// codes, in its own directory, on its own class. Nothing central is edited, which is why
/// <see cref="RejectionCode"/> is an open string-keyed type rather than an enum.
/// </remarks>
public static class CommandFixtures
{
    /// <summary>The fixture group these commands and handlers belong to.</summary>
    public const string Group = "commands";
}

/// <summary>Rejection codes owned by this fixture, exactly as a later task would own its own.</summary>
public static class TestRejections
{
    /// <summary>The order asked for more than the army could carry.</summary>
    public static readonly RejectionCode OverCapacity = new("test.over-capacity");
}

/// <summary>A command whose handler always accepts, moving money out of the nation's treasury.</summary>
public sealed record LevyCommand(string IssuingNationId, int Talents) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "test.levy";
}

/// <summary>A command whose handler always refuses — the illegal order this task's DoD item 3 is about.</summary>
public sealed record OverloadCommand(string IssuingNationId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "test.overload";
}

/// <summary>A command with no registered handler at all.</summary>
public sealed record UnclaimedCommand(string IssuingNationId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "test.unclaimed";
}

/// <summary>Accepts, and publishes an event as a real handler would.</summary>
[TestFixtureGroup(CommandFixtures.Group)]
[CommandHandler]
public sealed class LevyCommandHandler : ICommandHandler<LevyCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(LevyCommand command, CommandContext context)
    {
        var nations = context.State.Nations.Select(nation =>
            string.Equals(nation.Id, command.IssuingNationId, StringComparison.Ordinal)
                ? nation with { Treasury = nation.Treasury + command.Talents }
                : nation);

        context.Events.Publish(new TestLevyCollected(command.IssuingNationId, command.Talents));
        return CommandOutcome.Accept(context.State with { Nations = ValueList.From(nations) });
    }
}

/// <summary>
/// Refuses, with a typed code — and publishes an event first, on purpose. The dispatcher must discard
/// that event, so a refused order leaves no trace in the news log any more than it leaves one in the
/// state.
/// </summary>
[TestFixtureGroup(CommandFixtures.Group)]
[CommandHandler]
public sealed class OverloadCommandHandler : ICommandHandler<OverloadCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(OverloadCommand command, CommandContext context)
    {
        context.Events.Publish(new TestLevyCollected(command.IssuingNationId, 0));

        return CommandOutcome.Reject(
            TestRejections.OverCapacity,
            $"'{command.IssuingNationId}' asked to load more than the transport could carry.");
    }
}

/// <summary>A levy was collected.</summary>
[DomainEvent("test.levy-collected", NewsWorthy = true)]
public sealed record TestLevyCollected(string NationId, int Talents) : DomainEvent;
