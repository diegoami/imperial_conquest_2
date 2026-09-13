namespace IC2.Engine.Core;

/// <summary>
/// The engine's <see cref="IRng"/>: SplitMix64, written out in full.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why an algorithm is spelled out here rather than wrapped around a framework type.</strong>
/// <c>System.Random</c> is banned in engine code (and the determinism guard fails the build on it), but
/// the deeper reason is reproducibility across versions: <c>Random</c>'s algorithm is an implementation
/// detail Microsoft has already changed once, so a save or a golden fixture produced under one runtime
/// would not replay under another. SplitMix64 is twelve lines of fixed arithmetic; a state and a seed
/// pin the sequence exactly, on any runtime, forever. That is the property
/// <c>docs/game-design.md</c> principle 4 is actually asking for — including its explicit
/// "ship commands + seed, not state" lockstep-multiplayer clause.
/// </para>
/// <para>
/// SplitMix64 was chosen over the stronger xoshiro/PCG families deliberately: its state is a single
/// 64-bit word, which is exactly what <see cref="Model.GameState.RandomSeed"/> already is, so the
/// engine's whole random position round-trips through T02's existing save model with no new field. Its
/// statistical quality (it passes BigCrush) is far beyond what turn-based strategy rolls need. The
/// trade-off, stated honestly: its period is 2^64 with no jump-ahead and no guaranteed
/// stream-independence — <see cref="ForStream"/> derives sub-streams by mixing, which makes collisions
/// astronomically unlikely rather than impossible by construction.
/// </para>
/// </remarks>
public sealed class SplitMix64Rng : IRng
{
    private ulong _state;

    /// <summary>Creates a generator positioned at <paramref name="seed"/>.</summary>
    public SplitMix64Rng(ulong seed)
    {
        Seed = seed;
        _state = seed;
    }

    /// <inheritdoc/>
    public ulong Seed { get; }

    /// <inheritdoc/>
    public ulong State => _state;

    /// <summary>Creates the sub-stream <paramref name="streamName"/> of <paramref name="seed"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="streamName"/> is null or whitespace.</exception>
    public static SplitMix64Rng ForStream(ulong seed, string streamName) =>
        new(RngStreams.DeriveSeed(seed, streamName));

    /// <inheritdoc/>
    public ulong NextUInt64()
    {
        _state = RngStreams.Advance(_state);
        return RngStreams.Mix(_state);
    }

    /// <inheritdoc/>
    public int NextInt(int exclusiveUpperBound)
    {
        if (exclusiveUpperBound <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exclusiveUpperBound), exclusiveUpperBound, "The upper bound must be positive.");
        }

        return (int)NextBounded((ulong)exclusiveUpperBound);
    }

    /// <inheritdoc/>
    public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound)
    {
        if (exclusiveUpperBound <= inclusiveLowerBound)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exclusiveUpperBound),
                exclusiveUpperBound,
                $"The range [{inclusiveLowerBound}, {exclusiveUpperBound}) is empty.");
        }

        // Widened to long before subtracting so that a range spanning the whole int domain does not
        // overflow, then narrowed back: the drawn offset is always smaller than the range.
        var range = (ulong)((long)exclusiveUpperBound - inclusiveLowerBound);
        return (int)(inclusiveLowerBound + (long)NextBounded(range));
    }

    /// <inheritdoc/>
    public bool NextChance(int numerator, int denominator)
    {
        if (denominator <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(denominator), denominator, "A chance denominator must be positive.");
        }

        if (numerator < 0 || numerator > denominator)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numerator), numerator, $"A chance numerator must lie in [0, {denominator}].");
        }

        // One draw is consumed whatever the odds are, including the degenerate 0-in-n and n-in-n cases,
        // so that a later change to a probability never shifts the stream's position for the draws that
        // follow it.
        return NextBounded((ulong)denominator) < (ulong)numerator;
    }

    /// <inheritdoc/>
    public IRng ForStream(string streamName) => new SplitMix64Rng(RngStreams.DeriveSeed(Seed, streamName));

    /// <summary>
    /// Lemire's multiply-and-reject: draws uniformly from <c>[0, range)</c> with no modulo bias.
    /// </summary>
    /// <remarks>
    /// Rejection is what makes it unbiased, and it is deterministic: the rejected draws are part of the
    /// stream, so the same seed rejects the same values and lands on the same result every time.
    /// </remarks>
    private ulong NextBounded(ulong range)
    {
        var high = Math.BigMul(NextUInt64(), range, out var low);
        if (low < range)
        {
            var threshold = (0UL - range) % range;
            while (low < threshold)
            {
                high = Math.BigMul(NextUInt64(), range, out low);
            }
        }

        return high;
    }
}
