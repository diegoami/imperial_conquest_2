using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// <c>docs/task-catalogue.md</c> T19 DoD 6 (the quarterly thaw converges to 0 under a fixed seed) and
/// DoD 9 (the Q8 first-8-columns bug behind <see cref="RulesetFlags.FaithfulThawColumnBug"/>, a test for
/// each setting).
/// </summary>
public sealed class QuarterlyThawSystemTests
{
    private const string A = "a";
    private const string B = "b";

    private static GameState TwoNationStateWithCooldown(int cooldown)
    {
        var state = DiplomacyTestbed.StateOf(DiplomacyTestbed.Nation(A, "A"), DiplomacyTestbed.Nation(B, "B"));
        return state with { Relations = state.Relations.WithRelation(A, B, cooldown) };
    }

    // ---- DoD 6 ----

    /// <summary>Exact under a fixed seed: pins the +1 and the 1/3-chance +3 bonus together, not just sign.</summary>
    [Fact]
    public void DoD06_OneQuarter_ExactUnderAFixedSeed()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = TwoNationStateWithCooldown(-18);
        var rng = new SplitMix64Rng(0x600DUL);

        var after = QuarterlyThawSystem.Apply(state, ruleset, rng);

        var expectedRng = new SplitMix64Rng(0x600DUL);
        var expected = -18 + ruleset.Diplomacy.ThawPerQuarter;
        if (expectedRng.NextChance(1, ruleset.Diplomacy.ThawBonusChanceDenominator))
        {
            expected = Math.Min(0, expected + ruleset.Diplomacy.ThawBonus);
        }

