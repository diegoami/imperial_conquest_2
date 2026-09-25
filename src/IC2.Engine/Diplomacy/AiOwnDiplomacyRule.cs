using IC2.Engine.Model;

namespace IC2.Engine.Diplomacy;

/// <summary>
/// The pure, read-only half of <c>FUN_0044FB7C</c> — an AI seat's own per-turn diplomacy —
/// <c>decompiled-ai-offers-to-human-seats.md</c> §1a <strong>[confirmed]</strong>: the busy gate, the
/// power formula, the "protected" test, and the deterministic parts of the war-target, alliance-partner
/// and trade searches. Every random draw the report's own pseudocode makes (<c>Random(10)</c>,
/// <c>Random(20)</c>) is deliberately <strong>not</strong> here — see
/// <see cref="IC2.Engine.Ai.AiMilitaryPhase"/> and <see cref="IC2.Engine.Ai.AiDiplomacyPhase"/> for why those live at the
/// proposal call site instead.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this is split from the original's one monolithic call.</strong> The original runs the
/// whole busy/war/alliance/trade sequence as a single atomic pass with the two random draws at fixed
/// points inside it. This engine's AI is a greedy "propose every candidate, score them, take the best,
/// look again" loop (<c>docs/game-design.md</c> §AI; <see cref="IC2.Engine.Ai.AiTurn"/>), re-run after every single
/// action a seat takes — restructuring that loop into a once-per-turn atomic call is outside this task's
/// Owns list (<see cref="IC2.Engine.Ai.AiTurn"/> and <see cref="IC2.Engine.Ai.AiView"/> are not in it). So the war target, the
/// alliance partner and every eligible trade partner are each offered as their own candidate instead,
/// each written the moment it is chosen — still "direct, no consent step" — and the two chance rolls are
/// each decided once, idempotently, from a named stream keyed by the seat and the turn
/// (<see cref="IC2.Engine.Core.IRng.ForStream"/>: "calling it twice with the same name returns two generators that
/// produce the same sequence"), at the point a candidate is proposed, not when it is executed. That
/// keeps a re-evaluated Propose call from drawing the same chance twice in one turn, and lets a war
/// declaration that lands make the nation busy for a later-evaluated alliance candidate the same turn,
/// exactly as the original's own local <c>busy = true</c> does. The war candidate outscores every other
/// candidate this AI can offer (see <see cref="IC2.Engine.Ai.AiMilitaryPhase"/>'s own remarks) specifically so that,
/// within one turn, a declaration this routine is going to make happens before any trade action touches
/// the same target — the original's own loop order (loop 1's war/trade scan, then the declare, then
/// alliance, then loop 2's trade sweep) never trades with a nation and then declares war on it in the
/// same call, and the score ordering here preserves that.
/// </para>
/// <para>
/// <strong>Fixed at scenario start for "neighbour", live for everything else.</strong>
/// <see cref="NeighbourGeography"/> reads only <see cref="World.Cities"/> (never rewritten), but
/// <c>protected</c>, <c>wars</c>, <c>trades</c> and the power formula all read the live
/// <see cref="GameState"/> — cities won or lost, wealth and unity gained or spent, and relations formed
/// or broken all move them, exactly as the original's own per-turn re-evaluation would.
/// </para>
/// </remarks>
public static class AiOwnDiplomacyRule
{
    /// <summary>
    /// <c>busy(me) = atWar(me) or mobilization(me) &gt; <see cref="AiOwnDiplomacyRules.BusyMobilizationThreshold"/>
    /// or season == winter</c> — a busy AI declares no war and makes no alliance this turn (report §1a).
    /// The winter reading is <see cref="IC2.Engine.Naval.FleetTickSystem"/>'s own established one:
    /// <c>SeasonIndex == SeasonsPerYear - 1</c>.
    /// </summary>
    public static bool IsBusy(GameState state, Ruleset ruleset, string nationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var nation = state.NationById(nationId);
        if (nation is null)
        {
            return true;
        }

        if (RelationTransitions.IsAtWarWithAnyone(state, ruleset, nationId))
        {
            return true;
        }

        if (nation.MobilizedPercent > ruleset.Diplomacy.AiOwnDiplomacy.BusyMobilizationThreshold)
        {
            return true;
        }

        var isWinter = state.Calendar.SeasonIndex == ruleset.Calendar.SeasonsPerYear - 1;
        return isWinter;
    }

    /// <summary><c>P(n) = (wealth / <see cref="AiOwnDiplomacyRules.PowerWealthDivisor"/>) × (unity / <see cref="AiOwnDiplomacyRules.PowerUnityDivisor"/>)</c> (report §1a).</summary>
    public static long Power(NationState nation, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(ruleset);

        var rule = ruleset.Diplomacy.AiOwnDiplomacy;
        return (long)(nation.Wealth / rule.PowerWealthDivisor) * (nation.Unity / rule.PowerUnityDivisor);
    }

