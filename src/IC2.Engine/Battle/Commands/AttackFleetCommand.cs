using IC2.Engine.Core;

namespace IC2.Engine.Battle.Commands;

/// <summary>
/// One fleet attacks another at sea — the caller T16's <see cref="InstantBattleResolver.ResolveNaval"/>
/// never had (<c>docs/task-catalogue.md</c> T54, Done-when 7).
/// </summary>
/// <remarks>
/// <para>
/// Same shape as <see cref="AttackArmyCommand"/> and for the same confirmed reason: the order is a click
/// on the target fleet — <c>FUN_0044B5D0</c> is reached "from clicking an enemy fleet (<em>Are you sure
/// you want to attack this fleet ?</em>; blocked by <em>You cannot attack a fleet docked at its own
/// city !</em>)" <strong>[confirmed:
/// decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong> — and the user confirmed the same
/// select-then-target-then-act pattern for fleets from play
/// <strong>[confirmed: attack-and-siege-are-adjacency-orders.md — direct user observation of the original game, 2026-09-19]</strong>.
/// </para>
/// <para>
/// Done-when 7 allows deferring this command with a stated reason. It is implemented rather than
/// deferred: the resolver, the confirmed docking refusal and the gates are all already here, and T23's
/// "one order of each command type" needs it.
/// </para>
/// </remarks>
/// <param name="IssuingNationId">The attacking nation — must own <paramref name="AttackerFleetId"/>.</param>
/// <param name="AttackerFleetId">The attacking fleet.</param>
/// <param name="TargetFleetId">The fleet being attacked.</param>
public sealed record AttackFleetCommand(string IssuingNationId, string AttackerFleetId, string TargetFleetId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "battle.attack-fleet";
}

/// <summary>The refusals <see cref="AttackFleetCommandHandler"/> declares — see <see cref="RejectionCode"/>.</summary>
public static class AttackFleetRejections
{
    /// <summary>The attacking fleet id names no fleet.</summary>
    public static readonly RejectionCode UnknownFleet = new("battle.unknown-fleet");

    /// <summary>The attacking fleet belongs to another nation.</summary>
    public static readonly RejectionCode NotYourFleet = new("battle.not-your-fleet");

    /// <summary>The target fleet id names no fleet.</summary>
    public static readonly RejectionCode UnknownTarget = new("battle.unknown-target-fleet");

    /// <summary>Both fleets belong to one nation.</summary>
    public static readonly RejectionCode SameNation = new("battle.fleet-same-nation");

    /// <summary>Either fleet is still being built, and is therefore not on the map.</summary>
    public static readonly RejectionCode UnderConstruction = new("battle.fleet-under-construction");

    /// <summary>The attacking fleet has no moves left this turn.</summary>
    public static readonly RejectionCode NoMovesLeft = new("battle.fleet-no-moves-left");

    /// <summary>The two fleets are not on adjoining tiles.</summary>
    public static readonly RejectionCode NotAdjacent = new("battle.fleet-not-adjacent");

    /// <summary><em>"You cannot attack a fleet docked at its own city !"</em></summary>
    public static readonly RejectionCode TargetDockedAtItsOwnCity = new("battle.target-docked-at-own-city");

    /// <summary>The two nations are not at war.</summary>
    public static readonly RejectionCode NotAtWar = new("battle.fleet-not-at-war");

    /// <summary>
    /// The ruleset declares no archer unit type, which a carried army's strength is measured with — see
    /// <see cref="BattleCommandRuleset"/>.
    /// </summary>
    public static readonly RejectionCode NoArcherUnitType = new("battle.fleet-no-archer-unit-type");

    /// <summary>
    /// <c>docs/tasks/T63.md</c> Decision 2: both fleets would have zero naval strength
    /// (<see cref="Strength.FleetPower.Compute"/>'s own floor for a low-ship, low-condition fleet), which
    /// the original divides by at the naval call site (the winner's strength is always the divisor, and
    /// both are the winner's when both are zero). Refused here, before any state changes, rather than
    /// reproducing the original's crash.
    /// </summary>
    public static readonly RejectionCode BothFleetsHaveNoStrength = new("battle.fleet-both-have-no-strength");
}