        Assert.Equal(expected, after.Relations.Get(A, B));
    }

    /// <summary>Repeated quarters converge to exactly 0 and stop moving once there.</summary>
    [Fact]
    public void DoD06_RepeatedQuarters_ConvergeToExactlyZero_AndStayThere()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = TwoNationStateWithCooldown(-24);
        var rng = new SplitMix64Rng(0xF00DUL);

        for (var i = 0; i < 100; i++)
        {
            state = QuarterlyThawSystem.Apply(state, ruleset, rng);
        }

        Assert.Equal(0, state.Relations.Get(A, B));

        // Once at zero, further quarters draw nothing and leave it exactly at zero.
        var beforeExtra = state;
        state = QuarterlyThawSystem.Apply(state, ruleset, rng);
        Assert.Equal(0, state.Relations.Get(A, B));
        Assert.Equal(beforeExtra.Relations.Get(A, B), state.Relations.Get(A, B));
    }

    /// <summary>A non-negative relation (peace, trade, alliance, war) is never touched by the thaw.</summary>
    [Fact]
    public void DoD06_ANonNegativeRelation_IsNeverTouched()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = TwoNationStateWithCooldown(ruleset.Diplomacy.StateCodes.Trade);
        var rng = DiplomacyTestbed.Rng();

        var after = QuarterlyThawSystem.Apply(state, ruleset, rng);

        Assert.Equal(ruleset.Diplomacy.StateCodes.Trade, after.Relations.Get(A, B));
    }

    /// <summary>The matrix stays symmetric after a thaw pass -- one draw per pair, not one per row.</summary>
    [Fact]
    public void DoD06_TheMatrixStaysSymmetric_AfterAThawPass()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = TwoNationStateWithCooldown(-8);

        var after = QuarterlyThawSystem.Apply(state, ruleset, DiplomacyTestbed.Rng());

        Assert.True(after.Relations.IsWellFormed());
        Assert.Equal(after.Relations.Get(A, B), after.Relations.Get(B, A));
    }

    // ---- DoD 9: a test for EACH setting ----

    private static readonly string[] TenNationIds =
        { "n0", "n1", "n2", "n3", "n4", "n5", "n6", "n7", "n8", "n9" };

    private static GameState TenNationStateWithAllPairsCooling()
    {
        var nations = TenNationIds.Select(id => DiplomacyTestbed.Nation(id, id)).ToArray();
        var state = DiplomacyTestbed.StateOf(nations);

        var relations = state.Relations;
        for (var i = 0; i < TenNationIds.Length; i++)
        {
            for (var j = i + 1; j < TenNationIds.Length; j++)
            {
                relations = relations.WithRelation(TenNationIds[i], TenNationIds[j], -18);
            }
        }

        return state with { Relations = relations };
    }

    /// <summary>
    /// Setting 1 of 2: <c>faithfulThawColumnBug = true</c> reproduces the bug -- a pair both indexed at or
    /// past <see cref="DiplomacyRules.FaithfulThawColumnLimit"/> (8) never decays, while a pair with at
    /// least one index under it does.
    /// </summary>
    [Fact]
    public void DoD09_FaithfulSetting_APairBothPastTheLimit_NeverDecays()
    {
        var ruleset = DiplomacyTestbed.Ruleset with
        {
            Flags = DiplomacyTestbed.Ruleset.Flags with { FaithfulThawColumnBug = true },
        };
        var state = TenNationStateWithAllPairsCooling();
        var rng = DiplomacyTestbed.Rng();

        // n8 and n9 are both indexed >= 8 (the shipped limit): never decays.
        for (var i = 0; i < 50; i++)
        {
            state = QuarterlyThawSystem.Apply(state, ruleset, rng);
        }

        Assert.Equal(-18, state.Relations.Get("n8", "n9"));

        // n7 (index 7, under the limit) and n8 (index 8) DOES decay: at least one side is under 8.
        Assert.True(state.Relations.Get("n7", "n8") > -18);
    }

    /// <summary>Setting 2 of 2: <c>faithfulThawColumnBug = false</c> thaws every pair, index or no index.</summary>
    [Fact]
    public void DoD09_CorrectedSetting_EveryPairThaws_RegardlessOfIndex()
    {
        var ruleset = DiplomacyTestbed.Ruleset with
        {
            Flags = DiplomacyTestbed.Ruleset.Flags with { FaithfulThawColumnBug = false },
        };
        var state = TenNationStateWithAllPairsCooling();
        var rng = DiplomacyTestbed.Rng();

        for (var i = 0; i < 200; i++)
        {
            state = QuarterlyThawSystem.Apply(state, ruleset, rng);
        }

        // Even the pair that never moves under the faithful bug now converges to zero.
        Assert.Equal(0, state.Relations.Get("n8", "n9"));
    }

    /// <summary>
    /// Mutation proof: the two settings above produce genuinely different results for the same pair and
    /// the same number of quarters -- proving the flag is actually read, not a dead parameter.
    /// </summary>
    [Fact]
    public void DoD09_MutationProof_TheTwoSettings_DisagreeOnTheSamePair()
    {
        var faithful = DiplomacyTestbed.Ruleset with
        {
            Flags = DiplomacyTestbed.Ruleset.Flags with { FaithfulThawColumnBug = true },
        };
        var corrected = DiplomacyTestbed.Ruleset with
        {
            Flags = DiplomacyTestbed.Ruleset.Flags with { FaithfulThawColumnBug = false },
        };

        var faithfulState = TenNationStateWithAllPairsCooling();
        var correctedState = TenNationStateWithAllPairsCooling();
        var faithfulRng = new SplitMix64Rng(0xABCDUL);
        var correctedRng = new SplitMix64Rng(0xABCDUL);

        for (var i = 0; i < 30; i++)
        {
            faithfulState = QuarterlyThawSystem.Apply(faithfulState, faithful, faithfulRng);
            correctedState = QuarterlyThawSystem.Apply(correctedState, corrected, correctedRng);
        }

        Assert.NotEqual(faithfulState.Relations.Get("n8", "n9"), correctedState.Relations.Get("n8", "n9"));
    }
}
