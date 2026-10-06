using System.Globalization;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Engine.Ai;

/// <summary>
/// The AI's automatic mercenary hire, the computer-nation half of
/// <c>docs/tasks/T76.md</c> Done-when 3: <c>FUN_0044E41C</c> runs for every army of the acting computer
/// nation at the start of its turn. For each army it loops the cities once and, inside a city within
/// Chebyshev distance <c>&lt; 5</c> (radius 4), first resupplies the army (<c>FUN_0044f6d8</c>) and then
/// hires <strong>every</strong> live offer on that city, if the nation is at war with someone, the army
/// holds more than 50 money, it is not at war with the city's owner, and it has a free unit slot
/// <strong>[confirmed: decompiled-mercenary-offer-list-and-position.md §3]</strong>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The resupply and the hire interleave, per army and per city.</strong> The original's city
/// loop is <c>for (c = 0; c &lt; 334; c++)</c> and each in-range iteration runs <c>FUN_0044f6d8</c>
/// before the hire, on the live army record. A hire at an earlier city therefore raises the troop count
/// that a later city's resupply sees, so an army that hires and then passes a supply city leaves with
/// <c>cap(army.TotalTroops)</c> tons, not <c>cap(pre-hire troops)</c>. This pass reproduces that by
/// delegating each single army-city transfer to <see cref="AiResupplyPass.Run"/> on a state sliced to
/// that one army and city: the resupply pass keeps its own radius, T60's no-op discard and the
/// own-city purse ordering, and this pass only decides the order. The fleet half of automatic resupply
/// is not part of <c>FUN_0044E41C</c>, so <see cref="AiTurn.Run"/> still runs it from
/// <see cref="AiResupplyPass"/>.
/// </para>
/// <para>
/// <strong>It charges nothing and enforces no other cap.</strong> The original has no cost check, no
/// money deduction, no 100,000-troop cap, no supply check and no fleet check on this path — the
/// asymmetry against the player's order is the original's own, and "fixing" it here would be a
/// different rule. Only the five gates above (distance, at war, money, owner relation, free slot) apply.
/// </para>
/// <para>
/// <strong>An embarked army hires like any other.</strong> Its position is its fleet's tile
/// (<c>EmbarkArmyCommandHandler</c> writes the fleet's <c>X</c>/<c>Y</c> onto the army; only
/// <c>CoveredTileCode</c> carries the <c>-1</c> sentinel), so it has a real tile to measure a city
/// against and <c>FUN_0044E41C</c> runs for every army of the acting nation
/// <strong>[confirmed: decompiled-mercenary-offer-list-and-position.md §3; 2026-10-05-split-army-aboard-a-fleet.md]</strong>.
/// Skipping it would be a gate the report does not state. The original applies no fleet-capacity check
/// here either, so a hire may push an embarked army past its fleet's capacity — the original's own
/// asymmetry, deliberately not "fixed". (Its own resupply is skipped by
/// <see cref="AiResupplyPass"/>'s embarked-army rule, bug #759.)
/// </para>
/// <para>
/// <strong>The money gate reads the entry purse.</strong> The original captures <c>money = army.money</c>
/// once at <c>FUN_0044E41C</c>'s entry, before the city loop's resupply spends it, and gates the hire on
/// that captured value. This pass captures that same value for each army before that army's own first
/// resupply, so a resupply it performs itself never moves the hire across the <c>more than 50</c>
/// boundary. The optional <c>moneyAtTurnStart</c> snapshot therefore only matters to a caller that has
/// itself resupplied the army before calling in; see
/// <see cref="Run(GameState, Ruleset, string, IReadOnlyDictionary{string, int})"/>.
/// </para>
/// <para>
/// <strong>Why a pass and not a command or a scored candidate.</strong> The hire is not one of the
/// greedy loop's decisions: it is unconditional within its gates and runs up front for every army, the
/// same shape as <see cref="AiResupplyPass"/>. A scored candidate could only place one hire per action,
/// and a command would have to carry the free/no-cap asymmetry through the legality seam every human
/// order goes through. This pass therefore edits the state directly, the one other documented exception
/// beside resupply, and <see cref="AiTurn.Run"/> wires it in as the single <c>FUN_0044E41C</c> step.
/// </para>
/// <para>
/// <strong>It draws no randomness.</strong> Every qualifying offer is hired in a fixed order (armies in
/// <see cref="GameState.Armies"/> order, then cities in <see cref="GameState.Cities"/> order, then the
/// city's pool slots in <see cref="MercenaryPoolSlot.SlotIndex"/> order — <c>PoolSlotsAt</c> orders them
/// by slot index since T76), so it adds zero draws to an AI turn and a fixed-seed replay is identical by
/// construction. <see cref="Result.Describe"/> names the counts for the per-seed log.
/// </para>
/// <para>
/// <strong>The 19-unit quirk is reproduced.</strong> The original computes the army's first free slot
/// index once per army (<c>FUN_0044a66c</c>) and requires <c>lastPlus1 &lt; 19</c>, then re-checks
/// <c>army.slot[19].troops == 0</c> inside the offer loop. The effect is that an army already holding 19
/// units hires <em>nothing</em>, while one holding 18 can fill both remaining slots to 20. This pass
/// reproduces that exactly rather than modelling a clean "one free slot" test.
/// </para>
/// </remarks>
public static class AiMercenaryHirePass
{
    /// <summary>What one pass did, for the per-seed log.</summary>
    /// <param name="State">The state after every resupply and hire.</param>
    /// <param name="OffersHired">How many pool slots were consumed.</param>
    /// <param name="ArmiesHired">How many armies received at least one unit.</param>
    /// <param name="ArmyTransfers">How many army-city resupply transfers the interleaved loop made.</param>
    /// <param name="TonsMoved">The net tons those transfers admitted; negative where an over-capacity army gave stock back.</param>
    /// <param name="TalentsPaid">Talents those transfers spent at foreign cities.</param>
    public sealed record Result(
        GameState State,
        int OffersHired,
        int ArmiesHired,
        int ArmyTransfers = 0,
        int TonsMoved = 0,
        int TalentsPaid = 0)
    {
        /// <summary>A one-line summary of the hires for the log.</summary>
        public string Describe() => string.Format(
            CultureInfo.InvariantCulture,
            "mercenary hire: {0} offer(s) hired across {1} army/armies",
            OffersHired,
            ArmiesHired);
    }

