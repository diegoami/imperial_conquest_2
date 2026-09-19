using IC2.Engine.Model;

namespace IC2.Engine.Battle.Commands;

/// <summary>
/// The two ruleset ids this task's commands must hand to the merged resolvers, resolved from the loaded
/// <see cref="Ruleset"/> rather than written as call-site literals.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this type exists at all, stated plainly because it is a finding, not a design.</strong>
/// <see cref="InstantBattleResolver.ResolveSiege"/>, <see cref="InstantBattleResolver.ResolveNaval"/> and
/// <see cref="Cities.Capture.CityCaptureResolver.ResolveOutcome"/> all take an
/// <c>archerUnitTypeId</c> and (for the siege pair) a <c>fortifyOrderId</c> as <em>parameters</em>,
/// because <see cref="Ruleset"/> names neither: <see cref="Strength.SiegeStrength.Attacker"/>'s own
/// remarks say so for the archer type ("no <see cref="Ruleset"/> field names the archer type"), and
/// <see cref="CityOrderRules"/> is a plain list that names no entry as "the" fortification order. Until
/// T54 every caller was a test, and every test supplied the ids as its own constants
/// (<c>CaptureTestbed.ArcherUnitTypeId</c>, <c>CaptureTestbed.FortifyOrderId</c>). A command handler has
/// no test to lean on, so the ids have to be resolved from the loaded data — which is what this type
/// does, and the reason a <see cref="Ruleset"/> field for each would be the better fix. That field is
/// <em>not</em> added here: <c>src/IC2.Engine/Model/Ruleset.cs</c> is not in T54's Owns list, and the
/// task's brief is to wire merged rules and invent nothing.
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
/// <c>"fortify"</c> instead; that is T18's file and is left alone.)
/// </para>
/// <para>
/// <strong>The archer type is found by id, and that is the weaker of the two.</strong> Nothing in
/// <see cref="UnitTypeRules"/> marks a type as the one the siege formula triples, so there is no
/// behavioural handle equivalent to the one above. What was searched: every field of
/// <see cref="UnitTypeRules"/> (<c>Shots</c> and <c>Range</c> describe the tactical shell this build does
/// not have, and deriving "archer" from them would be inventing a rule), the whole of
/// <see cref="Ruleset"/> for a pointer field, and the research reports for a name the data carries. What
/// the evidence does give is the id itself: the original's unit type code <c>2</c> is the archer type
/// <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md — "archers counted ×3", type table
/// <c>Ar 3,500</c>]</strong>, <c>IC2.Data</c>'s <c>UnitCatalog</c> maps that code to the id
/// <c>"archers"</c>, and both shipped rulesets and <c>Armies/ArmyNaming</c>'s canonical type list use that
/// id. So the id is data the project already agrees on, and the <em>lookup</em> is
/// <c>[designed]</c>. A ruleset without it is refused with a typed rejection rather than crashing the
/// resolver, which is what <see cref="Strength.SiegeStrength.Attacker"/> would otherwise do.
/// </para>
/// </remarks>
public static class BattleCommandRuleset
{
    /// <summary>
    /// The unit type id the shipped rulesets give the original's type code <c>2</c>. See this class's
    /// remarks for why this is a constant and not a <see cref="Ruleset"/> field.
    /// </summary>
    public const string ArcherUnitTypeId = "archers";

    /// <summary>
    /// The archer unit type id, when <paramref name="ruleset"/> defines it.
    /// </summary>
    /// <param name="ruleset">The loaded ruleset.</param>
    /// <returns>
    /// <see cref="ArcherUnitTypeId"/> if the ruleset declares that unit type, otherwise
    /// <see langword="null"/>.
    /// </returns>
    public static string? ArcherUnitTypeIdIn(Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        return ruleset.UnitTypeById(ArcherUnitTypeId) is null ? null : ArcherUnitTypeId;
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
