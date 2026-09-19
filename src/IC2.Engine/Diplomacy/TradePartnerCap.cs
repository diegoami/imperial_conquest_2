using IC2.Engine.Model;

namespace IC2.Engine.Diplomacy;

/// <summary>
/// The 3-trade-partner cap (DoD 3) — <c>TPolitics_OK</c>'s own behaviour
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>: "if you open
/// trade with a nation that already has three partners, that nation drops its poorest existing partner"
/// — the one with the lowest <see cref="NationState.TaxBase"/> (T35's field; an earlier reading of the
/// report called this field "wealth", which
/// <c>nation-tax-base-and-city-economy-fields.md</c> corrects).
/// </summary>
/// <remarks>
/// The report's own wording names the <em>target</em> of a new proposal as the side that drops a
/// partner, but the cap itself — "you can only trade with 3 nations" — is stated as the issuer's own
/// limit too. Both sides of a pair can independently already be at their cap when a new trade is
/// proposed, so this is applied to whichever side (or both) would end up with more than
/// <see cref="DiplomacyRules.MaxTradePartners"/> partners, not only the proposal's target — a
/// symmetric reading of the same one mechanic, not a second one.
/// </remarks>
public static class TradePartnerCap
{
    /// <summary>
    /// The trade partners <paramref name="nationId"/> currently has, other than
    /// <paramref name="excluding"/> (the nation about to become a new partner, not yet counted).
    /// </summary>
    public static IReadOnlyList<string> CurrentPartners(GameState state, Ruleset ruleset, string nationId, string excluding)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var tradeCode = ruleset.Diplomacy.StateCodes.Trade;
        var partners = new List<string>();
        foreach (var other in state.Relations.NationIds)
        {
            if (string.Equals(other, nationId, StringComparison.Ordinal)
                || string.Equals(other, excluding, StringComparison.Ordinal))
            {
                continue;
            }

            if (state.Relations.Get(nationId, other) == tradeCode)
            {
                partners.Add(other);
            }
        }

        return partners;
    }

    /// <summary>
    /// If <paramref name="nationId"/> already has <see cref="DiplomacyRules.MaxTradePartners"/> trade
    /// partners (other than <paramref name="newPartner"/>), drops the one with the lowest
    /// <see cref="NationState.TaxBase"/> back to peace (DoD 2's broken-trade cooldown). A tie is broken by
    /// nation id, ordinally, so the result is deterministic regardless of enumeration order. A no-op when
    /// the nation is not yet at the cap.
    /// </summary>
    public static GameState MakeRoomForOneMorePartner(
        GameState state, Ruleset ruleset, string nationId, string newPartner)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var partners = CurrentPartners(state, ruleset, nationId, newPartner);
        if (partners.Count < ruleset.Diplomacy.MaxTradePartners)
        {
            return state;
        }

        string? weakest = null;
        var weakestTaxBase = int.MaxValue;
        foreach (var partnerId in partners)
        {
            var partner = state.NationById(partnerId);
            var taxBase = partner?.TaxBase ?? int.MaxValue;
            if (taxBase < weakestTaxBase
                || (taxBase == weakestTaxBase && string.CompareOrdinal(partnerId, weakest) < 0))
            {
                weakest = partnerId;
                weakestTaxBase = taxBase;
            }
        }

        return weakest is null ? state : RelationTransitions.BreakToPeace(state, ruleset, nationId, weakest);
    }
}
