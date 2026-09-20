using System.Globalization;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Engine.Ai;

/// <summary>
/// <c>docs/task-catalogue.md</c> T22 Done-when 5, the AI's per-turn resupply pass: every AI turn, each of
/// that nation's armies runs T38's automatic resupply against every non-hostile city within
/// <c>EconomyRules.AutoResupplyRadiusTiles</c>, and each of its fleets runs the fleet twin.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It re-implements no cap and no formula.</strong> Every ton, every talent and every purse
/// adjustment comes out of <see cref="AutomaticResupply.ForArmy"/> and
/// <see cref="AutomaticResupply.ForFleet"/>, which T38 merged and which are the transcription of
/// <c>FUN_0044F6D8</c> and <c>FUN_0044F7E4</c>. This file decides only <em>who</em> resupplies from
/// <em>whom</em>: it is the caller T38's own class remark says it deliberately did not write
/// ("<em>wiring the trigger… the AI's per-turn pass is T22's</em>").
/// </para>
/// <para>
/// <strong>The radius is data.</strong> <c>EconomyRules.AutoResupplyRadiusTiles</c>, added by this task
/// as its one granted model change, carries the 4 that <c>supply-capacity-rounding.md</c> states for the
/// army pass — "<em>called by the AI army pass (<c>FUN_0044F31C</c> → <c>FUN_0044E41C</c>) for every
/// non-hostile city within 4 tiles</em>" <strong>[confirmed]</strong>. Distance is measured with
/// <see cref="LandingTile.ChebyshevDistance"/>, the engine's shared metric, not a seventh inline copy
/// (issue #190 N7).
/// </para>
/// <para>
/// <strong>The fleet pass reuses the same radius, and that reuse is <c>[derived]</c>, not confirmed.</strong>
/// The report's table gives the radius for the army pass only; for the fleet pass
/// (<c>FUN_0044F608</c> → <c>FUN_0044E1FC</c> / <c>FUN_0044E5DC</c>) it states the function chain and the
/// cap and says nothing about a range at all. Searched <c>supply-capacity-rounding.md</c> and every other
/// report in the research repository for a fleet-side resupply radius and found none. Rather than invent
/// a second number, this pass uses the one the army pass confirms — the conservative choice, since a
/// fleet that ranged further than an army would be a behaviour nothing supports.
/// </para>
/// <para>
/// <strong>Order is fixed and stated.</strong> Armies in <see cref="GameState.Armies"/> order, then
/// fleets in <see cref="GameState.Fleets"/> order; for each, cities in <see cref="GameState.Cities"/>
/// order. Every transfer is threaded back into the state before the next one is computed, so a city that
/// runs out of stock supplying the first army has none left for the second — which is the behaviour a
/// shared stock has to have, and which makes the order load-bearing rather than cosmetic. These are the
/// model's own <see cref="ValueList{T}"/> orders, stable across a save/load round trip.
/// </para>
/// <para>
/// <strong>What is skipped, and why.</strong> An embarked army (<see cref="ArmyState.IsEmbarked"/>) is
/// off the map — its <see cref="ArmyState.CoveredTileCode"/> is the original's <c>-1</c> sentinel — so it
/// has no position to measure a city against; it is supplied by the fleet carrying it, through that
/// fleet's own pass. A fleet still under construction is likewise not on the map. A city whose owner does
/// not resolve to a nation is skipped rather than crashing the turn:
/// <see cref="AutomaticResupply.ForArmy"/> requires the owning <see cref="NationState"/> as an argument
/// and throws without it. Finally, a pairing whose computed transfer turns out to move no supply and
/// cost nothing is discarded rather than written back — see <see cref="MovesNothing"/>, which is T60's
/// correction and the reason the AI stopped beggaring itself on its own first turn.
/// </para>
/// </remarks>
public static class AiResupplyPass
{
    /// <summary>What one pass moved, for the per-seed log.</summary>
    /// <param name="State">The state after every transfer.</param>
    /// <param name="ArmyTransfers">How many army-city pairs were resupplied.</param>
    /// <param name="FleetTransfers">How many fleet-city pairs were resupplied.</param>
    /// <param name="TonsMoved">The net tons admitted across every transfer; negative where over-capacity units gave stock back.</param>
    /// <param name="TalentsPaid">Talents spent at foreign cities across every transfer.</param>
    public sealed record Result(
        GameState State, int ArmyTransfers, int FleetTransfers, int TonsMoved, int TalentsPaid)
    {
        /// <summary>A one-line summary for the log.</summary>
        public string Describe() => string.Format(
            CultureInfo.InvariantCulture,
            "resupply: {0} army transfers, {1} fleet transfers, {2} tons, {3} talents",
            ArmyTransfers, FleetTransfers, TonsMoved, TalentsPaid);
    }

