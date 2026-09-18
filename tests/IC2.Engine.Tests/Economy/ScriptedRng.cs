using IC2.Engine.Core;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// A minimal <see cref="IRng"/> test double that returns pre-scripted answers, in order, from
/// <see cref="NextInt(int)"/> and <see cref="NextChance"/> — so <see cref="Economy.CityLoyaltyDraws"/>'s
/// exact arithmetic (docs/task-catalogue.md T35 DoD 11) can be pinned without hunting for a real seed
/// that happens to draw a particular sequence.
/// </summary>
/// <remarks>
/// This is a test-only stand-in, not a second production RNG: every production type here depends only on
/// <see cref="IRng"/>. <see cref="ForStream"/> returns <c>this</c>, since a script is written against
/// the exact call sequence a test drives, not against a named sub-stream.
/// </remarks>
internal sealed class ScriptedRng : IRng
{
    private readonly Queue<int> _nextIntDraws;
    private readonly Queue<bool> _nextChanceDraws;

    public ScriptedRng(IEnumerable<int>? nextIntDraws = null, IEnumerable<bool>? nextChanceDraws = null)
    {
        _nextIntDraws = new Queue<int>(nextIntDraws ?? Array.Empty<int>());
        _nextChanceDraws = new Queue<bool>(nextChanceDraws ?? Array.Empty<bool>());
    }

    public ulong Seed => 0;

    public ulong State => 0;

    public ulong NextUInt64() => throw new NotSupportedException("Not scripted by this test double.");

    public int NextInt(int exclusiveUpperBound)
    {
        if (exclusiveUpperBound <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
        }

        if (_nextIntDraws.Count == 0)
        {
            throw new InvalidOperationException("ScriptedRng has no more scripted NextInt draws.");
        }

        var value = _nextIntDraws.Dequeue();

        // A real IRng can never draw outside [0, exclusiveUpperBound) -- fail loudly rather than quietly
        // asserting a value the real contract forbids.
        if ((uint)value >= (uint)exclusiveUpperBound)
        {
            throw new InvalidOperationException(
                $"ScriptedRng's next draw {value} does not fit [0, {exclusiveUpperBound}).");
        }

        return value;
    }

    public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound) =>
        throw new NotSupportedException("Not scripted by this test double.");

    public bool NextChance(int numerator, int denominator)
    {
        if (_nextChanceDraws.Count == 0)
        {
            throw new InvalidOperationException("ScriptedRng has no more scripted NextChance draws.");
        }

        return _nextChanceDraws.Dequeue();
    }

    public IRng ForStream(string streamName) => this;
}
