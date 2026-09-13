using IC2.Engine.Core;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// Two handlers that both claim the same command, in their own fixture group so no other test sees them.
/// </summary>
/// <remarks>
/// Attribute-based registration removes the shared registry file, and with it the place where a
/// duplicate claim used to be obvious at review time. The compensating control is that the registry
/// refuses to build — this fixture is what proves it does.
/// </remarks>
public static class ConflictingCommandFixtures
{
    /// <summary>The fixture group these handlers belong to.</summary>
    public const string Group = "conflicting-commands";
}

/// <summary>The contested command.</summary>
public sealed record ContestedCommand(string IssuingNationId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "test.contested";
}

/// <summary>One of two handlers claiming <see cref="ContestedCommand"/>.</summary>
[TestFixtureGroup(ConflictingCommandFixtures.Group)]
[CommandHandler]
public sealed class FirstContestedHandler : ICommandHandler<ContestedCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(ContestedCommand command, CommandContext context) =>
        CommandOutcome.Accept(context.State);
}

/// <summary>The other handler claiming <see cref="ContestedCommand"/>.</summary>
[TestFixtureGroup(ConflictingCommandFixtures.Group)]
[CommandHandler]
public sealed class SecondContestedHandler : ICommandHandler<ContestedCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(ContestedCommand command, CommandContext context) =>
        CommandOutcome.Accept(context.State);
}
