using IC2.Engine.Model;
using IC2.Engine.Strength;

namespace IC2.Engine.Cities.Capture;

/// <summary>
/// The complete <c>FUN_0044A98C</c> defender strength: T33's <see cref="SiegeStrength.Defender"/> (the
/// weighted sum, then the capital-and-loyalty branch, then the owner-vs-allegiance branch), plus the
/// garrison-troops addend that function leaves as a pure-function omission for its caller to add
/// (<c>docs/task-catalogue.md</c> T17 DoD 7; see <see cref="SiegeStrength"/>'s own class remarks for why
/// it stops short).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Where this is used, and where it is not.</strong> This composes the value
/// <c>decompiled-city-capture-resolution.md</c>'s <c>FUN_0044a98c</c> pseudocode returns as a whole
/// (weighted sum → the two scaling branches → <c>+= garrison / 2</c>, in that exact order — folding the
/// garrison term into the weighted sum instead would run it through both scaling branches, which the
/// decompiled function never does). <see cref="CityCaptureResolver"/>'s cascading-defection sweep
/// (<c>FUN_0044ba1c</c>) calls this for every candidate city it evaluates — that function's own
/// <c>otherDefense = defender_strength(other_city)</c> is exactly this value, entirely within T17's Owns.
/// </para>
/// <para>
/// <strong>Where the initiating siege attempt's own garrison term lives instead, and why it is not this
/// type.</strong> The besieging army's own win/loss decision is
/// <see cref="IC2.Engine.Battle.InstantBattleResolver.ResolveSiege"/> (T16), which is already the tested
/// implementation of "the strength comparison at the head of <c>FUN_0044B27C</c>" and already applies the
/// separate <c>× 9/10</c> reduction (<see cref="SiegeRules.AttackerIsAllegianceDefenderReductionPercent"/>)
/// this task must not apply a second time. An earlier revision of this file reasoned that
/// <see cref="ResolveSiege"/> computing <see cref="SiegeStrength.Defender"/> without the garrison term was
/// an out-of-Owns gap this task could not close, and used this type only for the cascade's own candidate
/// evaluations — <strong>that reading did not survive review</strong> (<c>docs/task-catalogue.md</c> T17
/// DoD 7's amendment): the garrison addend is <c>FUN_0044A98C</c>'s own last line, so every caller of that
/// function needs it, the initiating siege included, or its own win/loss resolves against an incomplete
/// defender strength. The fix is a narrow, granted addition directly inside
/// <see cref="ResolveSiege"/> itself (a small, self-contained per-slot sum — the same shape as
/// <see cref="GarrisonTerm"/> below, kept as two call sites on purpose rather than a
/// <c>Battle → Cities.Capture</c> reference, since <see cref="CityCaptureResolver"/> already depends on
/// <c>Battle</c> for <see cref="IC2.Engine.Battle.BattleResult"/> and a reference back the other way would
/// be circular). <see cref="CityCaptureResolver.ResolveOutcome"/> still treats T16's own
/// <see cref="IC2.Engine.Battle.BattleResult.Winner"/> as the authoritative win/loss decision — now a
/// <em>complete</em> one — and this type remains reserved for the parts of the pipeline that are
/// genuinely T17's own: the cascade's separate strength comparisons for cities the initiating siege never
/// touched.
/// </para>
/// </remarks>
public static class CompleteDefenderStrength
{
    /// <summary>
    /// <see cref="SiegeStrength.Defender"/>'s result, plus <c>troops / <see cref="SiegeRules.DefenderGarrisonTroopDivisor"/></c>
    /// summed over every one of <paramref name="owner"/>'s <see cref="RecruitmentSlot"/>s that targets
    /// <paramref name="city"/>.
    /// </summary>
    /// <param name="city">The city whose defender strength is being computed.</param>
    /// <param name="fortificationOrder">The ruleset's <c>"fortify"</c> order, decoding the fortification word.</param>
    /// <param name="isControllerCapital">Whether <paramref name="city"/> is <paramref name="owner"/>'s capital.</param>
    /// <param name="ownerDiffersFromAllegiance">Whether <paramref name="city"/>'s owner is not its allegiance.</param>
    /// <param name="owner">
    /// The city's current owner — whose <see cref="NationState.RecruitmentSlots"/> the garrison term sums
    /// over. Taken separately from <paramref name="city"/> rather than looked up internally, so this stays
    /// a pure function of its own inputs, matching <see cref="SiegeStrength.Defender"/>'s own convention.
    /// </param>
    /// <param name="ruleset">Supplies <see cref="Ruleset.Siege"/>'s defender weights, both scaling
    /// branches, and <see cref="SiegeRules.DefenderGarrisonTroopDivisor"/>.</param>
    public static int Compute(
        CityState city,
        CityOrderRule fortificationOrder,
        bool isControllerCapital,
        bool ownerDiffersFromAllegiance,
        NationState owner,
        Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(ruleset);

        var baseStrength = SiegeStrength.Defender(
            city.FortificationCode,
            fortificationOrder,
            city.Loyalty,
            city.PopulationThousands,
            isControllerCapital,
            ownerDiffersFromAllegiance,
            ruleset);

        return baseStrength + GarrisonTerm(city.Id, owner, ruleset);
    }

    /// <summary>
    /// The garrison addend alone: <c>Σ (slot.troops / <see cref="SiegeRules.DefenderGarrisonTroopDivisor"/>)</c>
    /// over <paramref name="owner"/>'s recruitment slots targeting <paramref name="cityId"/> — each
    /// qualifying slot's own troops divided <em>before</em> the sum, exactly as the decompiled
    /// <c>+= troops / 2 for slots targeting this city</c> addend accumulates it one slot at a time.
    /// Dividing the total once instead would give a different (larger-or-equal) result under truncation
    /// whenever more than one slot targets the same city — e.g. two slots of 3 troops each:
    /// per-slot (correct) is <c>3/2 + 3/2 = 1 + 1 = 2</c>; summed-then-divided (wrong) is
    /// <c>(3+3)/2 = 3</c>.
    /// </summary>
    public static int GarrisonTerm(string cityId, NationState owner, Ruleset ruleset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cityId);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(ruleset);

        var divisor = ruleset.Siege.DefenderGarrisonTroopDivisor;
        var garrisonTerm = 0;
        foreach (var slot in owner.RecruitmentSlots)
        {
            if (string.Equals(slot.TargetCityId, cityId, StringComparison.Ordinal))
            {
                garrisonTerm += slot.Troops / divisor;
            }
        }

        return garrisonTerm;
    }
}