    /// <summary>Runs the whole <c>FUN_0044E41C</c> step for one nation.</summary>
    /// <param name="state">The state at the start of the nation's turn.</param>
    /// <param name="ruleset">Supplies both radii, the money threshold, the war code and the unit cap.</param>
    /// <param name="nationId">The nation whose armies resupply and hire.</param>
    /// <param name="moneyAtTurnStart">
    /// Each army's purse <em>before</em> the turn's resupply, keyed by army id, or <see langword="null"/>.
    /// The pass captures each army's entry purse itself, before that army's own first resupply, so this
    /// snapshot only matters to a caller that has already resupplied the army and needs the hire gated on
    /// the original's pre-resupply <c>money = army.money</c> value (a 51-talent purse taken to 49 by
    /// resupply must still hire). The AI turn passes it for that reason; it is redundant for a caller that
    /// calls <see cref="Run(GameState, Ruleset, string, IReadOnlyDictionary{string, int})"/> on the
    /// untouched turn-start state.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="ruleset"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="nationId"/> is null or whitespace.</exception>
    public static Result Run(
        GameState state,
        Ruleset ruleset,
        string nationId,
        IReadOnlyDictionary<string, int>? moneyAtTurnStart = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentException.ThrowIfNullOrWhiteSpace(nationId);

        var radius = ruleset.Recruitment.MercenaryHireRangeAiSeat;
        var minMoney = ruleset.Recruitment.MercenaryAiHireMinMoney;
        var warCode = ruleset.Diplomacy.StateCodes.War;
        var unitCap = ruleset.ArmyManagement.MaxUnitsPerArmy;
        var resupplyRadius = ruleset.Economy.AutoResupplyRadiusTiles;

        var offersHired = 0;
        var armiesHired = 0;
        var armyTransfers = 0;
        var tons = 0;
        var talents = 0;

        // The army ids are collected up front because each hire rebuilds the armies list; walking the
        // live ValueList while replacing entries in it would visit stale copies.
        foreach (var armyId in IdsOfOwnArmies(state, nationId))
        {
            if (state.ArmyById(armyId) is not { } army)
            {
                continue;
            }

            // FUN_0044E41C captures all three of these once at entry, before its per-city loop:
            // money = army.money; atWar = FUN_00449cd8(me); lastPlus1 = FUN_0044a66c(army).
            var entryMoney = moneyAtTurnStart is not null
                && moneyAtTurnStart.TryGetValue(armyId, out var captured)
                    ? captured
                    : army.Money;
            var moneyGate = entryMoney > minMoney;
            var atWar = RelationTransitions.IsAtWarWithAnyone(state, ruleset, nationId);

            // lastPlus1 < 19: the first free slot is at most 18. See the class remarks.
            var freeSlot = army.Units.Count < unitCap - 1;

            var hiredByThisArmy = 0;
            var interestRadius = Math.Max(radius, resupplyRadius);

            foreach (var cityId in CityIds(state))
            {
                if (state.CityById(cityId) is not { } city)
                {
                    continue;
                }

                // A city out of reach of both steps is not visited at all; both passes re-check their own
                // radius, so this is only an allocation guard. City order within reach is preserved.
                if (Chebyshev(army, city) > interestRadius)
                {
                    continue;
                }

                // The original's city iteration resupplies before it hires. The live army record carries
                // the hire, so every later city resupplies the larger army.
                state = ResupplyArmyAtCity(state, ruleset, nationId, armyId, city,
                    ref armyTransfers, ref tons, ref talents);

                if (!moneyGate || !atWar || !freeSlot || !InRadius(army, city, radius)
                    || IsAtWar(state, nationId, city.Owner, warCode))
                {
                    continue;
                }

                state = HireOffersAt(state, armyId, city, unitCap, ref offersHired, ref hiredByThisArmy);
            }

            if (hiredByThisArmy > 0)
            {
                armiesHired++;
            }
        }

        return new Result(state, offersHired, armiesHired, armyTransfers, tons, talents);
    }

