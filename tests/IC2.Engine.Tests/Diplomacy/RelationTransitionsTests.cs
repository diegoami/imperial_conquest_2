using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// <c>docs/task-catalogue.md</c> T19 DoD 1 (the state machine round-trips all four states and the matrix
/// stays symmetric under every transition) and DoD 2 (the confirmed breaking cooldowns).
/// </summary>
public sealed class RelationTransitionsTests
{
    private static readonly string A = DiplomacyTestbed.FourNationIds[0];
    private static readonly string B = DiplomacyTestbed.FourNationIds[1];

    private static GameState TwoNationState() =>
        DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(A, "Rome"),
            DiplomacyTestbed.Nation(B, "Greece"));

    // ---- DoD 1: round-trips all four states, matrix stays symmetric under every transition ----

    [Fact]
    public void DoD01_PeaceToTradeToAllianceToWarToPeace_RoundTrips_MatrixStaysSymmetric()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = TwoNationState();

        Assert.Equal(codes.Peace, state.Relations.Get(A, B));
        Assert.True(state.Relations.IsWellFormed());

        state = state with { Relations = state.Relations.WithRelation(A, B, codes.Trade) };
        Assert.Equal(codes.Trade, state.Relations.Get(A, B));
        Assert.Equal(codes.Trade, state.Relations.Get(B, A));
        Assert.True(state.Relations.IsWellFormed());

        state = RelationTransitions.FormAlliance(state, ruleset, A, B);
        Assert.Equal(codes.Alliance, state.Relations.Get(A, B));
        Assert.Equal(codes.Alliance, state.Relations.Get(B, A));
        Assert.True(state.Relations.IsWellFormed());

        state = RelationTransitions.DeclareWar(state, ruleset, A, B);
        Assert.Equal(codes.War, state.Relations.Get(A, B));
        Assert.Equal(codes.War, state.Relations.Get(B, A));
        Assert.True(state.Relations.IsWellFormed());

        state = RelationTransitions.BreakToPeace(state, ruleset, A, B);
        Assert.Equal(ruleset.Diplomacy.CooldownAfterEndedWar, state.Relations.Get(A, B));
        Assert.Equal(ruleset.Diplomacy.CooldownAfterEndedWar, state.Relations.Get(B, A));
        Assert.True(state.Relations.IsWellFormed());
    }

    /// <summary>
    /// The mirror of the round trip above, proving DoD 1's "under every transition" is not a claim about
    /// one direction only: A→B and B→A must both land the exact same cell values.
    /// </summary>
    [Fact]
    public void DoD01_TheMirroredDirection_RoundTripsIdentically()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = TwoNationState();

        state = state with { Relations = state.Relations.WithRelation(B, A, codes.Trade) };
        state = RelationTransitions.FormAlliance(state, ruleset, B, A);
        state = RelationTransitions.DeclareWar(state, ruleset, B, A);
        state = RelationTransitions.BreakToPeace(state, ruleset, B, A);

        Assert.Equal(ruleset.Diplomacy.CooldownAfterEndedWar, state.Relations.Get(A, B));
        Assert.Equal(ruleset.Diplomacy.CooldownAfterEndedWar, state.Relations.Get(B, A));
        Assert.True(state.Relations.IsWellFormed());
    }

    /// <summary>
    /// Mutation proof: if <see cref="RelationTransitions.DeclareWar"/> wrote only one cell (the classic
    /// asymmetric-write bug DoD 1 exists to catch), this test fails even though the "forward" read still
    /// looks right.
    /// </summary>
    [Fact]
    public void DoD01_DeclareWar_WritesBothCells()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = RelationTransitions.DeclareWar(TwoNationState(), ruleset, A, B);

        Assert.Equal(ruleset.Diplomacy.StateCodes.War, state.Relations.Get(B, A));
    }

    // ---- DoD 2: breaking cooldowns ----

    [Fact]
    public void DoD02_BreakingTrade_SetsMinusEight()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = TwoNationState() with
        {
            Relations = TwoNationState().Relations.WithRelation(A, B, ruleset.Diplomacy.StateCodes.Trade),
        };

        state = RelationTransitions.BreakToPeace(state, ruleset, A, B);

        Assert.Equal(-8, state.Relations.Get(A, B));
        Assert.Equal(ruleset.Diplomacy.CooldownAfterBrokenTrade, state.Relations.Get(A, B));
    }

    [Fact]
    public void DoD02_BreakingAlliance_SetsMinusTwentyFour()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = RelationTransitions.FormAlliance(TwoNationState(), ruleset, A, B);

        state = RelationTransitions.BreakToPeace(state, ruleset, A, B);

        Assert.Equal(-24, state.Relations.Get(A, B));
        Assert.Equal(ruleset.Diplomacy.CooldownAfterBrokenAlliance, state.Relations.Get(A, B));
    }

    [Fact]
    public void DoD02_EndingWar_SetsMinusEighteen()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = RelationTransitions.DeclareWar(TwoNationState(), ruleset, A, B);

        state = RelationTransitions.BreakToPeace(state, ruleset, A, B);

        Assert.Equal(-18, state.Relations.Get(A, B));
        Assert.Equal(ruleset.Diplomacy.CooldownAfterEndedWar, state.Relations.Get(A, B));
    }

    /// <summary>
    /// Mutation proof: if the cooldown mapping used the same constant for every previous state (a
    /// copy-paste of one branch over the other two), this named test is the one that would still pass
    /// while its two siblings above failed — so together the three pin the mapping, not just "some
    /// negative number came out".
    /// </summary>
    [Fact]
    public void DoD02_TheThreeCooldowns_AreAllDifferent()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        Assert.NotEqual(ruleset.Diplomacy.CooldownAfterBrokenTrade, ruleset.Diplomacy.CooldownAfterBrokenAlliance);
        Assert.NotEqual(ruleset.Diplomacy.CooldownAfterBrokenAlliance, ruleset.Diplomacy.CooldownAfterEndedWar);
        Assert.NotEqual(ruleset.Diplomacy.CooldownAfterBrokenTrade, ruleset.Diplomacy.CooldownAfterEndedWar);
    }

    // ---- Rework round 2, N-b: HasAnAllyAtWarWithAnyone's own "at war" half ----

    /// <summary>
    /// Rework round 2, N-b: every existing caller-side test that exercises
    /// <see cref="RelationTransitions.HasAnAllyAtWarWithAnyone"/> gives the ally an active war, so
    /// reducing the check to "has any ally at all" (dropping the <c>IsAtWarWithAnyone(ally)</c> half
    /// entirely) would still pass every one of them. This is the missing half: an ally that is at peace
    /// with everyone must not trip the check.
    /// </summary>
    [Fact]
    public void HasAnAllyAtWarWithAnyone_IsFalse_WhenTheAllyIsAtPeaceWithEveryone()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var ally = DiplomacyTestbed.FourNationIds[2];
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(A, "Rome"), DiplomacyTestbed.Nation(B, "Greece"),
            DiplomacyTestbed.Nation(ally, "Ally"));
        state = state with
        {
            Relations = state.Relations.WithRelation(A, ally, ruleset.Diplomacy.StateCodes.Alliance),
        };

        // The ally has no war with anyone (including A itself, which the method's own loop excludes by id).
        Assert.False(RelationTransitions.HasAnAllyAtWarWithAnyone(state, ruleset, A));
    }

    /// <summary>The mirrored, positive case: the same ally, now at war with a third nation.</summary>
    [Fact]
    public void HasAnAllyAtWarWithAnyone_IsTrue_WhenTheAllyIsAtWarWithAThirdNation()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var ally = DiplomacyTestbed.FourNationIds[2];
        var thirdParty = DiplomacyTestbed.FourNationIds[3];
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(A, "Rome"), DiplomacyTestbed.Nation(B, "Greece"),
            DiplomacyTestbed.Nation(ally, "Ally"), DiplomacyTestbed.Nation(thirdParty, "ThirdParty"));
        state = state with
        {
            Relations = state.Relations
                .WithRelation(A, ally, ruleset.Diplomacy.StateCodes.Alliance)
                .WithRelation(ally, thirdParty, ruleset.Diplomacy.StateCodes.War),
        };

        Assert.True(RelationTransitions.HasAnAllyAtWarWithAnyone(state, ruleset, A));
    }
}
