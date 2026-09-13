using IC2.Engine.Core;

namespace IC2.Engine.Tests.Strength;

/// <summary>
/// A minimal <see cref="IRng"/> test double that always draws a fixed value from
/// <see cref="IRng.NextInt(int)"/>, so a formula test can pin an exact arithmetic result without
/// needing to find a real seed that happens to draw a particular number. Used only to isolate
/// <see cref="FleetPower"/>'s truncation order (Done-when 5) and its base-formula reproduction
/// (Done-when 2); Done-when 3 (seed-reproducibility) uses the real
/// <see cref="IC2.Engine.Core.IRng"/> implementation instead, since that is the property under test
/// there.
/// </summary>
/// <remarks>
/// This is a test-only stand-in, not a second production RNG: <see cref="FleetPower"/> only ever
/// depends on the <see cref="IRng"/> interface, never on this type.
/// </remarks>
internal sealed class FixedDrawRng : IRng
{
    private readonly int _fixedDraw;

    public FixedDrawRng(int fixedDraw) => _fixedDraw = fixedDraw;

    public ulong Seed => 0;

    public ulong State => 0;

    public ulong NextUInt64() => throw new NotSupportedException("Not needed by FleetPower.");

    public int NextInt(int exclusiveUpperBound)
    {
        if (exclusiveUpperBound <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
        }

        return _fixedDraw;
    }

    public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound) =>
        throw new NotSupportedException("Not needed by FleetPower.");

    public bool NextChance(int numerator, int denominator) =>
        throw new NotSupportedException("Not needed by FleetPower.");

    public IRng ForStream(string streamName) => this;
}