    /// <summary>
    /// Runs the one army-city automatic resupply the original performs inside its city iteration, by
    /// handing <see cref="AiResupplyPass.Run"/> a state sliced to that single army and city.
    /// </summary>
    /// <remarks>
    /// Slicing rather than calling <see cref="IC2.Engine.Economy.AutomaticResupply.ForArmy"/> directly
    /// keeps a single implementation of the transfer: the radius, the non-hostile test, T60's
    /// no-op discard and the own-city purse ordering all stay in <see cref="AiResupplyPass"/>, and this
    /// pass contributes only the interleaving <c>FUN_0044E41C</c> requires. The slice leaves the full
    /// nation list in place so the purse hygiene still writes both nations.
    /// </remarks>
    private static GameState ResupplyArmyAtCity(
        GameState state,
        Ruleset ruleset,
        string nationId,
        string armyId,
        CityState city,
        ref int armyTransfers,
        ref int tons,
        ref int talents)
    {
        if (state.ArmyById(armyId) is not { } army)
        {
            return state;
        }

        var sliced = state with
        {
            Armies = ValueList.Of(army),
            Fleets = ValueList<FleetState>.Empty,
            Cities = ValueList.Of(city),
        };

        var step = AiResupplyPass.Run(sliced, ruleset, nationId);
        if (step.ArmyTransfers == 0)
        {
            return state;
        }

        armyTransfers += step.ArmyTransfers;
        tons += step.TonsMoved;
        talents += step.TalentsPaid;

        return state with
        {
            Armies = ReplaceById(state.Armies, step.State.ArmyById(armyId)!, a => a.Id),
            Cities = ReplaceById(state.Cities, step.State.CityById(city.Id)!, c => c.Id),
            Nations = step.State.Nations,
        };
    }

