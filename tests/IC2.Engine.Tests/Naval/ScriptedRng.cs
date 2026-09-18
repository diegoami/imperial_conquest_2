using IC2.Engine.Core;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// A deterministic <see cref="IRng"/> whose <see cref="NextInt(int)"/> calls return a pre-set queue of
/// values, in order — for pinning <see cref="Naval.FleetAttritionRule"/>'s branch-ordering precisely
/// (e.g. "the death check precedes the zero-supply penalty") without depending on a real seed's actual
/// draw sequence. Kept local to this task's own test directory rather than reusing
/// <c>IC2.Engine.Tests.Economy.ScriptedRng</c>, which is T08's Owns list, not this task's.
/// </summary>
public sealed class ScriptedRng : IRng
{
    private readonly Queue<int> _nextIntQueue;
    private readonly Queue<bool> _nextChanceQueue;

    /// <summary>Creates a scripted generator that returns <paramref name="nextIntValues"/> in order for every <see cref="NextInt(int)"/> call.</summary>
    public ScriptedRng(params int[] nextIntValues)
    {
        _nextIntQueue = new Queue<int>(nextIntValues);
        _nextChanceQueue = new Queue<bool>();
    }

    /// <summary>Creates a scripted generator with separate queues for <see cref="NextInt(int)"/> and <see cref="NextChance"/> calls, each consumed in order.</summary>
    public ScriptedRng(int[] nextIntValues, bool[] nextChanceValues)
    {
        _nextIntQueue = new Queue<int>(nextIntValues);
        _nextChanceQueue = new Queue<bool>(nextChanceValues);
    }

    /// <inheritdoc/>
    public ulong Seed => 0;

    /// <inheritdoc/>
    public ulong State => 0;

    /// <inheritdoc/>
    public ulong NextUInt64() => throw new NotSupportedException("Not scripted for this test.");

    /// <inheritdoc/>
    public int NextInt(int exclusiveUpperBound)
    {
        if (_nextIntQueue.Count == 0)
        {
            throw new InvalidOperationException("ScriptedRng ran out of queued NextInt values.");
        }

        var value = _nextIntQueue.Dequeue();
        if (value < 0 || value >= exclusiveUpperBound)
        {
            throw new InvalidOperationException(
                $"Scripted value {value} is outside [0, {exclusiveUpperBound}) -- check the test's script.");
        }

        return value;
    }

    /// <inheritdoc/>
    public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound) =>
        throw new NotSupportedException("Not scripted for this test.");

    /// <inheritdoc/>
    public bool NextChance(int numerator, int denominator)
    {
        if (_nextChanceQueue.Count == 0)
        {
            throw new InvalidOperationException("ScriptedRng ran out of queued NextChance values.");
        }

        return _nextChanceQueue.Dequeue();
    }

    /// <inheritdoc/>
    public IRng ForStream(string streamName) => this;
}