    /// <summary>Runs the whole pass for one nation.</summary>
    /// <param name="state">The state at the start of the nation's turn.</param>
    /// <param name="ruleset">Supplies the radius and every constant the resupply functions read.</param>
    /// <param name="nationId">The nation whose units resupply.</param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="ruleset"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="nationId"/> is null or whitespace.</exception>
    public static Result Run(GameState state, Ruleset ruleset, string nationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentException.ThrowIfNullOrWhiteSpace(nationId);

        var radius = ruleset.Economy.AutoResupplyRadiusTiles;
        var warCode = ruleset.Diplomacy.StateCodes.War;

        var armyTransfers = 0;
        var fleetTransfers = 0;
        var tons = 0;
        var talents = 0;

        // The ids are collected up front because each transfer rebuilds the lists; walking the live
        // ValueList while replacing entries in it would visit stale copies.
        foreach (var armyId in IdsOfOwnArmies(state, nationId))
        {
            foreach (var cityId in CityIds(state))
            {
                if (state.ArmyById(armyId) is not { } army || army.IsEmbarked)
                {
                    break;
                }

                if (state.CityById(cityId) is not { } city)
                {
                    continue;
                }

                if (!InRange(army.X, army.Y, city, radius) || !IsNonHostile(state, warCode, nationId, city))
                {
                    continue;
                }

                if (state.NationById(army.Nation) is not { } armyNation
                    || state.NationById(city.Owner) is not { } cityNation)
                {
                    continue;
                }

                var result = AutomaticResupply.ForArmy(army, city, armyNation, cityNation, ruleset);
                if (MovesNothing(result.AdmittedTons, result.TalentsPaid))
                {
                    continue;
                }

                state = Apply(state, result);
                armyTransfers++;
                tons += result.AdmittedTons;
                talents += result.TalentsPaid;
            }
        }

        foreach (var fleetId in IdsOfOwnFleets(state, nationId))
        {
            foreach (var cityId in CityIds(state))
            {
                if (state.FleetById(fleetId) is not { } fleet || fleet.IsUnderConstruction)
                {
                    break;
                }

                if (state.CityById(cityId) is not { } city)
                {
                    continue;
                }

                if (!InRange(fleet.X, fleet.Y, city, radius) || !IsNonHostile(state, warCode, nationId, city))
                {
                    continue;
                }

                if (state.NationById(fleet.Nation) is not { } fleetNation
                    || state.NationById(city.Owner) is not { } cityNation)
                {
                    continue;
                }

                var result = AutomaticResupply.ForFleet(fleet, city, fleetNation, cityNation, ruleset);
                if (MovesNothing(result.AdmittedTons, result.TalentsPaid))
                {
                    continue;
                }

                state = Apply(state, result);
                fleetTransfers++;
                tons += result.AdmittedTons;
                talents += result.TalentsPaid;
            }
        }

        return new Result(state, armyTransfers, fleetTransfers, tons, talents);
    }

    /// <summary>
    /// Whether a computed transfer moved no supply in either direction and cost nothing — in which case
    /// the pairing was not a resupply at all and this pass declines it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why a no-op result is discarded rather than written back (T60, issue #259).</strong>
    /// <see cref="AutomaticResupply.ForArmy"/> always returns a state, including for an army that is
    /// already at its <see cref="SupplyCapacity.ArmyCapacityTons"/> and therefore admits no tons and pays
    /// no talents. That state is <em>not</em> inert: it still carries T38's purse hygiene, which moves a
    /// flat <see cref="EconomyRules.AutoResupplyPurseTopUpAmount"/> out of the nation's treasury into the
    /// unit's purse whenever the purse is under
    /// <see cref="EconomyRules.AutoResupplyPurseTopUpThreshold"/> and the treasury is positive. Applying
    /// it therefore spends the nation's money on a visit that delivered nothing.
    /// </para>
    /// <para>
    /// <strong>What that cost the AI, measured.</strong> On T22's fifty-seed soak both nations begin with
    /// a full army and a treasury (450 and 500 talents) smaller than one top-up grant (500). On its own
    /// first turn each seat paired its full army with a city, moved nothing, and handed its entire
    /// treasury to that army's purse — where it stayed, since an army spends its purse only when buying
    /// supply at a <em>foreign</em> city. From the next turn on
    /// <see cref="AiEconomyPhase.TurnBudget"/> was at or below zero, so
    /// <see cref="AiEconomyPhase.Propose"/> returned before proposing any recruitment at all, the army
    /// never grew, and <see cref="AiMilitaryPhase"/>'s siege ratio stayed between 20 and 106 permille
    /// against a required 1160 — <strong>the whole of why <c>chose [Military/besiege]</c> appeared zero
    /// times in 48,000 turns</strong>. Skipping the empty pairing lifts the same soak's best observed
    /// siege ratio to 658 permille and its recruitment count from 111 to 204.
    /// </para>
    /// <para>
    /// <strong>Why this is this file's decision and not a change to T38's rule.</strong> The class remark
    /// above states the division: <see cref="AutomaticResupply"/> owns every ton, talent and purse
    /// adjustment, and this pass "<em>decides only who resupplies from whom</em>". An army with nothing
    /// to receive resupplies from nobody. Nothing in <see cref="AutomaticResupply"/> changes, the purse
    /// hygiene still fires on every transfer that does move supply — which is how the army keeps the
    /// purse it needs to buy at foreign cities — and a unit <em>over</em> capacity still hands its
    /// surplus back, because that is a negative <c>AdmittedTons</c> and not a no-op.
    /// </para>
    /// </remarks>
    private static bool MovesNothing(int admittedTons, int talentsPaid) =>
        admittedTons == 0 && talentsPaid == 0;

