using IC2.Engine.Core;

namespace IC2.Engine.MapGen;

/// <summary>
/// T294b: the generator's own deterministic randomness. A generation attempt derives every purpose
/// stream (landmass, rivers, settlements, forces) from the caller's seed, the attempt ordinal and a
/// purpose name through <see cref="RngStreams.DeriveSeed"/>, so the same seed and attempt always
/// compose the same map and different attempts differ — and none of it touches any campaign RNG.
/// </summary>
public sealed class MapSeedStream
{
    private MapSeedStream(ulong seed, int attempt)
    {
        Seed = seed;
        Attempt = attempt;
    }

    public ulong Seed { get; }

    /// <summary>The one-based retry attempt this stream serves. Attempts 1..20 per the T294 contract.</summary>
    public int Attempt { get; }

    public static MapSeedStream For(ulong seed, int attempt) => new(seed, attempt);

    /// <summary>The purpose-named sub-stream for this seed and attempt.</summary>
    public SplitMix64Rng Stream(string purpose) =>
        new(RngStreams.DeriveSeed(Seed, $"mapgen:{purpose}", Attempt));
}
