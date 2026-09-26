using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// <c>decompiled-war-cascade-and-peace-paths.md</c> §1 (bug #383, T88 DoD 1): "Declaring war on b puts
/// each ally of b at war with you. Allying with b puts you at war with each enemy of b. The cascade goes
/// no further." A whole-program call-graph scan of the original's own setter, <c>FUN_00449B40</c>, finds
/// 22 callers and no call to itself — the engine's own cascade is now one step, not recursive. Both
/// directions, and — per <c>docs/build-process.md</c>'s two-entity probe — a nation nowhere near the
/// transition survives completely untouched.
/// </summary>
public sealed class ContagionTests
{
    private const string X = "x"; // the nation declaring war / forming the alliance
    private const string A = "a";
    private const string B = "b";
    private const string C = "c";
    private const string D = "d";
    private const string Untouched = "untouched"; // the two-entity probe

    /// <summary>
    /// The report's own worked example (§1.4): A–B, B–C and C–D are allied. X, at peace with all four,
    /// declares war on A. The original leaves X at war with A and B only — C and D, allied to B and C
    /// respectively rather than directly to X's own target, stay at peace: "Nothing later corrects that."
    /// </summary>
    private static GameState ChainOfFourAllies()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(X, "X"),
            DiplomacyTestbed.Nation(A, "A"),
            DiplomacyTestbed.Nation(B, "B"),
            DiplomacyTestbed.Nation(C, "C"),
            DiplomacyTestbed.Nation(D, "D"),
            DiplomacyTestbed.Nation(Untouched, "Untouched"));

        state = RelationTransitions.FormAlliance(state, ruleset, A, B);
        state = RelationTransitions.FormAlliance(state, ruleset, B, C);
        state = RelationTransitions.FormAlliance(state, ruleset, C, D);
        return state;
    }

    /// <summary>
    /// DoD 1: "one test shows X ends at war with A and B only" — the setter's own war-declaration loop
    /// (report §1.1, loop 2) drags in every ally of the direct target, and no further.
    /// </summary>
    [Fact]
    public void DoD01_OneStepWarCascade_XEndsAtWarWithAAndBOnly()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = ChainOfFourAllies();

        state = RelationTransitions.DeclareWar(state, ruleset, X, A);

        Assert.Equal(codes.War, state.Relations.Get(X, A));
        Assert.Equal(codes.War, state.Relations.Get(X, B));

        // The one-step cut: C is B's ally, not A's, and D is reachable only through C.
        Assert.Equal(codes.Peace, state.Relations.Get(X, C));
        Assert.Equal(codes.Peace, state.Relations.Get(X, D));

        // Two-entity probe.
        Assert.Equal(codes.Peace, state.Relations.Get(X, Untouched));
        Assert.True(state.Relations.IsWellFormed());

        // Each news line matches the setter's own line exactly (report §1.1: "X declares war on A." /
        // "X declares war on B.", the target first, then each dragged-in ally).
        var lines = state.NewsLog.Slots.Select(e => e.Text).ToList();
        Assert.Contains("X declares war on A.", lines);
        Assert.Contains("X declares war on B.", lines);
        Assert.DoesNotContain("X declares war on C.", lines);
        Assert.DoesNotContain("X declares war on D.", lines);
    }

    /// <summary>
    /// Mutation proof: restoring the old recursive cascade (a nation dragged in itself drags in its own
    /// allies) would put X at war with C too — this assertion is exactly what a flattened-but-still-wrong
    /// "drag in everyone reachable" mutation would still pass, and a deeper one would fail on D. Pinning
    /// C at peace is the assertion that actually catches recursion creeping back in.
    /// </summary>
    [Fact]
    public void DoD01_MutationProof_RecursionWouldPutXAtWarWithCToo()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = RelationTransitions.DeclareWar(ChainOfFourAllies(), ruleset, X, A);

        Assert.Equal(ruleset.Diplomacy.StateCodes.Peace, state.Relations.Get(X, C));
    }

    /// <summary>
    /// DoD 1: "one test shows the alliance cascade is one step as well." X is at war with A; B forms an
    /// alliance with A's own further ally chain (B allies with X's target's chain by allying with A). Per
    /// the setter's own loop 1 (report §1.1): allying with a partner drags you into war with every enemy
    /// the partner is already at war with, one step, no further. Here: X is at war with A only; B allies
    /// with A, so B is dragged into war with X. B's own further allies (none at war with X yet) are
    /// untouched.
    /// </summary>
    [Fact]
    public void DoD01_OneStepAllianceCascade_BEndsAtWarWithXOnly()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(X, "X"),
            DiplomacyTestbed.Nation(A, "A"),
            DiplomacyTestbed.Nation(B, "B"),
            DiplomacyTestbed.Nation(C, "C"),
            DiplomacyTestbed.Nation(Untouched, "Untouched"));

        // X is at war with A only. B is already allied with C (a bystander, not at war with X).
        state = RelationTransitions.DeclareWar(state, ruleset, X, A);
        state = RelationTransitions.FormAlliance(state, ruleset, B, C);

        // B now allies with A -- dragged into war with X, A's only enemy.
        state = RelationTransitions.FormAlliance(state, ruleset, B, A);

        Assert.Equal(codes.Alliance, state.Relations.Get(B, A));
        Assert.Equal(codes.War, state.Relations.Get(B, X));

        // C, B's own existing ally, is not itself dragged into X's war -- the cascade reads the NEW
        // partner's (A's) enemies, never the proposer's (B's) own other allies.
        Assert.Equal(codes.Peace, state.Relations.Get(C, X));

        Assert.Equal(codes.Peace, state.Relations.Get(Untouched, X));
        Assert.Equal(codes.Peace, state.Relations.Get(Untouched, B));
        Assert.True(state.Relations.IsWellFormed());

        var lines = state.NewsLog.Slots.Select(e => e.Text).ToList();
        Assert.Contains("B forms an alliance with A.", lines);
        Assert.Contains("B declares war on X.", lines);
    }

    /// <summary>
    /// DoD 1, rework round 1 (B1): <see cref="DoD01_OneStepAllianceCascade_BEndsAtWarWithXOnly"/> gives
    /// its "X" no ally, so there is no second hop for a recursion regression to reach -- a mutation that
    /// restores <see cref="RelationTransitions.FormAlliance"/>'s old recursive call (using
    /// <c>DeclareWar</c> instead of <c>DeclareWarWithoutFurtherCascade</c> for the dragged-in hop) leaves
    /// that test green. This is the report's own second worked example instead (§1.4): A is at war with
    /// X, and X is allied to Z. Y forms an alliance with A. Y is dragged into war with X (A's enemy, one
    /// hop), but the cascade must not reach past X to Z, X's own ally.
    /// </summary>
    [Fact]
    public void DoD01_OneStepAllianceCascade_DoesNotReachTargetsOwnAlly()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        const string ReportA = "reportA";
        const string ReportX = "reportX";
        const string ReportZ = "reportZ";
        const string ReportY = "reportY";
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(ReportA, "A"),
            DiplomacyTestbed.Nation(ReportX, "X"),
            DiplomacyTestbed.Nation(ReportZ, "Z"),
            DiplomacyTestbed.Nation(ReportY, "Y"));

        // A is at war with X, and X is allied to Z.
        state = RelationTransitions.DeclareWar(state, ruleset, ReportA, ReportX);
        state = RelationTransitions.FormAlliance(state, ruleset, ReportX, ReportZ);

        // Y forms an alliance with A.
        state = RelationTransitions.FormAlliance(state, ruleset, ReportY, ReportA);

        Assert.Equal(codes.Alliance, state.Relations.Get(ReportY, ReportA));
        Assert.Equal(codes.War, state.Relations.Get(ReportY, ReportX));

        // The one-step cut: Z is X's ally, not A's -- the cascade does not reach past X to Z.
        Assert.Equal(codes.Peace, state.Relations.Get(ReportY, ReportZ));
        Assert.True(state.Relations.IsWellFormed());

        var lines = state.NewsLog.Slots.Select(e => e.Text).ToList();
        Assert.Contains("Y forms an alliance with A.", lines);
        Assert.Contains("Y declares war on X.", lines);
        Assert.DoesNotContain("Y declares war on Z.", lines);
    }

    /// <summary>Already being at war with the cascade target is a no-op: no double-processing, no throw.</summary>
    [Fact]
    public void DoD04_CascadeTarget_AlreadyAtWar_IsANoOp()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = ChainOfFourAllies();

        state = RelationTransitions.DeclareWar(state, ruleset, X, A);
        state = RelationTransitions.DeclareWar(state, ruleset, Untouched, B);

        Assert.Equal(codes.War, state.Relations.Get(X, B));
        Assert.Equal(codes.War, state.Relations.Get(Untouched, B));
        Assert.True(state.Relations.IsWellFormed());
    }
}
