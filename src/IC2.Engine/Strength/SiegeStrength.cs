using IC2.Engine.Model;

namespace IC2.Engine.Strength;

/// <summary>
/// The two sides of the original's siege math — <c>docs/build-orchestration-plan.md</c> "T07 Strength
/// functions" Done-when 4 ("siege defender strength triples archers, and applies the
/// fortification/loyalty term").
/// </summary>
/// <remarks>
/// <para>
/// <strong>Reconciling the task's own wording with the evidence.</strong> The two clauses in Done-when 4
/// belong to two <em>different</em> decompiled functions, not one:
/// <see cref="Attacker"/> is <c>FUN_0044A930</c> (the besieging army's own strength, which triples
/// archers), and <see cref="Defender"/> is <c>FUN_0044A98C</c> (the city's defensive strength, built
/// from fortification and loyalty), per
/// <c>decompiled-city-capture-resolution.md</c> ("Siege outcome: <c>FUN_0044b27c</c>":
/// <c>attackerStrength = FUN_0044a930(armyIdx)</c>, <c>defenderStrength = FUN_0044a98c(cityIdx)</c>).
/// <see cref="Model.Ruleset.Siege"/> (T02, already merged) already carries both sides as one
/// <see cref="SiegeRules"/> record — the top-level <see cref="SiegeRules.ArcherStrengthMultiplier"/> and
/// <see cref="SiegeRules.PowerDivisor"/> for the attacker, the <c>Defender*</c>-prefixed fields for the
/// defender — so this file mirrors that split rather than forcing both formulas into one function that
/// doesn't correspond to anything in the decompilation. Flagged in the T07 PR body as the evidence
/// clarification it is, not silently resolved.
/// </para>
/// <para>
/// <see cref="Defender"/> is deliberately partial. <c>docs/data/rulesets/toy-ruleset.json</c>'s own
/// <c>siege._provenance</c> (written by T02) says plainly that the third city field's identity, the
/// <c>×5/3</c> high-fortification bonus's unrecovered gating condition, the owner-versus-allegiance
/// penalty (present in two different, unreconciled forms across two reports), and the garrison-troops
/// addend are all for <strong>T17</strong> (capture/siege/defection) to resolve, not T07. This function
/// implements exactly the three-term weighted sum Done-when 4 asks for and nothing past it — no invented
/// gating condition, no invented field identity.
/// </para>
/// <para>
/// Neither function here touches army morale mutation or the per-unit tactical morale array — see
/// <see cref="ArmyPower"/>'s remarks for that hazard; it applies equally here since <see cref="Attacker"/>
/// also takes strategic morale as a plain input.
/// </para>
/// </remarks>
public static class SiegeStrength
{
    /// <summary>
    /// The besieging army's own strength (<c>FUN_0044A930</c>): identical in shape to
    /// <see cref="ArmyPower.Compute"/> but archers count <see cref="SiegeRules.ArcherStrengthMultiplier"/>×
    /// their troop count instead of being weighted by <see cref="UnitTypeRules.CombatPowerWeight"/>, and
    /// every other unit type counts its troops straight (weight 1), with **no** intermediate
    /// <c>/ 100</c> — the decompiled loop has none, unlike <see cref="ArmyPower.Compute"/>'s.
    /// </summary>
    /// <param name="units">The besieging army's unit slots.</param>
    /// <param name="morale">The besieging army's strategic morale, taken as given (see class remarks).</param>
    /// <param name="ruleset">Supplies <see cref="Ruleset.Siege"/>'s <c>ArcherStrengthMultiplier</c>/<c>PowerDivisor</c>.</param>
    /// <param name="archerUnitTypeId">
    /// The unit type id this ruleset uses for archers (type code <c>2</c> in the original,
    /// <c>"archers"</c> in the shipped rulesets). Taken as a parameter, not a literal, so a custom
    /// ruleset that renames its unit types is not silently mishandled.
    /// </param>
    public static int Attacker(IEnumerable<UnitSlot> units, int morale, Ruleset ruleset, string archerUnitTypeId)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentException.ThrowIfNullOrWhiteSpace(archerUnitTypeId);

        var rules = ruleset.Siege;
        var total = 0;

        foreach (var unit in units)
        {
            var isArcher = string.Equals(unit.UnitTypeId, archerUnitTypeId, StringComparison.Ordinal);
            total += unit.Troops * (isArcher ? rules.ArcherStrengthMultiplier : 1);
        }

        return (total / rules.PowerDivisor) * morale;
    }

    /// <summary>
    /// The besieged city's own defensive strength (<c>FUN_0044A98C</c>), the confirmed part of it:
    /// <c>fortification × weight + loyalty × weight + thirdField × weight</c>, weights from
    /// <see cref="Model.Ruleset.Siege"/>. See the class remarks for what is deliberately not implemented
    /// here (the high-fortification bonus, the owner/allegiance penalty, the garrison-troops addend) and
    /// why.
    /// </summary>
    /// <param name="fortification">The city's fortification value, as stored (T07 does not decode the
    /// dual in-progress encoding — see <see cref="Model.FortificationCode"/> if that is needed by the
    /// caller).</param>
    /// <param name="loyalty">The city's loyalty value.</param>
    /// <param name="unidentifiedFieldValue">
    /// The confirmed-weight, unconfirmed-identity third term
    /// (<see cref="SiegeRules.DefenderUnidentifiedFieldWeight"/>'s own doc comment). Pass 0 until T17
    /// resolves what this field is.
    /// </param>
    /// <param name="ruleset">Supplies <see cref="Ruleset.Siege"/>'s three defender weights.</param>
    public static int Defender(int fortification, int loyalty, int unidentifiedFieldValue, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);

        var rules = ruleset.Siege;

        return (fortification * rules.DefenderFortificationWeight)
             + (loyalty * rules.DefenderLoyaltyWeight)
             + (unidentifiedFieldValue * rules.DefenderUnidentifiedFieldWeight);
    }
}
