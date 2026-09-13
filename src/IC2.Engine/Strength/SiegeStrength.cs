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
/// from loyalty, fortification, and population), per
/// <c>decompiled-city-capture-resolution.md</c> ("Siege outcome: <c>FUN_0044b27c</c>":
/// <c>attackerStrength = FUN_0044a930(armyIdx)</c>, <c>defenderStrength = FUN_0044a98c(cityIdx)</c>).
/// <see cref="Model.Ruleset.Siege"/> mirrors that same split (T31 confirms it is genuinely two
/// computations, not one — see the T07 PR's round-1 review) — the top-level
/// <see cref="SiegeRules.ArcherStrengthMultiplier"/>/<see cref="SiegeRules.PowerDivisor"/> for the
/// attacker, the <c>Defender*</c>-prefixed fields for the defender — so this file mirrors that split
/// rather than forcing both formulas into one function that doesn't correspond to anything in the
/// decompilation.
/// </para>
/// <para>
/// <strong><see cref="Defender"/>'s three field identities (round-1 correction).</strong> T07's first
/// round read <c>decompiled-city-capture-resolution.md</c>'s own prose (which never decompiled
/// <c>FUN_0044A98C</c> and only guessed at the formula) and shipped the loyalty and fortification
/// weights swapped, with the third field left as an explicit "unidentified, pass 0" parameter. T31
/// (<c>docs/investigations/siege-defender-strength.md</c>) decompiled <c>FUN_0044A98C</c> directly,
/// cross-checked every field against <c>TInformation_ShowCityDetails</c>'s own panel labels, and
/// corrected <see cref="Model.Ruleset.Siege"/>'s field names and weight assignment accordingly. This
/// file now follows that correction: <see cref="Defender"/> takes loyalty, a real (decoded)
/// fortification percentage, and population — no parameter is "unidentified", and none defaults to a
/// value a caller is told to treat as neutral.
/// </para>
/// <para>
/// <see cref="Defender"/> is still deliberately partial in one respect: the high-fortification <c>×5/3</c>
/// bonus's capital-only gate (<c>FUN_0044B8D0</c>, now identified but filed as
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/46">issue #46</see> for its
/// misnamed ruleset fields) and the owner/allegiance <c>×4/5</c> penalty (
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/47">issue #47</see>, a shape
/// mismatch between the ruleset field and the actual <c>(x &lt;&lt; 2) / 5</c> operation) are both
/// real, confirmed parts of <c>FUN_0044A98C</c> but are filed as open bugs against
/// <see cref="Model.Ruleset.Siege"/> rather than fixed here — this task's Owns list is
/// <c>src/IC2.Engine/Strength/**</c>, not the ruleset model, and Done-when 4's text asks only for the
/// archer-tripling and fortification/loyalty terms. The garrison-troops addend
/// (<see cref="SiegeRules.DefenderGarrisonTroopDivisor"/>) is omitted for a different reason: it needs
/// per-nation recruitment-slot state this pure function does not take as input, not because any task
/// has been assigned to resolve an open identity.
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
    /// ruleset that renames its unit types is not silently mishandled — validated against
    /// <paramref name="ruleset"/> for the same reason <see cref="ArmyPower.Compute"/> validates every
    /// unit's own type id, rather than silently treating an unrecognised id as "not an archer".
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="archerUnitTypeId"/> is not a unit type <paramref name="ruleset"/> defines.
    /// </exception>
    public static int Attacker(IEnumerable<UnitSlot> units, int morale, Ruleset ruleset, string archerUnitTypeId)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentException.ThrowIfNullOrWhiteSpace(archerUnitTypeId);

        if (ruleset.UnitTypeById(archerUnitTypeId) is null)
        {
            throw new ArgumentException(
                $"Unit type '{archerUnitTypeId}' is not defined in ruleset '{ruleset.Id}'.", nameof(archerUnitTypeId));
        }

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
    /// The besieged city's own defensive strength (<c>FUN_0044A98C</c>):
    /// <c>loyalty × <see cref="SiegeRules.DefenderLoyaltyWeight"/> + finishedFortificationPercent ×
    /// <see cref="SiegeRules.DefenderFortificationWeight"/> + populationThousands ×
    /// <see cref="SiegeRules.DefenderPopulationWeight"/></c> — see the class remarks for what this
    /// deliberately omits and why.
    /// </summary>
    /// <param name="fortificationCode">
    /// The city's stored fortification word, exactly as <see cref="Model.CityState.FortificationCode"/>
    /// holds it — dual-encoded, not pre-decoded. This function decodes it itself via
    /// <see cref="FortificationCode.FinishedPercent"/> before weighting it, matching
    /// <c>FUN_0044A98C</c>'s own unconditional decode; passing an already-decoded percentage here would
    /// double-decode it incorrectly for a city with a fortification order pending.
    /// </param>
    /// <param name="fortificationOrder">
    /// The ruleset's <c>"fortify"</c> <see cref="CityOrderRule"/>, needed only for its
    /// <see cref="CityOrderRule.MaxPercent"/>/<see cref="CityOrderRule.InProgressEncodingRadix"/> — the
    /// decode's own parameters, not a second source of gameplay constants.
    /// </param>
    /// <param name="loyalty">The city's loyalty value.</param>
    /// <param name="populationThousands">
    /// The city's population, in thousands (<see cref="Model.CityState.PopulationThousands"/>) — the
    /// third term of the confirmed formula, per <c>docs/investigations/siege-defender-strength.md</c>.
    /// </param>
    /// <param name="ruleset">Supplies <see cref="Ruleset.Siege"/>'s three defender weights.</param>
    public static int Defender(
        int fortificationCode,
        CityOrderRule fortificationOrder,
        int loyalty,
        int populationThousands,
        Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(fortificationOrder);
        ArgumentNullException.ThrowIfNull(ruleset);

        var rules = ruleset.Siege;
        var finishedFortificationPercent = FortificationCode.FinishedPercent(fortificationCode, fortificationOrder);

        return (loyalty * rules.DefenderLoyaltyWeight)
             + (finishedFortificationPercent * rules.DefenderFortificationWeight)
             + (populationThousands * rules.DefenderPopulationWeight);
    }
}
