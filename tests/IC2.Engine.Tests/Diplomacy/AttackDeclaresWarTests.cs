using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// <c>docs/task-catalogue.md</c> T19 DoD 5: "Attacking sets the relation to war before the battle
/// resolves" — <c>TUnitMap_SelectUnit</c>'s own confirmed sequence
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>: "Attacking IS
/// declaring war, with the ally-dragging propagation." This task owns the declaration
/// (<see cref="RelationTransitions.DeclareWar"/>); T16 owns the battle resolver
/// (<see cref="InstantBattleResolver.ResolveField"/>). Nothing in this build wires the two into one
/// "attack" command yet (no command anywhere calls the resolver), so this proves the ordering contract
/// directly: declaring war lands in the matrix before the resolver is even invoked, and the resolver then
/// runs normally against that already-updated state.
/// </summary>
public sealed class AttackDeclaresWarTests
{
    private const string Attacker = "attacker-nation";
    private const string Defender = "defender-nation";

    [Fact]
    public void DoD05_DeclaringWarThenResolving_TheRelationIsWarBeforeTheBattleRuns()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;

        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Attacker, "Attacker"),
            DiplomacyTestbed.Nation(Defender, "Defender"));

        Assert.Equal(codes.Peace, state.Relations.Get(Attacker, Defender));

        var attackerArmy = DiplomacyTestbed.Army(
            "attacker-army", Attacker, morale: 60, DiplomacyTestbed.Unit("light_infantry", 9000, 5));
        var defenderArmy = DiplomacyTestbed.Army(
            "defender-army", Defender, morale: 60, DiplomacyTestbed.Unit("light_infantry", 1000, 5));
        state = state with { Armies = ValueList.Of(attackerArmy, defenderArmy) };

        // The order under test: the attack command declares war FIRST.
        state = RelationTransitions.DeclareWar(state, ruleset, Attacker, Defender);

        // Asserted before the resolver is even called: the relation is already war.
        Assert.Equal(codes.War, state.Relations.Get(Attacker, Defender));

        // The resolver then runs normally against that already-updated state -- it neither reads nor
        // needs the relation itself (T16's Scope: diplomacy and battle do not depend on each other's
        // internals), but this proves the sequence a real attack command will use is legal end to end.
        var (afterBattle, result) = InstantBattleResolver.ResolveField(
            state, "attacker-army", "defender-army", ruleset, BattleTestbed.World,
            DiplomacyTestbed.Rng(), NullEventSink.Instance);

        Assert.NotNull(result);
        Assert.Equal(codes.War, afterBattle.Relations.Get(Attacker, Defender));
    }

    /// <summary>
    /// Mutation proof: if the war declaration were skipped (the ordering bug DoD 5 exists to prevent),
    /// this test would still fail here, before the resolver is ever reached.
    /// </summary>
    [Fact]
    public void DoD05_MutationProof_SkippingTheDeclaration_LeavesThePairAtPeace()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Attacker, "Attacker"), DiplomacyTestbed.Nation(Defender, "Defender"));

        Assert.Equal(ruleset.Diplomacy.StateCodes.Peace, state.Relations.Get(Attacker, Defender));
    }
}