    /// <summary>
    /// Appends every live offer on <paramref name="city"/> to the army, in pool-slot order and while the
    /// army has a free slot, exactly the record <c>TRecruitMercs_RecruitMercUnit</c> writes but with no
    /// cost and no cap check beyond the free slot the caller already verified.
    /// </summary>
    private static GameState HireOffersAt(
        GameState state,
        string armyId,
        CityState city,
        int unitCap,
        ref int offersHired,
        ref int hiredByThisArmy)
    {
        foreach (var slot in PoolSlotsAt(state, city))
        {
            var current = state.ArmyById(armyId);
            if (current is null || current.Units.Count >= unitCap)
            {
                break;
            }

            state = Hire(state, current, slot);
            offersHired++;
            hiredByThisArmy++;
        }

        return state;
    }

    /// <summary>
    /// Appends the offer's unit to the army and consumes the pool slot — exactly the record
    /// <c>TRecruitMercs_RecruitMercUnit</c> writes, but with no cost and no cap check beyond the free
    /// slot the caller already verified.
    /// </summary>
    private static GameState Hire(GameState state, ArmyState army, MercenaryPoolSlot slot)
    {
        var hiredUnit = new UnitSlot(
            MercenaryLabel: slot.NameLabel,
            UnitTypeId: slot.UnitTypeId,
            Troops: slot.Troops,
            Quality: slot.Quality,
            Name: $"Mercenary unit (label {slot.NameLabel})");

        var updatedArmy = army with { Units = ValueList.From(army.Units.Append(hiredUnit)) };

        var updatedArmies = new ArmyState[state.Armies.Count];
        for (var i = 0; i < state.Armies.Count; i++)
        {
            updatedArmies[i] = string.Equals(state.Armies[i].Id, army.Id, StringComparison.Ordinal)
                ? updatedArmy
                : state.Armies[i];
        }

        var remaining = new MercenaryPoolSlot[state.MercenaryPool.Count - 1];
        var w = 0;
        foreach (var candidate in state.MercenaryPool)
        {
            if (candidate.SlotIndex != slot.SlotIndex)
            {
                remaining[w++] = candidate;
            }
        }

        return state with
        {
            Armies = ValueList.From(updatedArmies),
            MercenaryPool = ValueList.From(remaining),
        };
    }

    private static int Chebyshev(ArmyState army, CityState city) =>
        LandingTile.ChebyshevDistance(new GridPoint(army.X, army.Y), new GridPoint(city.X, city.Y));

    private static bool InRadius(ArmyState army, CityState city, int radius) =>
        LandingTile.ChebyshevDistance(
            new GridPoint(army.X, army.Y), new GridPoint(city.X, city.Y)) <= radius;

    private static bool IsAtWar(GameState state, string nationId, string otherId, int warCode)
    {
        if (string.Equals(nationId, otherId, StringComparison.Ordinal))
        {
            return false;
        }

        var relations = state.Relations;
        return relations.IndexOf(nationId) >= 0
               && relations.IndexOf(otherId) >= 0
               && relations.Get(nationId, otherId) == warCode;
    }

    /// <summary>
    /// A city's live offers in slot-index order, the order the original's <c>i = 201 … 250</c> sweep and
    /// the player's <c>HireMercenaryCommandHandler.FindChosenCity</c> both use. Ordering here means an
    /// unsorted pool cannot change which offers an army takes when it can fill only some of them.
    /// </summary>
    private static List<MercenaryPoolSlot> PoolSlotsAt(GameState state, CityState city)
    {
        var slots = new List<MercenaryPoolSlot>();
        foreach (var slot in state.MercenaryPool)
        {
            if (slot.X == city.X && slot.Y == city.Y)
            {
                slots.Add(slot);
            }
        }

        return slots.OrderBy(slot => slot.SlotIndex).ToList();
    }

    private static ValueList<T> ReplaceById<T>(ValueList<T> items, T replacement, Func<T, string> idOf)
    {
        var id = idOf(replacement);
        var copy = new T[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            copy[i] = string.Equals(idOf(items[i]), id, StringComparison.Ordinal) ? replacement : items[i];
        }

        return ValueList<T>.Of(copy);
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

    private static List<string> CityIds(GameState state)
    {
        var ids = new List<string>();
        foreach (var city in state.Cities)
        {
            ids.Add(city.Id);
        }

        return ids;
    }
}
