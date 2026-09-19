using IC2.Engine.Core;

namespace IC2.Engine.Battle.Commands;

/// <summary>
/// An army besieges an adjacent enemy city — the caller T16's
/// <see cref="InstantBattleResolver.ResolveSiege"/> and T17's
/// <see cref="Cities.Capture.CityCaptureResolver.ResolveOutcome"/> never had
/// (<c>docs/task-catalogue.md</c> T54, Done-when 2).
/// </summary>
/// <remarks>
/// <para>
/// <strong>A siege is an order issued from adjacency, not a move onto the city's tile.</strong> The
/// original's order is select your army, then click the town: <em>"first you select the army, then the
/// town, and if they are adjacent there is a siege action"</em>
/// <strong>[confirmed: attack-and-siege-are-adjacency-orders.md — direct user observation of the original game, 2026-09-19]</strong>, matching
/// <c>TUnitMap_SelectUnit</c>'s confirmed "clicking an enemy city… prompts <em>Are you sure you want to
/// attack this …?</em>" <strong>[confirmed:
/// decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong> and the movement walk's inability to
/// step a city marker at all <strong>[confirmed: terrain-move-cost-table-in-dat.md]</strong>. See
/// <see cref="AttackLegality"/>.
/// </para>
/// <para>
/// <strong>This command re-derives no part of the outcome.</strong> It calls
/// <see cref="InstantBattleResolver.ResolveSiege"/> for the strength comparison and the per-attempt
/// attrition, then hands that result <em>unchanged</em> to
/// <see cref="Cities.Capture.CityCaptureResolver.ResolveOutcome"/>, which owns the capture, the loyalty
/// floors, the treasury and tax-base transfer, the unity swing, the defection cascade, elimination and
/// every news line. Nothing between the two calls inspects or adjusts the result.
/// </para>
/// </remarks>
/// <param name="IssuingNationId">The besieging nation — must own <paramref name="AttackerArmyId"/>.</param>
/// <param name="AttackerArmyId">The besieging army.</param>
/// <param name="TargetCityId">The city being besieged.</param>
public sealed record BesiegeCityCommand(string IssuingNationId, string AttackerArmyId, string TargetCityId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "battle.besiege-city";
}

/// <summary>The refusals <see cref="BesiegeCityCommandHandler"/> declares — see <see cref="RejectionCode"/>.</summary>
/// <remarks>
/// Decided by <see cref="AttackLegality"/> before the resolvers are called, so a refused siege leaves the
/// state reference-identical and publishes nothing.
/// </remarks>
public static class BesiegeCityRejections
{
    /// <summary>The besieging army id names no army.</summary>
    public static readonly RejectionCode UnknownArmy = new("battle.siege-unknown-army");

    /// <summary>The besieging army belongs to another nation.</summary>
    public static readonly RejectionCode NotYourArmy = new("battle.siege-not-your-army");

    /// <summary>The target city id names no city.</summary>
    public static readonly RejectionCode UnknownCity = new("battle.unknown-city");

    /// <summary>The city is already the issuing nation's own.</summary>
    public static readonly RejectionCode OwnCity = new("battle.own-city");

    /// <summary>The besieging army is aboard a fleet.</summary>
    public static readonly RejectionCode AttackerEmbarked = new("battle.siege-attacker-embarked");

    /// <summary>The besieging army has no moves left this turn.</summary>
    public static readonly RejectionCode NoMovesLeft = new("battle.siege-no-moves-left");

    /// <summary>The army is not on a tile adjoining the city.</summary>
    public static readonly RejectionCode NotAdjacent = new("battle.siege-not-adjacent");

    /// <summary>
    /// The city exists, but its <see cref="Model.CityState.Owner"/> names a nation the state does not
    /// contain — a data-integrity problem, not an unknown city, and kept distinct from
    /// <see cref="UnknownCity"/> for the same reason <c>BuySupplyRejections.UnresolvableCityOwner</c> is.
    /// <strong>Defensive: unreachable while every city's <c>Owner</c> names a real nation, which the
    /// loaded data always satisfies today.</strong> It is checked rather than left out because the war
    /// gate immediately below would otherwise answer "not at war" for what is really a broken state.
    /// </summary>
    public static readonly RejectionCode UnresolvableCityOwner = new("battle.unresolvable-city-owner");

    /// <summary>The besieging nation is not at war with the city's owner.</summary>
    public static readonly RejectionCode NotAtWar = new("battle.siege-not-at-war");

    /// <summary>
    /// The ruleset declares no archer unit type, which the besieger's strength is measured with — see
    /// <see cref="BattleCommandRuleset"/>.
    /// </summary>
    public static readonly RejectionCode NoArcherUnitType = new("battle.no-archer-unit-type");

    /// <summary>
    /// The ruleset declares no city order a siege attempt wipes, so a fortification word cannot be
    /// decoded — see <see cref="BattleCommandRuleset"/>.
    /// </summary>
    public static readonly RejectionCode NoFortificationOrder = new("battle.no-fortification-order");
}
