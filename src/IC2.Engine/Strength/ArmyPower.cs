using IC2.Engine.Model;

namespace IC2.Engine.Strength;

/// <summary>
/// The original's field-battle army-strength formula — <c>docs/game-design.md</c> milestone 5 and
/// design milestone 8's combat section, <c>docs/build-orchestration-plan.md</c> "T07 Strength
/// functions".
/// </summary>
/// <remarks>
/// <para>
/// <c>FUN_0044A8CC</c> (<c>0x0044A8CC</c>), read directly from the local decompiled dump
/// (<c>%LOCALAPPDATA%\ReTools\all_app_functions.txt:49197-49217</c>) as well as summarised in
/// <c>decompiled-unit-map-orders-and-record-fields.md</c>:
/// </para>
/// <code>
/// int FUN_0044a8cc(army)
/// {
///     total = 0;
///     for each of the army's 20 unit slots:
///         total = total + (powerWeight[unit.type] * unit.troops) / 100;   // truncated PER UNIT
///     return (total / 80) * army.morale;                                  // truncated again, then × morale
/// }
/// </code>
/// <para>
/// The per-unit <c>/ 100</c> happens <em>inside</em> the accumulation loop, before the running total is
/// summed — not once on the grand total. The two are not equivalent under truncating integer division in
/// general, but the published 13-unit Roman roster does <strong>not</strong> exhibit the difference at
/// its final output: per-unit truncation sums to 40,506 before the <c>/ 80</c> step and sum-first
/// truncation to 40,507 (it contains two heavy-cavalry units whose <c>weight × troops</c> is not an
/// exact multiple of 100 — 774 and 1,539 troops), but both floor to the same <c>506</c> under <c>/ 80</c>,
/// so both orders give the same <c>armyPower</c> of 29,854 (T33, correcting a claim in an earlier
/// revision of this comment). The dedicated truncation test,
/// <c>ArmyPowerTests.Compute_PerUnitTruncation_DiffersFromSumFirstTruncation</c>, is what actually pins
/// the per-unit order — it is built to straddle the <c>/ 80</c> divisor exactly, so the two orders visibly
/// diverge in its final result, which the Roman roster's own numbers happen not to do.
/// </para>
/// <para>
/// <strong>Only the strategic army morale (army record <c>+14</c>, <see cref="Model.ArmyState.Morale"/>)
/// feeds this formula.</strong> The per-unit tactical morale array (<c>DAT_004A0350</c>, the
/// <c>±2</c>/<c>−3</c>-per-exchange mechanic) is a different field entirely and must never appear here —
/// <c>docs/design-audit.md</c> §2.9. This function takes morale as a plain input and neither mutates nor
/// clamps it: the rule that writes <see cref="Model.ArmyState.Morale"/> (supply-driven, 51…70) is T08's,
/// not this task's — <c>docs/investigations/thracia-supply-morale.md</c>.
/// </para>
/// </remarks>
public static class ArmyPower
{
    /// <summary>
    /// Computes an army's field-battle strength from its unit composition and its strategic morale.
    /// </summary>
    /// <param name="units">The army's unit slots. An empty army has power 0.</param>
    /// <param name="morale">
    /// The army's strategic morale (<see cref="Model.ArmyState.Morale"/>), taken as given. This
    /// function does not validate, clamp, or otherwise second-guess it — an out-of-range value reaching
    /// here is an upstream bug, not something this pure function defends against
    /// (<c>docs/build-orchestration-plan.md</c> "T07 Strength functions", Done-when 6).
    /// </param>
    /// <param name="ruleset">
    /// Supplies every constant this formula uses: <see cref="Ruleset.UnitTypes"/> for each unit type's
    /// <see cref="UnitTypeRules.CombatPowerWeight"/> (the <c>+0x26</c> table), and
    /// <see cref="CombatRules.PowerTroopDivisor"/> / <see cref="CombatRules.PowerDivisor"/> for the two
    /// truncating divisions. Nothing here is a C# literal.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="units"/> or <paramref name="ruleset"/> is null.</exception>
    /// <exception cref="ArgumentException">A unit's <see cref="UnitSlot.UnitTypeId"/> is not in <paramref name="ruleset"/>.</exception>
    public static int Compute(IEnumerable<UnitSlot> units, int morale, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(ruleset);

        var rules = ruleset.Combat;
        var total = 0;

        foreach (var unit in units)
        {
            var type = ruleset.UnitTypeById(unit.UnitTypeId)
                ?? throw new ArgumentException(
                    $"Unit type '{unit.UnitTypeId}' is not defined in ruleset '{ruleset.Id}'.", nameof(units));

            // Truncated here, per unit -- see the class remarks for why this order is load-bearing.
            total += (type.CombatPowerWeight * unit.Troops) / rules.PowerTroopDivisor;
        }

        return (total / rules.PowerDivisor) * morale;
    }
}
