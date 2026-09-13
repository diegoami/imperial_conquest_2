namespace IC2.Engine.Core;

/// <summary>
/// Where systems publish <see cref="DomainEvent"/>s. The engine pushes; consumers buffer.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Push, not pull, and why.</strong> A pull model would have required the engine to retain every
/// event until someone asked for it, which means either an unbounded buffer or a policy decision about
/// how long to keep events — and that policy is precisely what T10's 40-slot ring buffer is. Pushing to
/// an interface lets each consumer own its own retention: T10 implements this interface with a ring
/// buffer that drops the oldest entry, the Godot UI implements it with whatever the screen needs, and a
/// test implements it with <see cref="RecordingEventSink"/>, which keeps everything. The engine holds no
/// buffer and makes no policy.
/// </para>
/// <para>
/// <strong>Ordering is part of the contract.</strong> Events arrive in the order they were published,
/// which is deterministic: phases run in declared order, systems run in declared order within a phase,
/// and every system is a sequential function. A sink that reorders (a concurrent queue, a
/// priority scheme) would break replay, so it must not.
/// </para>
/// </remarks>
public interface IEventSink
{
    /// <summary>Publishes one event.</summary>
    /// <param name="domainEvent">The event. Never null.</param>
    void Publish(DomainEvent domainEvent);
}

/// <summary>A sink that keeps everything it is given, in order. The default for tests and for replay.</summary>
public sealed class RecordingEventSink : IEventSink
{
    private readonly List<DomainEvent> _events = new();

    /// <summary>Everything published so far, oldest first.</summary>
    public IReadOnlyList<DomainEvent> Events => _events;

    /// <inheritdoc/>
    public void Publish(DomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _events.Add(domainEvent);
    }

    /// <summary>Returns everything published so far and starts again from empty.</summary>
    public IReadOnlyList<DomainEvent> Drain()
    {
        var drained = _events.ToArray();
        _events.Clear();
        return drained;
    }

    /// <summary>Forgets everything published so far.</summary>
    public void Clear() => _events.Clear();
}

/// <summary>A sink that discards everything. For a caller that genuinely does not care.</summary>
public sealed class NullEventSink : IEventSink
{
    /// <summary>The shared instance; the type is stateless.</summary>
    public static NullEventSink Instance { get; } = new();

    private NullEventSink()
    {
    }

    /// <inheritdoc/>
    public void Publish(DomainEvent domainEvent)
    {
    }
}

/// <summary>
/// Fans one publication out to several sinks in the order they were given — how the news log and the UI
/// both consume the same stream without either knowing about the other.
/// </summary>
public sealed class CompositeEventSink : IEventSink
{
    private readonly IEventSink[] _sinks;

    /// <summary>Creates a fan-out over the given sinks, which are notified in order.</summary>
    public CompositeEventSink(params IEventSink[] sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        _sinks = (IEventSink[])sinks.Clone();
        foreach (var sink in _sinks)
        {
            ArgumentNullException.ThrowIfNull(sink);
        }
    }

    /// <inheritdoc/>
    public void Publish(DomainEvent domainEvent)
    {
        foreach (var sink in _sinks)
        {
            sink.Publish(domainEvent);
        }
    }
}
