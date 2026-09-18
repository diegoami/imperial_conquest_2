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
/// <para>
/// <strong>Bug <c>#132</c> (docs/task-catalogue.md T37 DoD 11):</strong> a caller's <em>own</em> bound or
/// odds were previously never checked against anything a test declared expecting — only against the
/// scripted return value happening to fit whatever bound the caller passed. Substituting a wider bound
/// (or a different denominator) at the call site therefore left every existing test green: the scripted
/// value still fit, and still came back unchanged, so nothing distinguished the ruleset's real constant
/// from a wrong one that also happened to admit the same scripted draw. <see cref="_expectedNextIntBounds"/>
/// and <see cref="_expectedNextChanceOdds"/> are opt-in (<see langword="null"/> by default, so every
/// existing script that does not care about the exact bound or odds keeps working unchanged): when a
/// test supplies them, each call's actual argument is checked against the next expected one and a
/// mismatch throws, so a test that supplies them pins the constant, not just the call shape.
/// </para>
/// </remarks>
internal sealed class ScriptedRng : IRng
{
    private readonly Queue<int> _nextIntDraws;
    private readonly Queue<bool> _nextChanceDraws;
    private readonly Queue<int>? _expectedNextIntBounds;
    private readonly Queue<(int Numerator, int Denominator)>? _expectedNextChanceOdds;

    public ScriptedRng(
        IEnumerable<int>? nextIntDraws = null,
        IEnumerable<bool>? nextChanceDraws = null,
        IEnumerable<int>? expectedNextIntBounds = null,
        IEnumerable<(int Numerator, int Denominator)>? expectedNextChanceOdds = null)
    {
        _nextIntDraws = new Queue<int>(nextIntDraws ?? Array.Empty<int>());
        _nextChanceDraws = new Queue<bool>(nextChanceDraws ?? Array.Empty<bool>());
        _expectedNextIntBounds = expectedNextIntBounds is null ? null : new Queue<int>(expectedNextIntBounds);
        _expectedNextChanceOdds = expectedNextChanceOdds is null
            ? null
            : new Queue<(int Numerator, int Denominator)>(expectedNextChanceOdds);
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

        if (_expectedNextIntBounds is not null)
        {
            if (_expectedNextIntBounds.Count == 0)
            {
                throw new InvalidOperationException("ScriptedRng has no more expected NextInt bounds.");
            }

            var expectedBound = _expectedNextIntBounds.Dequeue();
            if (expectedBound != exclusiveUpperBound)
            {
                throw new InvalidOperationException(
                    $"ScriptedRng expected NextInt({expectedBound}) but the caller drew NextInt({exclusiveUpperBound}).");
            }
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

        if (_expectedNextChanceOdds is not null)
        {
            if (_expectedNextChanceOdds.Count == 0)
            {
                throw new InvalidOperationException("ScriptedRng has no more expected NextChance odds.");
            }

            var (expectedNumerator, expectedDenominator) = _expectedNextChanceOdds.Dequeue();
            if (expectedNumerator != numerator || expectedDenominator != denominator)
            {
                throw new InvalidOperationException(
                    $"ScriptedRng expected NextChance({expectedNumerator}, {expectedDenominator}) but the "
                    + $"caller drew NextChance({numerator}, {denominator}).");
            }
        }

        return _nextChanceDraws.Dequeue();
    }

    public IRng ForStream(string streamName) => this;
}
