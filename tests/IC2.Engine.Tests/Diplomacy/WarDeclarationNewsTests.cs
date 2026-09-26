using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// <c>docs/task-catalogue.md</c> T19 Hazards: "A war declaration involving a human is uppercased in
/// full... An alliance also declares war on each of the ally's enemies." Both news lines are asserted
/// against the exact literal text, not just "something got appended".
/// </summary>
public sealed class WarDeclarationNewsTests
{
    private const string Ai1 = "ai-1";
    private const string Ai2 = "ai-2";
    private const string HumanNation = "human-nation";

    [Fact]
    public void WarDeclaration_BetweenTwoAiNations_IsLowercase()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Ai1, "Dacia"), DiplomacyTestbed.Nation(Ai2, "Gaul"));

        state = RelationTransitions.DeclareWar(state, ruleset, Ai1, Ai2);

        var lines = state.NewsLog.Slots.Select(e => e.Text).ToList();
        Assert.Contains("Dacia declares war on Gaul.", lines);
    }

    [Fact]
    public void WarDeclaration_InvolvingAHuman_IsUppercasedInFull()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(HumanNation, "Rome", control: SeatControl.Human),
            DiplomacyTestbed.Nation(Ai1, "Gaul"));

        state = RelationTransitions.DeclareWar(state, ruleset, HumanNation, Ai1);

        var lines = state.NewsLog.Slots.Select(e => e.Text).ToList();
        Assert.Contains("ROME DECLARES WAR ON GAUL.", lines);
        Assert.DoesNotContain("Rome declares war on Gaul.", lines);
    }

    /// <summary>The shouting rule applies whichever side is human -- the target, not only the decreeing nation.</summary>
    [Fact]
    public void WarDeclaration_WhenTheTargetIsHuman_IsAlsoUppercased()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Ai1, "Gaul"),
            DiplomacyTestbed.Nation(HumanNation, "Rome", control: SeatControl.Human));

        state = RelationTransitions.DeclareWar(state, ruleset, Ai1, HumanNation);

        var lines = state.NewsLog.Slots.Select(e => e.Text).ToList();
        Assert.Contains("GAUL DECLARES WAR ON ROME.", lines);
    }

    /// <summary>Mutation proof: deleting the shouting call leaves the human-involved line lowercase.</summary>
    [Fact]
    public void WarDeclaration_MutationProof_TheHumanFlagActuallyGatesTheCase()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var aiOnly = DiplomacyTestbed.StateOf(DiplomacyTestbed.Nation(Ai1, "Dacia"), DiplomacyTestbed.Nation(Ai2, "Gaul"));
        var withHuman = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Ai1, "Dacia", control: SeatControl.Human), DiplomacyTestbed.Nation(Ai2, "Gaul"));

        var aiOnlyLine = RelationTransitions.DeclareWar(aiOnly, ruleset, Ai1, Ai2)
            .NewsLog.Slots.Select(e => e.Text).Single();
        var withHumanLine = RelationTransitions.DeclareWar(withHuman, ruleset, Ai1, Ai2)
            .NewsLog.Slots.Select(e => e.Text).Single();

        Assert.NotEqual(aiOnlyLine, withHumanLine);
        Assert.Equal("Dacia declares war on Gaul.", aiOnlyLine);
        Assert.Equal("DACIA DECLARES WAR ON GAUL.", withHumanLine);
    }

    /// <summary>Alliance lines are never uppercased, even between two human-adjacent nations' allies.</summary>
    [Fact]
    public void AllianceFormed_IsNeverUppercased()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(HumanNation, "Rome", control: SeatControl.Human),
            DiplomacyTestbed.Nation(Ai1, "Dacia"));

        state = RelationTransitions.FormAlliance(state, ruleset, Ai1, HumanNation);

        var lines = state.NewsLog.Slots.Select(e => e.Text).ToList();
        Assert.Contains("Dacia forms an alliance with Rome.", lines);
    }

    /// <summary>
    /// N3 (rework round 1): the shouting rule applies to a cascade hop's own dragged-in pair, not only a
    /// direct declaration -- <c>DeclareWarWithoutFurtherCascade</c>'s upper-casing (RelationTransitions.cs)
    /// had no test of its own before this. Dacia (AI) declares war on Gaul (AI); Gaul is allied to Rome
    /// (human), so the cascade drags Dacia into war with Rome too. The direct line (both AI) stays
    /// lowercase; the cascade line (Rome is human) is uppercased in full. A mutation that hardcodes
    /// <c>involvesHuman = false</c> in the cascade hop would leave the cascade line lowercase, failing the
    /// second assertion below.
    /// </summary>
    [Fact]
    public void WarDeclarationCascade_DraggedInPairInvolvingAHuman_IsUppercasedInFull()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        const string Dacia = "dacia";
        const string Gaul = "gaul";
        const string Rome = "rome";
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Dacia, "Dacia"),
            DiplomacyTestbed.Nation(Gaul, "Gaul"),
            DiplomacyTestbed.Nation(Rome, "Rome", control: SeatControl.Human));

        state = RelationTransitions.FormAlliance(state, ruleset, Gaul, Rome);
        state = RelationTransitions.DeclareWar(state, ruleset, Dacia, Gaul);

        var lines = state.NewsLog.Slots.Select(e => e.Text).ToList();
        Assert.Contains("Dacia declares war on Gaul.", lines);
        Assert.Contains("DACIA DECLARES WAR ON ROME.", lines);
        Assert.DoesNotContain("Dacia declares war on Rome.", lines);
    }

    /// <summary>
    /// Hazard: "An alliance also declares war on each of the ally's enemies" — the cascade declaration
    /// follows the alliance line, with its own exact literal.
    /// </summary>
    [Fact]
    public void AllianceCascade_WritesItsOwnWarDeclaredLine_AfterTheAllianceLine()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation("dacia", "Dacia"),
            DiplomacyTestbed.Nation("rome", "Rome"),
            DiplomacyTestbed.Nation("gaul", "Gaul"));

        state = RelationTransitions.DeclareWar(state, ruleset, "rome", "gaul");
        state = RelationTransitions.FormAlliance(state, ruleset, "dacia", "rome");

        var lines = state.NewsLog.Slots.Select(e => e.Text).ToList();
        var allianceIndex = lines.IndexOf("Dacia forms an alliance with Rome.");
        var warIndex = lines.IndexOf("Dacia declares war on Gaul.");

        Assert.True(allianceIndex >= 0);
        Assert.True(warIndex > allianceIndex);
    }
}
