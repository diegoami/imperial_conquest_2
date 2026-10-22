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

    private static Ruleset WithColumnBug(bool faithful) => DiplomacyTestbed.Ruleset with
    {
        Flags = DiplomacyTestbed.Ruleset.Flags with { FaithfulThawColumnBug = faithful },
    };

    /// <summary>
    /// #1339: under the original's loop a pair of two nations both indexed below 8 is visited twice, from
    /// each nation's row, the second visit reading the first's result; a plain visit adds 1.
    /// </summary>
    [Fact]
    public void A_low_low_pair_is_visited_twice_a_quarter_under_the_original_loop()
    {
        var after = QuarterlyThawSystem.Apply(
            TwoNationStateWithCooldown(-18), WithColumnBug(true), new Economy.ScriptedRng(nextChanceDraws: new[] { false, false }));

        Assert.Equal(-16, after.Relations.Get(A, B));
        Assert.Equal(-16, after.Relations.Get(B, A));
    }

    /// <summary>
    /// #1339: the bonus draw replaces the +1 with <c>min(0, v + 3)</c>, so one bonus and one plain visit take
    /// -18 to -14 (the Rome-Gaul peace offer of <c>2026-10-05-battle-peace-offer.md</c>).
    /// </summary>
    [Fact]
    public void The_bonus_replaces_the_plus_one_instead_of_adding_to_it()
    {
        var ruleset = WithColumnBug(true);
        var odds = (1, ruleset.Diplomacy.ThawBonusChanceDenominator);
        var after = QuarterlyThawSystem.Apply(
            TwoNationStateWithCooldown(-18),
            ruleset,
            new Economy.ScriptedRng(nextChanceDraws: new[] { true, false }, expectedNextChanceOdds: new[] { odds, odds }));

        Assert.Equal(-14, after.Relations.Get(A, B));

        var fixedPolicy = QuarterlyThawSystem.Apply(
            TwoNationStateWithCooldown(-18), WithColumnBug(false), new Economy.ScriptedRng(nextChanceDraws: new[] { true }));
        Assert.Equal(-15, fixedPolicy.Relations.Get(A, B));
    }

    /// <summary>
    /// #1339: a pair with one index below 8 is visited once, reading the directed cell <c>[high][low]</c> and
    /// writing both sides. A one-sided cooldown in <c>[low][high]</c> is never read (atlas increment 11:
    /// Seleucid (2) and Galatia (12) went from -8 to -7 on both sides).
    /// </summary>
    [Fact]
    public void A_mixed_pair_thaws_from_the_high_rows_cell_only()
    {
        var state = TenNationStateWithAllPairsCooling();
        var zeroed = state.Relations;
        foreach (var x in TenNationIds)
        {
            foreach (var y in TenNationIds.Where(y => y != x))
            {
                zeroed = zeroed.WithRelation(x, y, 0);
            }
        }

        var highRow = state with { Relations = zeroed.WithDirectedRelation("n8", "n2", -8) };
        var after = QuarterlyThawSystem.Apply(highRow, WithColumnBug(true), new Economy.ScriptedRng(nextChanceDraws: new[] { false }));
        Assert.Equal(-7, after.Relations.Get("n8", "n2"));
        Assert.Equal(-7, after.Relations.Get("n2", "n8"));

        var lowRow = state with { Relations = zeroed.WithDirectedRelation("n2", "n8", -8) };
        var untouched = QuarterlyThawSystem.Apply(lowRow, WithColumnBug(true), new Economy.ScriptedRng());
        Assert.Equal(-8, untouched.Relations.Get("n2", "n8"));
        Assert.Equal(0, untouched.Relations.Get("n8", "n2"));
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
