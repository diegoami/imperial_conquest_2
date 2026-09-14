namespace IC2.Engine.Core;

/// <summary>
/// One event exactly as it was published during the current run: what it was, which phase was running
/// when it was published, and the id of the system that published it.
/// </summary>
/// <remarks>
/// This is the T40 seam (<c>docs/task-catalogue.md</c> "Expose the run's published events to systems"):
/// it lets a stateless system read what earlier systems in the same run already published, without
/// reaching into another sink's private state and without a mutable static. See
/// <see cref="SystemContext.PublishedEvents"/> for where this is exposed and exactly what it covers.
/// </remarks>
/// <param name="Event">The event itself.</param>
/// <param name="Phase">The phase that was running when <paramref name="Event"/> was published.</param>
/// <param name="SystemId">
/// The declared id of the system that was running when <paramref name="Event"/> was published — whether
/// the system published it directly, through a command it issued, or through a quarter-boundary handler
/// its own phase fired. All three publish through the same run-bound sink, so all three are tagged the
/// same way: by whichever system was on the stack at the moment of publication.
/// </param>
public sealed record PublishedEvent(DomainEvent Event, TurnPhase Phase, string SystemId);
