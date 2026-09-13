namespace IC2.Engine.Core;

/// <summary>
/// Seed arithmetic: how a named sub-stream's seed is derived, and how the engine's one persisted
/// random value moves forward.
/// </summary>
/// <remarks>
/// <para>
/// Split out of <see cref="SplitMix64Rng"/> so the derivation is a documented, testable contract rather
/// than a private detail of one generator. Every value here is a fixed constant of the SplitMix64 /
/// FNV-1a algorithms — an algorithm identifier, not a gameplay number, so it is deliberately not
/// ruleset data.
/// </para>
/// <para>
/// <strong>Where the engine's randomness is persisted.</strong> There is exactly one persisted random
/// value: <see cref="Model.GameState.RandomSeed"/>. It is advanced by <see cref="Advance"/> at exactly
/// two points, and nowhere else:
/// </para>
/// <list type="number">
/// <item><description>once at the start of every turn-pipeline run (<see cref="TurnCoordinator"/>);</description></item>
/// <item><description>once per <em>accepted</em> command (<see cref="CommandDispatcher"/>).</description></item>
/// </list>
/// <para>
/// Every actual draw then happens in a named sub-stream derived from that value by
/// <see cref="DeriveSeed(ulong, string)"/>. The consequence worth stating plainly, because the whole
/// parallel build depends on it: adding a system, adding a draw inside a system, or reordering systems
/// within a phase changes <em>nothing</em> about any other system's sequence. Only the command sequence
/// and the number of turns move the root value, and both of those are the game's actual input.
/// </para>
/// </remarks>
public static class RngStreams
{
    /// <summary>
    /// SplitMix64's additive constant, the 64-bit odd approximation of the golden ratio
    /// (2^64 / φ). Part of the algorithm, not a tunable.
    /// </summary>
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

    private const ulong MixMultiplierA = 0xBF58476D1CE4E5B9UL;
    private const ulong MixMultiplierB = 0x94D049BB133111EBUL;

    private const ulong Fnv1aOffsetBasis = 0xCBF29CE484222325UL;
    private const ulong Fnv1aPrime = 0x00000100000001B3UL;

    /// <summary>
    /// SplitMix64's finalizing mix: a bijection on 64 bits with good avalanche, used both as the
    /// generator's output function and as the mixer for seed derivation.
    /// </summary>
    public static ulong Mix(ulong value)
    {
        var z = value;
        z = (z ^ (z >> 30)) * MixMultiplierA;
        z = (z ^ (z >> 27)) * MixMultiplierB;
        return z ^ (z >> 31);
    }

    /// <summary>
    /// One step of a SplitMix64 counter: add the golden gamma. Because the gamma is odd, the counter
    /// visits all 2^64 values before repeating — a guaranteed full period, which a "mix, then step"
    /// variant would not have (an arbitrary bijection decomposes into cycles of unknown length).
    /// </summary>
    /// <remarks>
    /// Stepping the counter is deliberately <em>not</em> where the scrambling happens. A raw counter
    /// step looks unimpressive as a persisted <see cref="Model.GameState.RandomSeed"/>, but every value
    /// actually drawn from it passes through <see cref="Mix"/> — in the generator's output function, and
    /// again in <see cref="DeriveSeed(ulong, string)"/> — so two consecutive root values produce
    /// completely unrelated sub-streams.
    /// </remarks>
    public static ulong Advance(ulong state) => unchecked(state + GoldenGamma);

    /// <summary>
    /// The seed of the sub-stream called <paramref name="streamName"/> under <paramref name="seed"/>.
    /// A pure function of its two arguments: same inputs, same stream, forever.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="streamName"/> is null or whitespace.</exception>
    public static ulong DeriveSeed(ulong seed, string streamName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamName);
        return unchecked(Mix(seed + Mix(HashStreamName(streamName))));
    }

    /// <summary>
    /// The seed of an indexed sub-stream — <c>DeriveSeed(seed, name)</c> further split by
    /// <paramref name="ordinal"/>, for the case where the same name legitimately recurs (a per-city or
    /// per-army loop, say) and each iteration needs its own independent sequence.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="streamName"/> is null or whitespace.</exception>
    public static ulong DeriveSeed(ulong seed, string streamName, long ordinal) =>
        unchecked(Mix(DeriveSeed(seed, streamName) + Mix((ulong)ordinal)));

    /// <summary>
    /// FNV-1a over the stream name's UTF-16 code units.
    /// </summary>
    /// <remarks>
    /// Written out rather than using <see cref="string.GetHashCode()"/> for a reason that is easy to
    /// miss and fatal here: .NET randomizes string hashing per process, so a seed derived from
    /// <c>GetHashCode</c> would differ between two runs of the same test on the same machine. FNV-1a is
    /// fixed by its two constants and is identical on every process, machine and platform.
    /// </remarks>
    public static ulong HashStreamName(string streamName)
    {
        ArgumentNullException.ThrowIfNull(streamName);

        var hash = Fnv1aOffsetBasis;
        foreach (var character in streamName)
        {
            unchecked
            {
                hash ^= character;
                hash *= Fnv1aPrime;
            }
        }

        return hash;
    }
}
