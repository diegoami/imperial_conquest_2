using System.Globalization;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Model;

namespace IC2.Engine.Recruitment;

/// <summary>
/// T155 (#515, #904): the original's two "may this town recruit?" predicates, in the one home the
/// task's Done-when 3 asks for. The engine's refusal, the AI's candidate filter and the UI's Recruit
/// controls ask <see cref="MayTakeOrder"/>; the Recruit unit dialog's town list asks
/// <see cref="IsListedInDialog"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>They are genuinely two different predicates, and the difference is deliberate.</strong>
/// The order itself — <c>TArmyRecruits_RecruitUnit</c> (<c>0x454E78</c>) and the AI's
/// <c>FUN_004504F4</c> — compares the town's <strong>raw</strong>
/// <see cref="CityState.FortificationCode"/> word with the threshold − 1 (74), so a town with
/// <em>any</em> pending fortify order (the word is <c>points × radix + current</c>, ≥ 100) passes
/// whatever its current level, and the nation's capital always passes
/// <strong>[derived: code; #515's stage-2 comment item 1]</strong>. The dialog's town list
/// (<c>FUN_004544E0</c>) instead <em>decodes</em> the word and shows the capital, any town whose
/// current level is at least the threshold, or any town that already has units in training — that
/// last case is what lets units in training still be mobilized or disbanded from the list in a town
/// whose fortification has since fallen <strong>[derived: code; #515's first research comment;
/// <c>2026-09-29-which-cities-may-recruit-and-troop-amounts.md</c>; 118 of 118 training towns in six
/// saves satisfy it]</strong>. So a town listed only for its units in training shows up in the dialog
/// with Recruit disabled and Mobilize and Disband offered.
/// </para>
/// <para>
/// The threshold is ruleset data — <see cref="RecruitmentRules.RecruitTownMinFortificationPercent"/>,
/// <c>75</c> in every shipped ruleset. Nothing here hardcodes it.
/// </para>
/// </remarks>
public static class RecruitmentEligibility
{
    /// <summary>
    /// Whether <paramref name="city"/> may take a <em>new</em> recruitment order —
    /// <c>RecruitUnit</c>'s raw-word comparison: the city is its nation's capital, or its raw
    /// fortification word is at or above <see cref="RecruitmentRules.RecruitTownMinFortificationPercent"/>
    /// (the current level, or any pending fortify order). This is the predicate the engine's refusal,
    /// the AI's candidate filter and the UI's Recruit controls follow.
    /// </summary>
    /// <param name="city">The town the order would name.</param>
    /// <param name="nation">The ordering nation (the city's owner).</param>
    /// <param name="ruleset">The loaded ruleset, for the threshold.</param>
    public static bool MayTakeOrder(CityState city, NationState nation, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(ruleset);

        return IsCapital(city, nation)
               || city.FortificationCode >= ruleset.Recruitment.RecruitTownMinFortificationPercent;
    }

    /// <summary>
    /// Whether the Recruit unit dialog's town list shows <paramref name="city"/> —
    /// <c>FUN_004544E0</c>'s decoded-level comparison: the city is its nation's capital, or its
    /// <em>current</em> fortification level is at or above
    /// <see cref="RecruitmentRules.RecruitTownMinFortificationPercent"/>, or it already has units in
    /// training. Being listed is not being able to order: a town listed only for its units in
    /// training fails <see cref="MayTakeOrder"/>, and Recruit is then not offered there.
    /// </summary>
    /// <param name="city">The candidate town.</param>
    /// <param name="nation">The seat's nation.</param>
    /// <param name="ruleset">The loaded ruleset, for the threshold and the fortify order's encoding.</param>
    public static bool IsListedInDialog(CityState city, NationState nation, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(ruleset);

        return IsCapital(city, nation)
               || CurrentFortificationPercent(city, ruleset) >= ruleset.Recruitment.RecruitTownMinFortificationPercent
               || HasUnitsInTraining(city, nation);
    }

    /// <summary>
    /// The rule's reason, naming the town: what <see cref="MayTakeOrder"/> refused and why. The
    /// engine's rejection carries it as the refusal message, and the UI shows the same words where a
    /// Recruit control is absent or disabled.
    /// </summary>
    /// <param name="city">The town that was refused.</param>
    /// <param name="nation">The ordering nation.</param>
    /// <param name="ruleset">The loaded ruleset, for the threshold.</param>
    public static string RefusalReason(CityState city, NationState nation, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(ruleset);

        return FormattableString.Invariant(
            $"'{city.Name}' ({city.Id}) may not take a new recruitment order: only the nation's capital or a town at a fortification word of at least {ruleset.Recruitment.RecruitTownMinFortificationPercent} (the current level, or any pending fortify order) may recruit, and this town's word is {city.FortificationCode}.");
    }

    private static bool IsCapital(CityState city, NationState nation) =>
        nation.CapitalCityId is { } capitalId
        && string.Equals(city.Id, capitalId, StringComparison.Ordinal);

    private static bool HasUnitsInTraining(CityState city, NationState nation)
    {
        foreach (var slot in nation.RecruitmentSlots)
        {
            if (string.Equals(slot.TargetCityId, city.Id, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The town's <em>current</em> fortification level — <c>FUN_004544E0</c>'s decode of the raw
    /// word, through the engine's own <see cref="FortificationCode.FinishedPercent"/>. A ruleset with
    /// no fortify order can encode no pending order, so the raw word is already the finished level.
    /// </summary>
    private static int CurrentFortificationPercent(CityState city, Ruleset ruleset) =>
        FortifyRule(ruleset) is { } rule
            ? FortificationCode.FinishedPercent(city.FortificationCode, rule)
            : city.FortificationCode;

    /// <summary>
    /// The fortification order's own <see cref="CityOrderRule"/> — the encoding rule
    /// <see cref="FortificationCode"/> decodes with — or <see langword="null"/> when the ruleset
    /// declares no fortify order at all. Found through the engine's own
    /// <see cref="BattleCommandRuleset.FortificationOrderIdIn"/> seam (the order a siege attempt
    /// wipes), never a second copy of that criterion.
    /// </summary>
    private static CityOrderRule? FortifyRule(Ruleset ruleset)
    {
        if (BattleCommandRuleset.FortificationOrderIdIn(ruleset) is not { } orderId)
        {
            return null;
        }

        foreach (var order in ruleset.CityOrders.Orders)
        {
            if (string.Equals(order.Id, orderId, StringComparison.Ordinal))
            {
                return order;
            }
        }

        return null;
    }
}
