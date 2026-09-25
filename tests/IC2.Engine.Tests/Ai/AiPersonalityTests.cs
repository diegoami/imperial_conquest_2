using IC2.Engine.Ai;
using IC2.Engine.Battle;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;
using BattleFixtures = IC2.Engine.Tests.Battle.Commands.BattleCommandTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// <c>docs/task-catalogue.md</c> T22 Done-when 3: "<em>Personality parameters demonstrably change
/// behaviour: an <c>aggression: 0.9</c> nation attacks in a scripted state where an
/// <c>aggression: 0.1</c> nation does not.</em>"
/// </summary>
/// <remarks>
/// <para>
/// One scripted state (<see cref="AiScriptedStates.TwoArmiesInContact"/>), two personalities, opposite
/// outcomes — asserted three ways, because each answers a different objection:
/// </para>
/// <list type="number">
/// <item>the <em>candidate</em> level, so the difference is visibly the attack gate and not some other
/// action happening to outscore it;</item>
/// <item>the <em>command</em> level, driving a whole turn through the real
/// <see cref="CommandDispatcher"/>, so the difference survives everything else the turn does;</item>
/// <item>the <em>world</em> level, so the 0.9 nation's battle actually resolved and the 0.1 nation's
/// state is untouched.</item>
/// </list>
/// <para>
/// <strong>The two nations are otherwise identical.</strong> Same armies, same morale, same cities, same
/// seed, same relation matrix. <see cref="Only_aggression_differs_between_the_two_runs"/> asserts that
/// mechanically rather than by inspection, so the test cannot quietly become a test of two different
/// situations.
/// </para>
/// </remarks>
public sealed class AiPersonalityTests
{
    private static AiPersonality WithAggression(double aggression) =>
        new(aggression, ExpansionDrive: 0.5, LoyaltyToAlliances: 0.5);

    private static readonly AiPersonality Bold = WithAggression(0.9);
    private static readonly AiPersonality Timid = WithAggression(0.1);

    [Fact]
    public void An_aggression_0_9_nation_proposes_the_attack_and_an_aggression_0_1_nation_does_not()
    {
        Assert.Contains("attack-army", AttackCandidateKinds(Bold));
        Assert.DoesNotContain("attack-army", AttackCandidateKinds(Timid));
    }

    /// <summary>
    /// T82 (#359, bug #357): attacking no longer declares war (<c>decompiled-ai-offers-to-human-seats.md</c>
    /// §4/§5, "no implicit declaration by attack") -- an attack now requires the two already being at
    /// war, so this fixture sets that up directly, isolating the attack-ratio gate this test is actually
    /// about from the separate, unrelated war-target search <see cref="AiMilitaryPhase.ProposeOwnWarDeclaration"/>
    /// now owns.
    /// </summary>
    private static GameState AtWar(AiPersonality personality) =>
        BattleFixtures.AtWar(
            AiScriptedStates.TwoArmiesInContact(personality), AiScriptedStates.Attacker, AiScriptedStates.Defender);

    [Fact]
    public void An_aggression_0_9_nation_issues_the_attack_and_an_aggression_0_1_nation_does_not()
    {
        var bold = AiScriptedStates.DriveOneTurn(AtWar(Bold));
        var timid = AiScriptedStates.DriveOneTurn(AtWar(Timid));

        // No declaration to issue first any more: the two start the turn already at war.
        Assert.Equal(new[] { "battle.attack-army" }, bold.IssuedKinds);
        Assert.DoesNotContain("battle.attack-army", timid.IssuedKinds);

        // Neither run may ever place an order the engine refuses -- Done-when 1's constraint, asserted
        // here too because this is the state in which the AI is most tempted to.
        Assert.Equal(0, bold.Outcome.CommandsRejected);
        Assert.Equal(0, timid.Outcome.CommandsRejected);
        Assert.Equal(0, bold.Outcome.ProjectionMismatches);
    }

    [Fact]
    public void The_battle_really_resolves_for_the_bold_nation_and_the_timid_nation_leaves_the_world_alone()
    {
        var bold = AiScriptedStates.DriveOneTurn(AtWar(Bold));
        var timid = AiScriptedStates.DriveOneTurn(AtWar(Timid));

        Assert.Contains(bold.Events, e => e is BattleResolved);
        Assert.DoesNotContain(timid.Events, e => e is BattleResolved);

        // The defender is gone from the bold run (the toy ruleset's combat.onDefeat is "destroyed") and
        // still standing, untouched, in the timid one.
        Assert.Null(bold.Outcome.State.ArmyById("defender-army"));

        var timidBefore = AtWar(Timid);
        Assert.Equal(
            timidBefore.ArmyById("defender-army"), timid.Outcome.State.ArmyById("defender-army"));
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
    /// The gate is a straight line in aggression, so the personality that flips the decision can be
    /// located exactly. Pins the boundary either side, which is what stops a later edit moving the line
    /// without a test noticing.
    /// </summary>
    [Theory]
    [InlineData(0.0, false)]
    [InlineData(0.1, false)]
    [InlineData(0.55, false)]
    [InlineData(0.56, true)]
    [InlineData(0.9, true)]
    [InlineData(1.0, true)]
    public void The_attack_threshold_moves_with_aggression_and_nothing_else(double aggression, bool attacks)
    {
        var kinds = AttackCandidateKinds(WithAggression(aggression));
        Assert.Equal(attacks, kinds.Contains("attack-army"));
    }

    /// <summary>
    /// The scripted state's own arithmetic, asserted rather than asserted-about: the attacker is 1,480
    /// permille of the defender, which is above what <c>aggression 0.9</c> asks (1,030) and below what
    /// <c>aggression 0.1</c> asks (2,070). If the ruleset's combat weights ever change, this fails first
    /// and says why, instead of the behaviour tests failing for an unexplained reason.
    /// </summary>
    [Fact]
    public void The_scripted_state_sits_between_the_two_personalities_thresholds()
    {
        var state = AiScriptedStates.TwoArmiesInContact(Bold);
        var ruleset = AiScriptedStates.Ruleset;

        var attacker = state.ArmyById("attacker-army")!;
        var defender = state.ArmyById("defender-army")!;
        var ratio = AiView.RatioPermille(
            IC2.Engine.Strength.ArmyPower.Compute(attacker.Units, attacker.Morale, ruleset),
            IC2.Engine.Strength.ArmyPower.Compute(defender.Units, defender.Morale, ruleset));

        var boldGate = AiView.RequiredAttackRatioPermille(AiPersonalityProfile.For(
            state.NationById(AiScriptedStates.Attacker)!).AggressionPermille);
        var timidGate = AiView.RequiredAttackRatioPermille(
            AiPersonalityProfile.For(
                AiScriptedStates.TwoArmiesInContact(Timid).NationById(AiScriptedStates.Attacker)!)
                .AggressionPermille);

        Assert.Equal(1480, ratio);
        Assert.Equal(1030, boldGate);
        Assert.Equal(2070, timidGate);
        Assert.True(boldGate < ratio && ratio < timidGate, $"{boldGate} < {ratio} < {timidGate}");
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
            AiPersonalityProfile.For(state.NationById(AiScriptedStates.Attacker)!),
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