    /// <summary>
    /// <c>protected(n)</c>, relative to <paramref name="referenceNationId"/>: true if <paramref name="candidateId"/>
    /// has an ally <c>a</c> with <c>cities[candidate] + cities[a] &gt; cities[reference]</c> (report §1a).
    /// Used both for a war-target candidate (reference = the acting AI) and for an alliance-partner
    /// neighbour <c>j</c> (same reference).
    /// </summary>
    public static bool IsProtected(GameState state, Ruleset ruleset, string candidateId, string referenceNationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var allianceCode = ruleset.Diplomacy.StateCodes.Alliance;
        var referenceCities = state.CountCitiesOwnedBy(referenceNationId);
        var candidateCities = state.CountCitiesOwnedBy(candidateId);

        foreach (var allyId in state.Relations.NationIds)
        {
            if (string.Equals(allyId, candidateId, StringComparison.Ordinal))
            {
                continue;
            }

            if (state.Relations.Get(candidateId, allyId) != allianceCode)
            {
                continue;
            }

            var allyCities = state.CountCitiesOwnedBy(allyId);
            if (candidateCities + allyCities > referenceCities)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How many nations <paramref name="nationId"/> is currently at war with.</summary>
    public static int WarCount(GameState state, Ruleset ruleset, string nationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var warCode = ruleset.Diplomacy.StateCodes.War;
        var count = 0;
        foreach (var other in state.Relations.NationIds)
        {
            if (!string.Equals(other, nationId, StringComparison.Ordinal)
                && state.Relations.Get(nationId, other) == warCode)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>How many trade partners <paramref name="nationId"/> currently has.</summary>
    public static int TradeCount(GameState state, Ruleset ruleset, string nationId) =>
        TradePartnerCap.CurrentPartners(state, ruleset, nationId, excluding: nationId).Count;

    /// <summary>
    /// The best not-protected, neighbouring war-target candidate for <paramref name="meId"/> — report
    /// §1a's own loop, deterministic (the <c>Random(10)</c> roll is the caller's, not this method's):
    /// every other living nation <c>k</c> at peace, trade or alliance with <paramref name="meId"/>
    /// (<c>0 &lt;= rel[k][me] &lt; war</c>), not <see cref="IsProtected"/>, a
    /// <see cref="NeighbourGeography"/> neighbour of <paramref name="meId"/>, whose power ratio
    /// <c>WarTargetRatioMultiplier · P(me) / max(1, P(k))</c> exceeds the best seen so far (starting at
    /// <see cref="AiOwnDiplomacyRules.WarTargetRatioBase"/>). <see langword="null"/> if
    /// <see cref="IsBusy"/> or no candidate clears the bar.
    /// </summary>
    public static string? BestWarTarget(GameState state, Ruleset ruleset, World world, string meId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(world);

        if (IsBusy(state, ruleset, meId))
        {
            return null;
        }

        var me = state.NationById(meId);
        if (me is null)
        {
            return null;
        }

        var rule = ruleset.Diplomacy.AiOwnDiplomacy;
        var warCode = ruleset.Diplomacy.StateCodes.War;
        var myPower = Power(me, ruleset);

        string? best = null;
        long bestRatio = rule.WarTargetRatioBase;

        foreach (var candidate in state.Nations)
        {
            if (string.Equals(candidate.Id, meId, StringComparison.Ordinal) || candidate.Eliminated)
            {
                continue;
            }

            var relation = state.Relations.Get(candidate.Id, meId);
            if (relation < ruleset.Diplomacy.StateCodes.Peace || relation >= warCode)
            {
                // Already at war (nothing to declare) or on a cooldown (not a legal target either way).
                continue;
            }

            if (IsProtected(state, ruleset, candidate.Id, meId))
            {
                continue;
            }

            if (!NeighbourGeography.AreNeighbours(world, meId, candidate.Id))
            {
                continue;
            }

            var theirPower = Power(candidate, ruleset);
            var ratio = (rule.WarTargetRatioMultiplier * myPower) / Math.Max(1, theirPower);
            if (ratio > bestRatio)
            {
                bestRatio = ratio;
                best = candidate.Id;
            }
        }

        return best;
    }

    /// <summary>
    /// The first eligible alliance partner for <paramref name="meId"/> — report §1a's own nested search
    /// (deterministic; the <c>Random(20)</c> roll and the <see cref="IsBusy"/> gate are the caller's):
    /// the first neighbour <c>j</c> of <paramref name="meId"/> at peace or trade with it
    /// (<c>0 &lt;= rel[me][j] &lt;= trade</c>), alive, not <see cref="IsProtected"/> (relative to
    /// <paramref name="meId"/>), for which some AI nation <c>m</c> is at war with <c>j</c>, has fewer
    /// than <see cref="AiOwnDiplomacyRules.AllianceMaxPartnerWars"/> wars, is not <paramref name="meId"/>
    /// itself, and for which <c>cities[j] &lt; cities[me] + cities[m]</c> — first match only, both loops
    /// in <see cref="GameState.Nations"/>' own stable order.
    /// </summary>
    public static string? FindAlliancePartner(GameState state, Ruleset ruleset, World world, string meId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(world);

        var rule = ruleset.Diplomacy.AiOwnDiplomacy;
        var codes = ruleset.Diplomacy.StateCodes;
        var meCities = state.CountCitiesOwnedBy(meId);

        foreach (var j in state.Nations)
        {
            if (string.Equals(j.Id, meId, StringComparison.Ordinal) || j.Eliminated)
            {
                continue;
            }

            var relationToJ = state.Relations.Get(meId, j.Id);
            if (relationToJ < codes.Peace || relationToJ > codes.Trade)
            {
                continue;
            }

            if (!NeighbourGeography.AreNeighbours(world, meId, j.Id))
            {
                continue;
            }

            if (IsProtected(state, ruleset, j.Id, meId))
            {
                continue;
            }

            var jCities = state.CountCitiesOwnedBy(j.Id);

            foreach (var m in state.Nations)
            {
                if (m.Control != SeatControl.Ai
                    || string.Equals(m.Id, meId, StringComparison.Ordinal)
                    || m.Eliminated)
                {
                    continue;
                }

                if (state.Relations.Get(m.Id, j.Id) != codes.War)
                {
                    continue;
                }

                if (WarCount(state, ruleset, m.Id) >= rule.AllianceMaxPartnerWars)
                {
                    continue;
                }

                var mCities = state.CountCitiesOwnedBy(m.Id);
                if (jCities < meCities + mCities)
                {
                    return m.Id;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Every nation currently eligible to trade directly with <paramref name="meId"/> (report §1a's own
    /// two trade sweeps, which this collapses into one deterministic scan since nothing here
    /// distinguishes "was a war-target candidate this pass" the way the original's loop order does — see
    /// this type's own remarks): at peace with <paramref name="meId"/>, alive, AI-controlled, and both
    /// sides under <see cref="DiplomacyRules.MaxTradePartners"/>, in <see cref="GameState.Nations"/>'
    /// own order. <see cref="TradeCount"/> for <paramref name="meId"/> is read once; a caller offering
    /// more than one of these as separate candidates in the same proposal pass should re-check the cap
    /// after each one lands, since accepting one changes it for the next.
    /// </summary>
    public static IReadOnlyList<string> EligibleTradePartners(GameState state, Ruleset ruleset, string meId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var codes = ruleset.Diplomacy.StateCodes;
        var cap = ruleset.Diplomacy.MaxTradePartners;
        var myTrades = TradeCount(state, ruleset, meId);
        if (myTrades >= cap)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var candidate in state.Nations)
        {
            if (string.Equals(candidate.Id, meId, StringComparison.Ordinal)
                || candidate.Eliminated
                || candidate.Control != SeatControl.Ai)
            {
                continue;
            }

            if (state.Relations.Get(meId, candidate.Id) != codes.Peace)
            {
                continue;
            }

            if (TradeCount(state, ruleset, candidate.Id) >= cap)
            {
                continue;
            }

            result.Add(candidate.Id);
        }

        return result;
    }

    /// <summary>
    /// The first "swap a poorer partner for a richer one" opportunity (report §1a's closing loop): the
    /// first of <paramref name="meId"/>'s own current trade partners <c>s</c>, in
    /// <see cref="GameState.Nations"/> order, for which some AI nation <c>k</c> at peace with
    /// <paramref name="meId"/> and under <see cref="DiplomacyRules.MaxTradePartners"/> has a higher
    /// <see cref="NationState.TaxBase"/> than <c>s</c> — first match for <c>k</c>, per <c>s</c>, in the
    /// same stable order.
    /// </summary>
    public static (string PoorerPartner, string RicherCandidate)? FindTradeSwap(
        GameState state, Ruleset ruleset, string meId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var codes = ruleset.Diplomacy.StateCodes;
        var cap = ruleset.Diplomacy.MaxTradePartners;
        var currentPartners = TradePartnerCap.CurrentPartners(state, ruleset, meId, excluding: meId);

        foreach (var partnerId in currentPartners)
        {
            var partner = state.NationById(partnerId);
            if (partner is null)
            {
                continue;
            }

            foreach (var candidate in state.Nations)
            {
                if (string.Equals(candidate.Id, meId, StringComparison.Ordinal)
                    || string.Equals(candidate.Id, partnerId, StringComparison.Ordinal)
                    || candidate.Eliminated
                    || candidate.Control != SeatControl.Ai)
                {
                    continue;
                }

                if (state.Relations.Get(meId, candidate.Id) != codes.Peace)
                {
                    continue;
                }

                if (candidate.TaxBase <= partner.TaxBase)
                {
                    continue;
                }

                if (TradeCount(state, ruleset, candidate.Id) >= cap)
                {
                    continue;
                }

                return (partnerId, candidate.Id);
            }
        }

        return null;
    }
}
