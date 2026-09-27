using IC2.Engine.Model;

namespace IC2.Engine.Battle.Commands;

/// <summary>
/// The two ids this task's commands must hand to the merged resolvers, resolved from the loaded
/// <see cref="Ruleset"/> rather than written as a call-site literal: the archer unit type, now a real
/// <see cref="Ruleset.ArcherUnitTypeId"/> field (bug #221 N1, T66), and the fortification order, which
/// <see cref="Ruleset"/> still does not name by any field and which this class finds by behaviour
/// instead.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this type exists at all, stated plainly because it is a finding, not a design.</strong>
/// <see cref="InstantBattleResolver.ResolveSiege"/>, <see cref="InstantBattleResolver.ResolveNaval"/> and
/// <see cref="Cities.Capture.CityCaptureResolver.ResolveOutcome"/> all take an
/// <c>archerUnitTypeId</c> and (for the siege pair) a <c>fortifyOrderId</c> as <em>parameters</em>.
/// Until T54 every caller was a test, and every test supplied the ids as its own constants
/// (<c>CaptureTestbed.ArcherUnitTypeId</c>, <c>CaptureTestbed.FortifyOrderId</c>). A command handler has
/// no test to lean on, so the ids have to be resolved from the loaded data — which is what this type
/// does.
/// </para>
/// <para>
/// <strong>The fortification order is found by its behaviour, not by its name.</strong>
/// <c>FUN_0044B27C</c> — the siege entry point <see cref="InstantBattleResolver.ResolveSiege"/> is
/// ported from — opens with <c>if (fort &gt; 100) fort = fort % 100;</c>, i.e. a siege attempt wipes a
/// pending fortification order <strong>[confirmed:
/// decompiled-unit-map-orders-and-record-fields.md]</strong>. That is exactly what
/// <see cref="CityOrderRule.WipedBySiegeAttempt"/> records, so "the order a siege attempt wipes" picks
/// the fortification order out of the generic table without a hardcoded id — and a ruleset that renames
/// its orders keeps working. (<c>Cities/Orders/CityOrderProgressSystem</c> matches the literal
/// <c>"fortify"</c> instead; that is T18's file and is left alone.) No <see cref="Ruleset"/> pointer is
/// added for this one, on the user's 2026-09-23 decision (bug #221 N1): the order is found by behaviour,
/// and adding a redundant pointer field would invite the two to disagree.
/// </para>
/// <para>
/// <strong>The archer type had no behavioural handle, so T54 could only find it by id — and that id now
/// lives on <see cref="Ruleset"/> itself (bug #221 N1, T66).</strong> Nothing in
/// <see cref="UnitTypeRules"/> marks a type as the one the siege formula triples, so there is no
/// behavioural handle equivalent to the fortification order's. T54's search: every field of
/// <see cref="UnitTypeRules"/> (<c>Shots</c> and <c>Range</c> describe the tactical shell this build does
/// not have, and deriving "archer" from them would be inventing a rule), the whole of
/// <see cref="Ruleset"/> for a pointer field (none existed yet), and the research reports for a name the
/// data carries. What the evidence gave was the id itself: the original's unit type code <c>2</c> is the
/// archer type <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md — "archers counted
/// ×3", type table <c>Ar 3,500</c>]</strong>, <c>IC2.Data</c>'s <c>UnitCatalog</c> maps that code to the
/// id <c>"archers"</c>, and both shipped rulesets and <c>Armies/ArmyNaming</c>'s canonical type list use
/// that id. T54 could only carry that id as a call-site constant, because <see cref="Ruleset"/> was
/// outside its Owns list; this task's Owns list reaches <see cref="Ruleset"/>, so
/// <see cref="Ruleset.ArcherUnitTypeId"/> is now that pointer field, and the constant this class used to
/// declare is gone. A ruleset without it declared as a real unit type is refused with the same typed
/// rejection as before, rather than crashing the resolver, which is what
/// <see cref="Strength.SiegeStrength.Attacker"/> would otherwise do.
/// </para>
/// </remarks>
public static class BattleCommandRuleset
{
    /// <summary>
    /// The archer unit type id, when <paramref name="ruleset"/> declares it as a real unit type.
    /// </summary>
    /// <param name="ruleset">The loaded ruleset.</param>
    /// <returns>
    /// <see cref="Ruleset.ArcherUnitTypeId"/> if the ruleset declares that unit type, otherwise
    /// <see langword="null"/>.
    /// </returns>
    public static string? ArcherUnitTypeIdIn(Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        return ruleset.UnitTypeById(ruleset.ArcherUnitTypeId) is null ? null : ruleset.ArcherUnitTypeId;
    }

    /// <summary>
    /// The id of the city order a siege attempt wipes — the fortification order. See this class's
    /// remarks.
    /// </summary>
    /// <param name="ruleset">The loaded ruleset.</param>
    /// <returns>That order's id, or <see langword="null"/> when the ruleset declares no such order.</returns>
    public static string? FortificationOrderIdIn(Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);

        foreach (var order in ruleset.CityOrders.Orders)
        {
            if (order.WipedBySiegeAttempt)
            {
                return order.Id;
            }
        }

        return null;
    }
}
