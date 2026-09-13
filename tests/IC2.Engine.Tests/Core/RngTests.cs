using IC2.Engine.Core;
using Xunit;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// The seeded RNG seam: reproducible, unbiased, and — the property the whole parallel build leans on —
/// isolated between named streams.
/// </summary>
public class RngTests
{
    private const int SampleCount = 4096;

    private static ulong SampleSeed => CoreTestbed.Toy.Scenario.RandomSeed;

    [Fact]
    public void The_same_seed_produces_the_same_sequence()
    {
        var first = new SplitMix64Rng(SampleSeed);
        var second = new SplitMix64Rng(SampleSeed);

        for (var i = 0; i < SampleCount; i++)
        {
            Assert.Equal(first.NextUInt64(), second.NextUInt64());
        }
    }

    [Fact]
    public void Different_seeds_produce_different_sequences()
    {
        var first = new SplitMix64Rng(SampleSeed);
        var second = new SplitMix64Rng(RngStreams.Advance(SampleSeed));

        var differences = 0;
        for (var i = 0; i < SampleCount; i++)
        {
            if (first.NextUInt64() != second.NextUInt64())
            {
                differences++;
            }
        }

        // Not "at least one": two 64-bit streams that agreed on even a handful of draws would mean the
        // seed barely reaches the output.
        Assert.Equal(SampleCount, differences);
    }

    [Fact]
    public void A_stream_is_a_pure_function_of_its_parents_seed_and_its_name()
    {
        var parent = new SplitMix64Rng(SampleSeed);

        var before = parent.ForStream("economy.upkeep");

        // Draw from the parent, a lot, then ask for the same stream again. This is the isolation
        // property: adding draws to one system must not move another system's sequence.
        for (var i = 0; i < SampleCount; i++)
        {
            parent.NextUInt64();
        }

        var after = parent.ForStream("economy.upkeep");

        Assert.Equal(before.Seed, after.Seed);
        for (var i = 0; i < SampleCount; i++)
        {
            Assert.Equal(before.NextUInt64(), after.NextUInt64());
        }
    }

    [Fact]
    public void Different_stream_names_give_independent_sequences()
    {
        var parent = new SplitMix64Rng(SampleSeed);

        var economy = parent.ForStream("economy.upkeep");
        var battle = parent.ForStream("battle.resolve");

        Assert.NotEqual(economy.Seed, battle.Seed);

        var collisions = 0;
        for (var i = 0; i < SampleCount; i++)
        {
            if (economy.NextUInt64() == battle.NextUInt64())
            {
                collisions++;
            }
        }

        Assert.Equal(0, collisions);
    }

    [Fact]
    public void Stream_names_that_differ_by_one_character_do_not_collide()
    {
        var parent = new SplitMix64Rng(SampleSeed);

        Assert.NotEqual(
            parent.ForStream("system:economy.upkeep@CityTick").Seed,
            parent.ForStream("system:economy.upkeep@ArmyTick").Seed);
    }

    [Fact]
    public void Bounded_draws_stay_in_range_and_cover_it()
    {
        // A bound that is not a power of two is the case naive modulo gets wrong, so it is the one worth
        // exercising. The value is the ruleset's own season count, not a number invented here.
        var bound = CoreTestbed.Toy.Ruleset.Calendar.SeasonsPerYear + 1;
        var rng = new SplitMix64Rng(SampleSeed);
        var seen = new bool[bound];

        for (var i = 0; i < SampleCount; i++)
        {
            var value = rng.NextInt(bound);
            Assert.InRange(value, 0, bound - 1);
            seen[value] = true;
        }

        Assert.DoesNotContain(false, seen);
    }

    [Fact]
    public void A_lower_bounded_draw_stays_inside_its_range()
    {
        var loyalty = CoreTestbed.Toy.Ruleset.Loyalty;
        var rng = new SplitMix64Rng(SampleSeed);

        for (var i = 0; i < SampleCount; i++)
        {
            var value = rng.NextInt(loyalty.ForcedCaptureFloor, loyalty.AllegiantRecaptureTarget);
            Assert.InRange(value, loyalty.ForcedCaptureFloor, loyalty.AllegiantRecaptureTarget - 1);
        }
    }

    [Fact]
    public void A_chance_consumes_exactly_one_draw_whatever_its_odds()
    {
        // Otherwise tuning a probability would shift every roll after it, and every golden fixture
        // downstream of it.
        var denominator = CoreTestbed.Toy.Ruleset.Calendar.SeasonsPerYear;

        for (var numerator = 0; numerator <= denominator; numerator++)
        {
            var chance = new SplitMix64Rng(SampleSeed);
            var reference = new SplitMix64Rng(SampleSeed);

            chance.NextChance(numerator, denominator);
            reference.NextInt(denominator);

            Assert.Equal(reference.State, chance.State);
        }
    }

    [Fact]
    public void A_certain_chance_always_holds_and_an_impossible_one_never_does()
    {
        var denominator = CoreTestbed.Toy.Ruleset.Calendar.SeasonsPerYear;
        var rng = new SplitMix64Rng(SampleSeed);

        for (var i = 0; i < SampleCount; i++)
        {
            Assert.True(rng.NextChance(denominator, denominator));
            Assert.False(rng.NextChance(0, denominator));
        }
    }

    [Fact]
    public void A_one_in_n_chance_both_holds_and_fails_over_a_long_run()
    {
        // Deliberately not a band assertion on the hit rate: the draw is deterministic under a fixed
        // seed, so a band would be a weaker statement than it looks. What is worth pinning is that the
        // bounded draw is not stuck at one end — which "both outcomes occur" states exactly.
        var denominator = CoreTestbed.Toy.Ruleset.Calendar.SeasonsPerYear;
        var rng = new SplitMix64Rng(SampleSeed);

        var hits = 0;
        var misses = 0;
        for (var i = 0; i < SampleCount; i++)
        {
            if (rng.NextChance(1, denominator))
            {
                hits++;
            }
            else
            {
                misses++;
            }
        }

        Assert.True(hits > 0 && misses > 0, $"{hits} hits and {misses} misses over {SampleCount} draws.");
    }

    [Fact]
    public void Invalid_bounds_are_rejected()
    {
        var rng = new SplitMix64Rng(SampleSeed);

        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextChance(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextChance(-1, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextChance(5, 4));
        Assert.Throws<ArgumentException>(() => rng.ForStream("  "));
    }

    [Fact]
    public void The_root_advance_never_repeats_a_value_over_a_long_walk()
    {
        // The counter step is what guarantees a full period; this is a cheap witness that it is a step
        // and not, say, a mix that could land back where it started.
        var seen = new HashSet<ulong>();
        var state = SampleSeed;

        for (var i = 0; i < SampleCount; i++)
        {
            state = RngStreams.Advance(state);
            Assert.True(seen.Add(state), $"The root advance repeated a value after {i} steps.");
        }
    }

    [Fact]
    public void Stream_derivation_is_pure()
    {
        Assert.Equal(
            RngStreams.DeriveSeed(SampleSeed, "economy.upkeep"),
            RngStreams.DeriveSeed(SampleSeed, "economy.upkeep"));

        Assert.NotEqual(
            RngStreams.DeriveSeed(SampleSeed, "economy.upkeep"),
            RngStreams.DeriveSeed(RngStreams.Advance(SampleSeed), "economy.upkeep"));

        Assert.NotEqual(
            RngStreams.DeriveSeed(SampleSeed, "economy.upkeep", 0),
            RngStreams.DeriveSeed(SampleSeed, "economy.upkeep", 1));
    }
}
