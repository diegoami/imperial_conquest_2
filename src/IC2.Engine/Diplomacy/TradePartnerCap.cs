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
    /// <see cref="NationState.TaxBase"/> back to peace (DoD 2's broken-trade cooldown). A no-op when the
    /// nation is not yet at the cap.
    /// </summary>
    /// <remarks>
    /// Rework round 2, N-c: a tax-base tie is broken by nation order, matching <c>TPolitics_OK</c>'s own
    /// <c>k = 0..15</c> scan with a strict <c>&lt;</c> comparison against the running minimum -- the
    /// first nation in the scenario's own table order wins a tie, not whichever sorts first
    /// alphabetically by id. <see cref="CurrentPartners"/>'s own list is already in
    /// <see cref="GameState.Nations"/>' stable order (it filters <c>state.Relations.NationIds</c>, itself
    /// built from that same order), so the natural first-occurrence winner of a strict <c>&lt;</c> loop
    /// already <em>is</em> nation order -- the earlier version's own explicit ordinal re-comparison on an
    /// exact tie (<c>string.CompareOrdinal(partnerId, weakest) &lt; 0</c>) was what broke that, by
    /// overriding first-occurrence with alphabetical order whenever two partners' tax bases matched
    /// exactly. Removing that clause is the whole fix: on the classical world, "carthage" sorts before
    /// "rome" alphabetically while Rome is nation-table index 0, ahead of Carthage -- the ordinal
    /// re-comparison would have picked Carthage on a tie the original always resolves to Rome.
    /// </remarks>
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

            // Strict '<' only: the first partner encountered -- in CurrentPartners' own nation-table
            // order -- keeps the title on an exact tie, matching TPolitics_OK's own k = 0..15 scan.
            if (weakest is null || taxBase < weakestTaxBase)
            {
                weakest = partnerId;
                weakestTaxBase = taxBase;
            }
        }

        return weakest is null ? state : RelationTransitions.BreakToPeace(state, ruleset, nationId, weakest);
    }
}
