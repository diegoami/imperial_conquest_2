using IC2.Engine.Model;

namespace IC2.Engine.Strength;

/// <summary>
/// The two sides of the original's siege math, decompiled from two <em>different</em> functions:
/// <see cref="Attacker"/> is <c>FUN_0044A930</c> (the besieging army's own strength, which triples
/// archers), and <see cref="Defender"/> is <c>FUN_0044A98C</c> (the besieged city's defensive strength,
/// built from loyalty, fortification and population, then two further scaling branches).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two computations, not one.</strong> Per
/// <c>decompiled-city-capture-resolution.md</c> ("Siege outcome: <c>FUN_0044b27c</c>":
/// <c>attackerStrength = FUN_0044a930(armyIdx)</c>, <c>defenderStrength = FUN_0044a98c(cityIdx)</c>),
/// these are genuinely two separate decompiled functions.
/// <see cref="Model.Ruleset.Siege"/> mirrors that same split — the top-level
/// <see cref="SiegeRules.ArcherStrengthMultiplier"/>/<see cref="SiegeRules.PowerDivisor"/> for the
/// attacker, the <c>Defender*</c>-prefixed fields for the defender — so this file mirrors that split
/// rather than forcing both formulas into one function that doesn't correspond to anything in the
/// decompilation.
/// </para>
/// <para>
/// <strong><see cref="Defender"/>'s complete shape (T33, correcting T31/T07).</strong> T07's first round
/// read <c>decompiled-city-capture-resolution.md</c>'s own prose (which never decompiled
/// <c>FUN_0044A98C</c> and only guessed at the formula) and shipped the loyalty and fortification weights
/// swapped, with the population field left as an explicit "unidentified, pass 0" parameter, and stopped
/// after the weighted sum. T31 (<c>docs/investigations/siege-defender-strength.md</c>) decompiled
/// <c>FUN_0044A98C</c> directly and corrected the three field identities. T33 completes the function:
/// <see cref="Defender"/> now applies, in the decompiled order, the weighted sum, then the
/// capital-and-loyalty <c>× <see cref="SiegeRules.HighLoyaltyBonusNumerator"/> /
/// <see cref="SiegeRules.HighLoyaltyBonusDenominator"/></c> branch, then the owner-vs-allegiance
/// <c>× <see cref="SiegeRules.DefenderNonAllegiantNumerator"/> /
/// <see cref="SiegeRules.DefenderNonAllegiantDenominator"/></c> branch — each division truncating before
/// the next step runs, exactly as <c>FUN_0044A98C</c>'s own sequential integer arithmetic does. The
/// function stays pure: "is the controller's capital" and "is the owner not the allegiance" are taken as
/// plain <see langword="bool"/> inputs, not derived by looking up world state here. The garrison-troops
/// addend (<see cref="SiegeRules.DefenderGarrisonTroopDivisor"/>) is still omitted: it needs per-nation
/// recruitment-slot state this pure function does not take as input (T17's DoD 7), not because any
/// identity is still open. <strong>Where T17 must add it: last, after both scaling branches</strong> —
/// the decompiled function's own listing (<c>docs/investigations/siege-defender-strength.md</c>) adds
/// the garrison term to the already-scaled strength, as the final step. Folding it into the weighted sum
/// instead would run the garrison contribution through both the <c>×5/3</c> and <c>×4/5</c> branches,
/// which the original never does.
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
    /// <paramref name="archerUnitTypeId"/> is not a unit type <paramref name="ruleset"/> defines, or a
    /// unit in <paramref name="units"/> has a <see cref="UnitSlot.UnitTypeId"/> the ruleset does not
    /// define — the same failure mode <see cref="ArmyPower.Compute"/> has for an unrecognised unit type,
    /// rather than silently treating it as "not an archer" (T33, issue #49).
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
            if (ruleset.UnitTypeById(unit.UnitTypeId) is null)
            {
                throw new ArgumentException(
                    $"Unit type '{unit.UnitTypeId}' is not defined in ruleset '{ruleset.Id}'.", nameof(units));
            }

            var isArcher = string.Equals(unit.UnitTypeId, archerUnitTypeId, StringComparison.Ordinal);
            total += unit.Troops * (isArcher ? rules.ArcherStrengthMultiplier : 1);
        }

        return (total / rules.PowerDivisor) * morale;
    }

    /// <summary>
    /// The besieged city's own defensive strength (<c>FUN_0044A98C</c>), in the decompiled function's
    /// own order: the weighted sum
    /// <c>loyalty × <see cref="SiegeRules.DefenderLoyaltyWeight"/> + finishedFortificationPercent ×
    /// <see cref="SiegeRules.DefenderFortificationWeight"/> + populationThousands ×
    /// <see cref="SiegeRules.DefenderPopulationWeight"/></c>, then, only when
    /// <paramref name="isControllerCapital"/> and <paramref name="loyalty"/> exceeds
    /// <see cref="SiegeRules.HighLoyaltyThreshold"/>, <c>× <see cref="SiegeRules.HighLoyaltyBonusNumerator"/>
    /// / <see cref="SiegeRules.HighLoyaltyBonusDenominator"/></c>, then, only when
    /// <paramref name="ownerDiffersFromAllegiance"/>, <c>× <see cref="SiegeRules.DefenderNonAllegiantNumerator"/>
    /// / <see cref="SiegeRules.DefenderNonAllegiantDenominator"/></c> — each division truncating before the
    /// next step, exactly as the decompiled sequential arithmetic does; the two branches are not
    /// commutative under truncation, so the order matters (see the class remarks and
    /// <c>docs/investigations/siege-defender-strength.md</c>). The garrison-troops addend is omitted; see
    /// the class remarks for why.
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
    /// <param name="isControllerCapital">
    /// Whether this city is its controlling nation's capital (<c>FUN_0044B8D0</c>). Taken as a plain
    /// input so this function stays pure — the capital predicate itself belongs to whichever caller holds
    /// world/nation state, not to this formula.
    /// </param>
    /// <param name="ownerDiffersFromAllegiance">
    /// Whether the city's owner is not its allegiance (<c>owner != allegiance</c> in <c>FUN_0044A98C</c>).
    /// Taken as a plain input for the same reason as <paramref name="isControllerCapital"/>.
    /// </param>
    /// <param name="ruleset">Supplies <see cref="Ruleset.Siege"/>'s defender weights and both scaling branches.</param>
    public static int Defender(
        int fortificationCode,
        CityOrderRule fortificationOrder,
        int loyalty,
        int populationThousands,
        bool isControllerCapital,
        bool ownerDiffersFromAllegiance,
        Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(fortificationOrder);
        ArgumentNullException.ThrowIfNull(ruleset);

        var rules = ruleset.Siege;
        var finishedFortificationPercent = FortificationCode.FinishedPercent(fortificationCode, fortificationOrder);

        var strength = (loyalty * rules.DefenderLoyaltyWeight)
                      + (finishedFortificationPercent * rules.DefenderFortificationWeight)
                      + (populationThousands * rules.DefenderPopulationWeight);

        if (isControllerCapital && loyalty > rules.HighLoyaltyThreshold)
        {
            strength = (strength * rules.HighLoyaltyBonusNumerator) / rules.HighLoyaltyBonusDenominator;
        }

        if (ownerDiffersFromAllegiance)
        {
            strength = (strength * rules.DefenderNonAllegiantNumerator) / rules.DefenderNonAllegiantDenominator;
        }

        return strength;
    }
}
