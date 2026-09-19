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
    private const string Pontus = "pontus"; // the third-hop nation (N1)

    private static GameState FiveNationState() =>
        DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Rome, "Rome"),
            DiplomacyTestbed.Nation(Greece, "Greece"),
            DiplomacyTestbed.Nation(Bithynia, "Bithynia"),
            DiplomacyTestbed.Nation(Media, "Media"),
            DiplomacyTestbed.Nation(Armenia, "Armenia"));

    private static GameState SixNationState() =>
        DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Rome, "Rome"),
            DiplomacyTestbed.Nation(Greece, "Greece"),
            DiplomacyTestbed.Nation(Bithynia, "Bithynia"),
            DiplomacyTestbed.Nation(Media, "Media"),
            DiplomacyTestbed.Nation(Armenia, "Armenia"),
            DiplomacyTestbed.Nation(Pontus, "Pontus"));

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

    /// <summary>
    /// N1: <see cref="RelationTransitions.DeclareWar"/>'s <em>own</em> recursion, proved genuinely
    /// multi-level rather than a single extra hop indistinguishable from one flat, non-recursive write.
    /// Greece is at war with Bithynia (drags Rome in via <see cref="RelationTransitions.FormAlliance"/>'s
    /// own cascade); Bithynia is allied with Media (drags Rome in via <c>DeclareWar</c>'s own recursion,
    /// one level); Media is allied with Pontus (drags Rome in via a <em>second</em> level of that same
    /// recursion). A mutation that replaced the recursive call with a single direct relation write would
    /// still pass the two-hop test above (Rome-vs-Media is one level from Rome-vs-Bithynia either way) but
    /// would leave Rome-vs-Pontus at peace, since nothing would ever re-examine Media's own allies.
    /// </summary>
    [Fact]
    public void N1_DeclareWarsOwnRecursion_GoesGenuinelyMultiLevelDeep()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = SixNationState();

        state = RelationTransitions.DeclareWar(state, ruleset, Greece, Bithynia);
        state = RelationTransitions.FormAlliance(state, ruleset, Bithynia, Media);
        state = RelationTransitions.FormAlliance(state, ruleset, Media, Pontus);

        state = RelationTransitions.FormAlliance(state, ruleset, Rome, Greece);

        Assert.Equal(codes.War, state.Relations.Get(Rome, Bithynia));
        Assert.Equal(codes.War, state.Relations.Get(Rome, Media));
        Assert.Equal(codes.War, state.Relations.Get(Rome, Pontus));

        // Two-entity probe: Armenia, reachable from nothing in this chain, is still untouched.
        Assert.Equal(codes.Peace, state.Relations.Get(Rome, Armenia));
        Assert.True(state.Relations.IsWellFormed());

        // Hazard: every war declaration in the cascade -- FormAlliance's own hop AND both levels of
        // DeclareWar's own recursion -- writes its own war.declared line (news-log-format-and-messages.md
        // Q4 #1-2: "Each of those gets its own line, written directly after"), not just the first one.
        var lines = state.NewsLog.Slots.Select(e => e.Text).ToList();
        Assert.Contains("Rome declares war on Bithynia.", lines);
        Assert.Contains("Rome declares war on Media.", lines);
        Assert.Contains("Rome declares war on Pontus.", lines);
    }

    /// <summary>
    /// Mutation proof for N1: replacing <c>DeclareWar</c>'s own recursive call with a single direct
    /// relation write (no further recursion) leaves Rome-vs-Pontus at peace, failing this exact
    /// assertion, while <see cref="DoD04_TheCascadeIsRecursive_TwoHopsDragsInASecondNation"/> above would
    /// still pass under the same mutation -- which is precisely why that test alone did not pin this.
    /// </summary>
    [Fact]
    public void N1_MutationProof_AFlattenedRecursionWouldLeaveTheThirdHopAtPeace()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = SixNationState();

        state = RelationTransitions.DeclareWar(state, ruleset, Greece, Bithynia);
        state = RelationTransitions.FormAlliance(state, ruleset, Bithynia, Media);
        state = RelationTransitions.FormAlliance(state, ruleset, Media, Pontus);
        state = RelationTransitions.FormAlliance(state, ruleset, Rome, Greece);

        Assert.Equal(ruleset.Diplomacy.StateCodes.War, state.Relations.Get(Rome, Pontus));
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
