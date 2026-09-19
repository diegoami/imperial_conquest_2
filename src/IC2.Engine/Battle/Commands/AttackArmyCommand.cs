using IC2.Engine.Core;

namespace IC2.Engine.Battle.Commands;

/// <summary>
/// One army attacks another on the strategic map — the caller T16's
/// <see cref="InstantBattleResolver.ResolveField"/> never had (<c>docs/task-catalogue.md</c> T54,
/// Done-when 1).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The original's own order.</strong> <c>FUN_0044AEE4</c> is "reached from
/// <c>TUnitMap_SelectUnit</c> when one army attacks another on the strategic map"
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>, and
/// <c>FUN_0044d734</c>'s tail reaches the same function when a move ends on another army's marker
/// ("landing on another army's marker triggers <c>FUN_0044aee4</c>"
/// <strong>[confirmed: decompiled-army-movement-and-river-cost.md]</strong>). Both are a click on a
/// target, not an occupation of its tile: an army marker is map code <c>200..247</c>, and the movement
/// walk cannot step any code ≥ 12, so the attacker resolves from the tile it is standing on — see
/// <see cref="AttackLegality"/> for the adjacency reading that follows from that.
/// </para>
/// <para>
/// <strong>This command invents no combat rule.</strong> Every number, threshold and outcome is
/// <see cref="InstantBattleResolver.ResolveField"/>'s, unchanged; this type contributes the legality
/// gates, the typed refusals and the dispatch.
/// </para>
/// </remarks>
/// <param name="IssuingNationId">The attacking nation — must own <paramref name="AttackerArmyId"/>.</param>
/// <param name="AttackerArmyId">The attacking army.</param>
/// <param name="TargetArmyId">The army being attacked.</param>
public sealed record AttackArmyCommand(string IssuingNationId, string AttackerArmyId, string TargetArmyId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "battle.attack-army";
}

/// <summary>
/// The refusals <see cref="AttackArmyCommandHandler"/> declares, in this task's own directory — see
/// <see cref="RejectionCode"/>.
/// </summary>
/// <remarks>
/// Every one of these is decided by <see cref="AttackLegality"/> <em>before</em> the resolver is called,
/// so a refused attack leaves the state reference-identical (<c>Assert.Same</c>) and publishes nothing.
/// Four of them exist because <see cref="InstantBattleResolver.ResolveField"/> <em>throws</em> on the
/// same conditions — an unknown id, an embarked army on either side, one nation on both sides — and a
/// handler must refuse rather than throw (<see cref="ICommandHandler{TCommand}"/>'s own contract:
/// "illegality is an outcome, not an error").
/// </remarks>
public static class AttackArmyRejections
{
    /// <summary>The attacking army id names no army.</summary>
    public static readonly RejectionCode UnknownArmy = new("battle.unknown-army");

    /// <summary>The attacking army belongs to another nation.</summary>
    public static readonly RejectionCode NotYourArmy = new("battle.not-your-army");

    /// <summary>The target army id names no army.</summary>
    public static readonly RejectionCode UnknownTarget = new("battle.unknown-target-army");

    /// <summary>Both armies belong to one nation — including an army ordered to attack itself.</summary>
    public static readonly RejectionCode SameNation = new("battle.same-nation");

    /// <summary>The attacking army is aboard a fleet; a field battle is fought ashore.</summary>
    public static readonly RejectionCode AttackerEmbarked = new("battle.attacker-embarked");

    /// <summary>The target army is aboard a fleet, and is attacked through its carrier instead.</summary>
    public static readonly RejectionCode TargetEmbarked = new("battle.target-embarked");

    /// <summary>The attacking army has no moves left this turn.</summary>
    public static readonly RejectionCode NoMovesLeft = new("battle.no-moves-left");

    /// <summary>The two armies are not on adjoining tiles.</summary>
    public static readonly RejectionCode NotAdjacent = new("battle.not-adjacent");

    /// <summary>
    /// The two nations are not at war. Attacking <em>is</em> declaring war in the original, and this
    /// engine spells that as two commands in the confirmed order — see <see cref="AttackLegality"/>.
    /// </summary>
    public static readonly RejectionCode NotAtWar = new("battle.not-at-war");
}
