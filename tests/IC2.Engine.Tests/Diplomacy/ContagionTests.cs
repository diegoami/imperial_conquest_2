using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// <c>docs/task-catalogue.md</c> T19 DoD 4: "Allying drags the ally into the partner's wars; declaring
/// war drags in the target's allies." Both directions, and — per <c>docs/build-process.md</c>'s
/// two-entity probe — a nation nowhere near the transition survives completely untouched.
/// </summary>
public sealed class ContagionTests
{
    private const string Rome = "rome";
    private const string Greece = "greece";
    private const string Bithynia = "bithynia";
    private const string Media = "media";
    private const string Armenia = "armenia"; // the untouched fifth nation

    private static GameState FiveNationState() =>
        DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Rome, "Rome"),
            DiplomacyTestbed.Nation(Greece, "Greece"),
            DiplomacyTestbed.Nation(Bithynia, "Bithynia"),
            DiplomacyTestbed.Nation(Media, "Media"),
            DiplomacyTestbed.Nation(Armenia, "Armenia"));

    /// <summary>
    /// DoD 4, first half: forming an alliance drags the proposer into the new ally's existing wars.
    /// </summary>
    [Fact]
    public void DoD04_FormingAnAlliance_DragsTheProposerIntoThePartnersWars()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = FiveNationState();

        // Greece is already at war with Bithynia.
        state = RelationTransitions.DeclareWar(state, ruleset, Greece, Bithynia);

        // Rome allies with Greece.
        state = RelationTransitions.FormAlliance(state, ruleset, Rome, Greece);

        Assert.Equal(codes.Alliance, state.Relations.Get(Rome, Greece));
        Assert.Equal(codes.War, state.Relations.Get(Rome, Bithynia));

        // Two-entity probe: Media and Armenia, nowhere near this alliance, are untouched.
        Assert.Equal(codes.Peace, state.Relations.Get(Rome, Media));
        Assert.Equal(codes.Peace, state.Relations.Get(Rome, Armenia));
        Assert.Equal(codes.Peace, state.Relations.Get(Bithynia, Media));
        Assert.Equal(codes.Peace, state.Relations.Get(Greece, Armenia));
        Assert.True(state.Relations.IsWellFormed());
    }

    /// <summary>DoD 4, second half: declaring war drags the decreeing nation into the target's alliances.</summary>
    [Fact]
    public void DoD04_DeclaringWar_DragsTheDecreeingNationIntoTheTargetsAlliances()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = FiveNationState();

        // Greece is already allied with Bithynia.
        state = RelationTransitions.FormAlliance(state, ruleset, Greece, Bithynia);

        // Rome declares war on Greece.
        state = RelationTransitions.DeclareWar(state, ruleset, Rome, Greece);

        Assert.Equal(codes.War, state.Relations.Get(Rome, Greece));
        Assert.Equal(codes.War, state.Relations.Get(Rome, Bithynia));

        // Two-entity probe: Media and Armenia survive untouched.
        Assert.Equal(codes.Peace, state.Relations.Get(Rome, Media));
        Assert.Equal(codes.Peace, state.Relations.Get(Rome, Armenia));
        Assert.Equal(codes.Peace, state.Relations.Get(Bithynia, Armenia));
        Assert.True(state.Relations.IsWellFormed());
    }

    /// <summary>
    /// The cascade is genuinely recursive, not a single hop: Rome allies with Greece (at war with
    /// Bithynia); the resulting Rome-vs-Bithynia declaration itself cascades again, because Bithynia is
    /// allied with Media. Media ends up at war with Rome two hops away from the original proposal.
    /// </summary>
    [Fact]
    public void DoD04_TheCascadeIsRecursive_TwoHopsDragsInASecondNation()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = FiveNationState();

        state = RelationTransitions.DeclareWar(state, ruleset, Greece, Bithynia);
        state = RelationTransitions.FormAlliance(state, ruleset, Bithynia, Media);

        state = RelationTransitions.FormAlliance(state, ruleset, Rome, Greece);

        Assert.Equal(codes.Alliance, state.Relations.Get(Rome, Greece));
        Assert.Equal(codes.War, state.Relations.Get(Rome, Bithynia));
        Assert.Equal(codes.War, state.Relations.Get(Rome, Media));

        // Armenia, reachable from nothing in this chain, is still untouched.
        Assert.Equal(codes.Peace, state.Relations.Get(Rome, Armenia));
        Assert.True(state.Relations.IsWellFormed());
    }

    /// <summary>
    /// Mutation proof: delete the alliance cascade loop and this test — which asserts the dragged-in war,
    /// not merely the alliance itself — fails, while a test that only checked Rome-vs-Greece would not.
    /// </summary>
    [Fact]
    public void DoD04_MutationProof_RemovingTheAllianceCascadeWouldFailThisAssertion()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = RelationTransitions.DeclareWar(FiveNationState(), ruleset, Greece, Bithynia);
        state = RelationTransitions.FormAlliance(state, ruleset, Rome, Greece);

        Assert.Equal(ruleset.Diplomacy.StateCodes.War, state.Relations.Get(Rome, Bithynia));
    }

    /// <summary>Already being at war with the cascade target is a no-op: no double-processing, no throw.</summary>
    [Fact]
    public void DoD04_CascadeTarget_AlreadyAtWar_IsANoOp()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = FiveNationState();

        state = RelationTransitions.DeclareWar(state, ruleset, Greece, Bithynia);
        state = RelationTransitions.DeclareWar(state, ruleset, Rome, Bithynia);

        state = RelationTransitions.FormAlliance(state, ruleset, Rome, Greece);

        Assert.Equal(codes.War, state.Relations.Get(Rome, Bithynia));
        Assert.True(state.Relations.IsWellFormed());
    }
}
