using IC2.Engine.Ai;
using IC2.Engine.Battle;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;
using BattleFixtures = IC2.Engine.Tests.Battle.Commands.BattleCommandTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T156 (issue #925): the AI's army decisions no longer depend on <c>aggression</c>. The original's
/// target tree decides for armies, with a fixed 100-threshold rather than the personality-shaped ratio
/// gate. Fleet attacks still use the gate (T156's Owns list keeps <see cref="AiView.RequiredAttackRatioPermille"/>
/// for the naval half). These tests pin the army side's new semantics.
/// </summary>
public sealed class AiPersonalityTests
{
    private static AiPersonality WithAggression(double aggression) =>
        new(aggression, ExpansionDrive: 0.5, LoyaltyToAlliances: 0.5);

    private static readonly AiPersonality Bold = WithAggression(0.9);
    private static readonly AiPersonality Timid = WithAggression(0.1);

    /// <summary>
    /// The tree decides for armies, not the personality: a weaker enemy army at war with an army-score
    /// above the 100 threshold is attacked at every aggression the personality can take.
    /// </summary>
    [Fact]
    public void An_army_attack_is_proposed_for_any_aggression_when_the_tree_picks_it()
    {
        Assert.Contains("attack-army", AttackCandidateKinds(Bold));
        Assert.Contains("attack-army", AttackCandidateKinds(Timid));
    }

    /// <summary>
    /// T82 (#359, bug #357): attacking no longer declares war (<c>decompiled-ai-offers-to-human-seats.md</c>
    /// §4/§5, "no implicit declaration by attack") -- an attack now requires the two already being at
    /// war, so this fixture sets that up directly, isolating the tree-driven attack decision this test
    /// is actually about from the separate, unrelated war-target search
    /// <see cref="AiMilitaryPhase.ProposeOwnWarDeclaration"/> now owns.
    /// </summary>
    private static GameState AtWar(AiPersonality personality) =>
        BattleFixtures.AtWar(
            AiScriptedStates.TwoArmiesInContact(personality), AiScriptedStates.Attacker, AiScriptedStates.Defender);

    /// <summary>
    /// The tree-driven attack runs to completion for both personalities: the army candidate is issued,
    /// the battle resolves, and no command is rejected.
    /// </summary>
    [Fact]
    public void The_tree_driven_attack_resolves_for_either_personalities_aggression()
    {
        var bold = AiScriptedStates.DriveOneTurn(AtWar(Bold));
        var timid = AiScriptedStates.DriveOneTurn(AtWar(Timid));

        // An accepted attack zeroes the attacker's moves, so the driver's garrison fallback (which only
        // runs for an army with moves left and an unchanged tile) never follows it: exactly one command.
        Assert.Equal(new[] { "battle.attack-army" }, bold.IssuedKinds);
        Assert.Equal(new[] { "battle.attack-army" }, timid.IssuedKinds);

        Assert.Equal(0, bold.Outcome.CommandsRejected);
        Assert.Equal(0, timid.Outcome.CommandsRejected);
        Assert.Equal(0, bold.Outcome.ProjectionMismatches);
        Assert.Equal(0, timid.Outcome.ProjectionMismatches);
    }

    /// <summary>
    /// The battle really resolves in both runs: the defender is gone from both post-turn states.
    /// </summary>
    [Fact]
    public void The_battle_resolves_under_either_personalities_aggression()
    {
        var bold = AiScriptedStates.DriveOneTurn(AtWar(Bold));
        var timid = AiScriptedStates.DriveOneTurn(AtWar(Timid));

        Assert.Contains(bold.Events, e => e is BattleResolved);
        Assert.Contains(timid.Events, e => e is BattleResolved);

        Assert.Null(bold.Outcome.State.ArmyById("defender-army"));
        Assert.Null(timid.Outcome.State.ArmyById("defender-army"));
    }

    /// <summary>
    /// The fixture's own control: the two runs differ in exactly one field of one record. Without this,
    /// a later edit that changed, say, the attacker's troop count alongside its aggression would still
    /// make the other tests pass while proving nothing about personality.
    /// </summary>
    [Fact]
    public void Only_aggression_differs_between_the_two_runs()
    {
        var bold = AiScriptedStates.TwoArmiesInContact(Bold);
        var timid = AiScriptedStates.TwoArmiesInContact(Timid);

        Assert.Equal(AiSubstantiveState.Of(bold) with { Nations = timid.Nations }, AiSubstantiveState.Of(timid));

        var boldNation = bold.NationById(AiScriptedStates.Attacker)!;
        var timidNation = timid.NationById(AiScriptedStates.Attacker)!;
        Assert.Equal(boldNation with { Personality = timidNation.Personality }, timidNation);
        Assert.NotEqual(boldNation.Personality!.Aggression, timidNation.Personality!.Aggression);
        Assert.Equal(boldNation.Personality.ExpansionDrive, timidNation.Personality.ExpansionDrive);
        Assert.Equal(boldNation.Personality.LoyaltyToAlliances, timidNation.Personality.LoyaltyToAlliances);
    }

    /// <summary>
    /// The military phase's own candidate kinds for a given personality, read straight off
    /// <see cref="AiMilitaryPhase.Propose"/> rather than inferred from what the turn happened to do.
    /// </summary>
    private static List<string> AttackCandidateKinds(AiPersonality personality)
    {
        var state = AtWar(personality);
        var view = new AiView(
            state, AiScriptedStates.Ruleset, AiScriptedStates.World, AiScriptedStates.Attacker);
        var candidates = new List<AiCandidate>();

        AiMilitaryPhase.Propose(
            view,
            AiPersonalityProfile.For(state.NationById(AiScriptedStates.Attacker)!, view.Ruleset),
            SplitMix64Rng.ForStream(1, "ai.turn"),
            Array.Empty<string>(),
            candidates);

        var kinds = new List<string>();
        foreach (var candidate in candidates)
        {
            kinds.Add(candidate.Kind);
        }

        return kinds;
    }
}