    private static bool InRange(int x, int y, CityState city, int radius) =>
        LandingTile.ChebyshevDistance(new GridPoint(x, y), new GridPoint(city.X, city.Y)) <= radius;

    /// <summary>
    /// The same reading of "non-hostile" the merged supply gates use: an own city always, a foreign city
    /// unless its owner is at war with the unit's nation. Resolved through
    /// <see cref="DiplomaticRelations.IndexOf"/> first, because <see cref="DiplomaticRelations.Get"/>
    /// throws for a nation the matrix does not carry and this pass must never throw mid-turn.
    /// </summary>
    private static bool IsNonHostile(GameState state, int warCode, string nationId, CityState city)
    {
        if (string.Equals(city.Owner, nationId, StringComparison.Ordinal))
        {
            return true;
        }

        var relations = state.Relations;
        if (relations.IndexOf(nationId) < 0 || relations.IndexOf(city.Owner) < 0)
        {
            return false;
        }

        return relations.Get(nationId, city.Owner) != warCode;
    }

    private static List<string> IdsOfOwnArmies(GameState state, string nationId)
    {
        var ids = new List<string>();
        foreach (var army in state.Armies)
        {
            if (string.Equals(army.Nation, nationId, StringComparison.Ordinal))
            {
                ids.Add(army.Id);
            }
        }

        return ids;
    }

    private static List<string> IdsOfOwnFleets(GameState state, string nationId)
    {
        var ids = new List<string>();
        foreach (var fleet in state.Fleets)
        {
            if (string.Equals(fleet.Nation, nationId, StringComparison.Ordinal))
            {
                ids.Add(fleet.Id);
            }
        }

        return ids;
    }

    private static List<string> CityIds(GameState state)
    {
        var ids = new List<string>();
        foreach (var city in state.Cities)
        {
            ids.Add(city.Id);
        }

        return ids;
    }

    private static GameState Apply(GameState state, AutomaticResupply.ArmyResult result) =>
        state with
        {
            Armies = Replaced(state.Armies, result.Army, a => a.Id),
            Cities = Replaced(state.Cities, result.City, c => c.Id),
            Nations = ReplacedNations(state.Nations, result.ArmyNation, result.CityNation),
        };

    private static GameState Apply(GameState state, AutomaticResupply.FleetResult result) =>
        state with
        {
            Fleets = Replaced(state.Fleets, result.Fleet, f => f.Id),
            Cities = Replaced(state.Cities, result.City, c => c.Id),
            Nations = ReplacedNations(state.Nations, result.FleetNation, result.CityNation),
        };

    /// <summary>
    /// Writes both nations back, in that order, so that the own-city case — where the two
    /// <see cref="NationState"/> values are the <em>same</em> nation and the second carries the earlier
    /// one's changes — cannot lose the purse-hygiene treasury move.
    /// </summary>
    /// <remarks>
    /// <see cref="AutomaticResupply.ForArmy"/> returns <c>ArmyNation</c> and <c>CityNation</c> as two
    /// separate records. At an own city they describe one nation: the army's nation is the one the purse
    /// hygiene moved, and the city's nation is the untouched copy it started from. Writing the city's
    /// copy last would therefore silently revert the hygiene. Writing the army's copy last is safe in
    /// both directions because on the foreign path the two are genuinely different nations and the order
    /// between them does not matter.
    /// </remarks>
    private static ValueList<NationState> ReplacedNations(
        ValueList<NationState> nations, NationState unitNation, NationState cityNation)
    {
        var updated = Replaced(nations, cityNation, n => n.Id);
        return Replaced(updated, unitNation, n => n.Id);
    }

    private static ValueList<T> Replaced<T>(ValueList<T> items, T replacement, Func<T, string> idOf)
    {
        var replacementId = idOf(replacement);
        var copy = new T[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            copy[i] = string.Equals(idOf(items[i]), replacementId, StringComparison.Ordinal)
                ? replacement
                : items[i];
        }

        return ValueList<T>.Of(copy);
    }
}
