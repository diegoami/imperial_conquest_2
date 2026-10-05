using System.Globalization;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Engine.Ai;

/// <summary>
/// The AI's automatic mercenary hire, the computer-nation half of
/// <c>docs/tasks/T76.md</c> Done-when 3: <c>FUN_0044E41C</c> runs for every army of the acting computer
/// nation at the start of its turn and hires <strong>every</strong> live offer on any city within
/// Chebyshev distance <c>&lt; 5</c> (radius 4), if the nation is at war with someone, the army holds
/// more than 50 money, it is not at war with the city's owner, and it has a free unit slot
/// <strong>[confirmed: decompiled-mercenary-offer-list-and-position.md §3]</strong>.
/// </summary>
/// <remarks>
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
/// asymmetry, deliberately not "fixed".
/// </para>
/// <para>
/// <strong>The money gate reads the entry purse.</strong> See
/// <see cref="Run(GameState, Ruleset, string, IReadOnlyDictionary{string, int})"/>: the caller passes
/// each army's purse from before the turn's resupply, because the original captures it once at
/// <c>FUN_0044E41C</c>'s entry, before resupply spends it.
/// </para>
/// <para>
/// <strong>Why a pass and not a command or a scored candidate.</strong> The hire is not one of the
/// greedy loop's decisions: it is unconditional within its gates and runs up front for every army, the
/// same shape as <see cref="AiResupplyPass"/>. A scored candidate could only place one hire per action,
/// and a command would have to carry the free/no-cap asymmetry through the legality seam every human
/// order goes through. This pass therefore edits the state directly, the one other documented exception
/// beside resupply, and it is wired into <see cref="AiTurn.Run"/> immediately after resupply — the
/// original runs both from the same <c>FUN_0044E41C</c> call.
/// </para>
/// <para>
/// <strong>It draws no randomness.</strong> Every qualifying offer is hired in a fixed order (armies in
/// <see cref="GameState.Armies"/> order, then cities in <see cref="GameState.Cities"/> order, then pool
/// slots in <see cref="GameState.MercenaryPool"/> order), so it adds zero draws to an AI turn and a
/// fixed-seed replay is identical by construction. <see cref="Result.Describe"/> names the counts for the
/// per-seed log.
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
    /// <param name="State">The state after every hire.</param>
    /// <param name="OffersHired">How many pool slots were consumed.</param>
    /// <param name="ArmiesHired">How many armies received at least one unit.</param>
    public sealed record Result(GameState State, int OffersHired, int ArmiesHired)
    {
        /// <summary>A one-line summary for the log.</summary>
        public string Describe() => string.Format(
            CultureInfo.InvariantCulture,
            "mercenary hire: {0} offer(s) hired across {1} army/armies",
            OffersHired,
            ArmiesHired);
    }

    /// <summary>Runs the whole pass for one nation.</summary>
    /// <param name="state">The state at the start of the nation's turn.</param>
    /// <param name="ruleset">Supplies the radius, the money threshold, the war code and the unit cap.</param>
    /// <param name="nationId">The nation whose armies hire.</param>
    /// <param name="moneyAtTurnStart">
    /// Each army's purse <em>before</em> the turn's resupply, keyed by army id, or <see langword="null"/>
    /// when the caller has not advanced the state since turn start. The original captures
    /// <c>money = army.money</c> once at <c>FUN_0044E41C</c>'s entry and gates the hire on that captured
    /// value, before the per-city loop in which resupply spends the purse; the AI turn therefore passes
    /// this snapshot so a foreign resupply that takes a 51-talent purse to 49 does not silently move the
    /// hire across its own <c>more than 50</c> boundary. A direct caller that passes nothing gates on the
    /// army's current purse, which is the same thing when no resupply has run.
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

        var offersHired = 0;
        var armiesHired = 0;

        // The army ids are collected up front because each hire rebuilds the armies list; walking the
        // live ValueList while replacing entries in it would visit stale copies.
        foreach (var armyId in IdsOfOwnArmies(state, nationId))
        {
            if (state.ArmyById(armyId) is not { } army)
            {
                continue;
            }

            // R3: the original reads the purse once at entry, before resupply. Gate on the captured
            // entry purse when the caller supplied it, otherwise on the live one.
            var entryMoney = moneyAtTurnStart is not null
                && moneyAtTurnStart.TryGetValue(armyId, out var captured)
                    ? captured
                    : army.Money;
            if (entryMoney <= minMoney)
            {
                continue;
            }

            if (!RelationTransitions.IsAtWarWithAnyone(state, ruleset, nationId))
            {
                continue;
            }

            // lastPlus1 < 19: first free slot at most 18. See the class remarks.
            if (army.Units.Count >= unitCap - 1)
            {
                continue;
            }

            var hiredByThisArmy = 0;
            foreach (var cityId in CityIds(state))
            {
                if (state.CityById(cityId) is not { } city)
                {
                    continue;
                }

                if (!InRadius(army, city, radius) || IsAtWar(state, nationId, city.Owner, warCode))
                {
                    continue;
                }

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
            }

            if (hiredByThisArmy > 0)
            {
                armiesHired++;
            }
        }

        return new Result(state, offersHired, armiesHired);
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

        return slots;
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
