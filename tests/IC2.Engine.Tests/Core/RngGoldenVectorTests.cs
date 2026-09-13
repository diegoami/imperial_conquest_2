using IC2.Engine.Core;
using Xunit;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// Pins the generator's actual output values, not just its relative behaviour.
/// </summary>
/// <remarks>
/// <para>
/// Every other test in <see cref="RngTests"/> asserts a <em>relative</em> property — the same seed agrees
/// with itself, different seeds disagree, sub-streams are independent — and every one of those survives a
/// silently corrupted generator. Changing one shift width in
/// <see cref="RngStreams.Mix"/> (<c>z &gt;&gt; 30</c> to <c>z &gt;&gt; 29</c>, a plausible typo) left the
/// whole suite green when the reviewer tried it. That is a serious hole, because from wave 3 onward every
/// fixed-seed golden fixture in T07, T08, T16 and T19 bakes in this exact bit sequence: a one-character
/// regression would be invisible today and would invalidate all of them at once later.
/// </para>
/// <para>
/// So this class asserts known values. The first group is an <strong>external reference</strong>: Vigna's
/// published SplitMix64, whose canonical first output for seed 0 is <c>0xE220A8397B1DCDAF</c>. The rest
/// are <strong>regression fixtures</strong> — no published vector exists for them, so they pin
/// <em>this</em> implementation rather than an outside standard, and their only claim is "this is what the
/// engine produced when T03 merged". Both kinds fail on any change to a constant, a shift width, or the
/// order of operations.
/// </para>
/// </remarks>
public class RngGoldenVectorTests
{
    /// <summary>
    /// Vigna's SplitMix64 reference output for seed 0. External: this sequence is published, and
    /// <c>e220a8397b1dcdaf</c> is the canonical first value every conforming implementation produces.
    /// </summary>
    private static readonly ulong[] ReferenceSeedZero =
    {
        0xE220A8397B1DCDAF, 0x6E789E6AA1B965F4, 0x06C45D188009454F,
        0xF88BB8A8724C81EC, 0x1B39896A51A8749B,
    };

    /// <summary>Vigna's SplitMix64 reference output for seed 1. External, same source.</summary>
    private static readonly ulong[] ReferenceSeedOne =
    {
        0x910A2DEC89025CC1, 0xBEEB8DA1658EEC67, 0xF893A2EEFB32555E,
        0x71C18690EE42C90B, 0x71BB54D8D101B5B9,
    };

    /// <summary>Vigna's SplitMix64 reference output for a seed with bits set across the whole word.</summary>
    private static readonly ulong[] ReferenceSpreadSeed =
    {
        0x0D7D93560D1929D2, 0x491DFB740E50D43F, 0x42722BF4473E5E7D,
        0xD6CA8A0790FFFC45, 0xB2D3AB004CDB504B,
    };

    private const ulong SpreadSeed = 0xDEADBEEFCAFEBABE;

    [Fact]
    public void The_generator_reproduces_the_published_splitmix64_reference_vector()
    {
        Assert.Equal(ReferenceSeedZero, Draw(seed: 0, count: ReferenceSeedZero.Length));
        Assert.Equal(ReferenceSeedOne, Draw(seed: 1, count: ReferenceSeedOne.Length));
        Assert.Equal(ReferenceSpreadSeed, Draw(SpreadSeed, ReferenceSpreadSeed.Length));
    }

    [Fact]
    public void The_mix_function_alone_matches_the_reference()
    {
        // Mix is the reference's finalizer applied to the counter's first value, so asserting it separately
        // localises a failure: a wrong multiplier or shift shows up here, while a wrong Advance shows up
        // only in the sequence test above.
        Assert.Equal(ReferenceSeedZero[0], RngStreams.Mix(RngStreams.Advance(0)));
        Assert.Equal(ReferenceSeedOne[0], RngStreams.Mix(RngStreams.Advance(1)));
        Assert.Equal(ReferenceSpreadSeed[0], RngStreams.Mix(RngStreams.Advance(SpreadSeed)));
    }

    [Fact]
    public void The_counter_step_matches_the_reference()
    {
        // The reference's state update is `x += 0x9e3779b97f4a7c15`, wrapping. Asserted at 0 and at a value
        // chosen to wrap, so an accidental `checked` or a narrowed type would fail here.
        Assert.Equal(0x9E3779B97F4A7C15UL, RngStreams.Advance(0));
        Assert.Equal(0x9E3779B97F4A7C14UL, RngStreams.Advance(ulong.MaxValue));
    }

    [Fact]
    public void Bounded_draws_are_pinned_as_a_regression_fixture()
    {
        // REGRESSION FIXTURE, not an external reference: Lemire's method has no published vector, so these
        // values pin this implementation. They still fail on any change to Mix, to Advance, or to the
        // rejection threshold, which is what they are here for.
        var rng = new SplitMix64Rng(0);
        var drawn = new int[8];
        for (var i = 0; i < drawn.Length; i++)
        {
            drawn[i] = rng.NextInt(100);
        }

        Assert.Equal(new[] { 88, 43, 2, 97, 10, 32, 17, 77 }, drawn);
    }

    [Fact]
    public void Stream_derivation_is_pinned_as_a_regression_fixture()
    {
        // REGRESSION FIXTURE. FNV-1a over UTF-16 code units, mixed into the seed — see RngStreams. This is
        // the value a later task's golden fixtures ultimately hang off, so it is pinned by name.
        Assert.Equal(0x4D899BF10AABCC97UL, RngStreams.DeriveSeed(0, "economy.upkeep"));
        Assert.Equal(0xCBF29CE484222325UL ^ 0, RngStreams.HashStreamName(string.Empty));
    }

    private static ulong[] Draw(ulong seed, int count)
    {
        var rng = new SplitMix64Rng(seed);
        var values = new ulong[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = rng.NextUInt64();
        }

        return values;
    }
}
