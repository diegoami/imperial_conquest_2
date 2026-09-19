using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Engine.Armies;

/// <summary>
/// Which army a mobilized recruit joins, and in which of its unit slots — the original's
/// <c>FUN_0044a120</c> and <c>FUN_0044a66c</c>. <c>docs/task-catalogue.md</c> "T55 Mobilization: a ready
/// recruit becomes an army unit", Done-when 2, 3 and 5.
/// </summary>
/// <remarks>
/// <para>
/// <strong>[confirmed: decompiled-mobilization-and-mercenary-restock.md §3]</strong>, whose decompilation
/// of <c>FUN_0044a120</c> is reproduced by <see cref="Find"/> statement for statement:
/// </para>
/// <code>
/// *armyOut = -1;
/// for (a = 0; a &lt; armyCount; a++)
///   if (army[a].owner == nation) {
///     d = Chebyshev(army[a].xy, cityTable[city].xy);
///     if (d == 1 || (nationIsComputer &amp;&amp; d &lt; 6)) *armyOut = a;   // no break: the LAST match wins
///   }
/// if (-1 &lt; *armyOut) {
///   *slotOut = firstFreeUnitSlot(*armyOut);
///   if (*slotOut != 20 &amp;&amp; totalTroops(*armyOut) + slot.troops &lt; 0x186a1) return;   // accepted
///   *armyOut = -1;                                        // army full -> caller creates a new one
/// }
/// </code>
/// <para>
/// <strong>Three things in that shape are each a rule on their own</strong>, and none of them is the
/// obvious choice:
/// </para>
/// <list type="number">
/// <item><description>
/// <strong>The tie-break is the last matching army index, not the nearest.</strong> The loop has no
/// <c>break</c>, so a later index overwrites an earlier one. The corpus mobilization does turn on
/// this: once the overflow army exists it is the highest index, so the remaining four units go to it
/// and not back to the army they overflowed from.
/// </description></item>
/// <item><description>
/// <strong>A full army is not skipped — it ends the search.</strong> The capacity test runs once,
/// against the army the adjacency loop already settled on. An <em>earlier</em> adjacent army with room
/// is never tried; the caller creates a new army instead.
/// <strong>The corpus pair does not discriminate this clause</strong> and no claim here rests on it:
/// Rome owns exactly one army in <c>autumn_1</c>, so when army 0 fills there is nothing to fall back
/// to and both readings create the new army. It is read straight off the decompilation above, and
/// <c>MobilizationReceivingArmyTests.A_full_last_match_ends_the_search_rather_than_falling_back_to_an_earlier_army</c>
/// pins it with a two-army fixture built for the purpose.
/// </description></item>
/// <item><description>
/// <strong>The two seats use differently-shaped predicates</strong>, not one predicate with two radii:
/// the player's test is <c>d == 1</c> (adjacent, and the report stresses it is not <c>d &lt;= 1</c>),
/// the AI's is <c>d &lt; 6</c>. <see cref="Accepts"/> keeps both shapes.
/// </description></item>
/// </list>
/// <para>
/// <strong>An embarked army is not a candidate</strong> — <c>[derived]</c>. <c>FUN_0044a120</c> itself
/// has no such test, because in the original an army aboard a fleet is marked by <c>covered == -1</c>
/// and the very next call in the mobilization sequence, <c>FUN_0044a80c</c>, guards its map write with
/// <c>if (-1 &lt; army.covered)</c> — the original knows the case at the end of the same sequence.
/// This engine models it as <see cref="ArmyState.AboardFleetId"/> and already refuses every other seam
/// that adds or removes an army's units while it is at sea
/// (<see cref="Commands.JoinArmiesCommandHandler"/>, <see cref="Commands.SplitArmyCommandHandler"/>,
/// <see cref="Commands.DisbandArmyCommandHandler"/> and
/// <see cref="Recruitment.Commands.HireMercenaryCommandHandler"/>), and
/// <see cref="Economy.HostileArmyAdjacent"/> states the convention outright: "an embarked army covers no
/// map cell". Landing a recruit on a ship would also put troops aboard a fleet without going through
/// <see cref="ArmyTransportTrim"/>'s capacity rule. <c>MobilizationReceivingArmyTests</c> visits this
/// edge with an embarked army parked one tile from the city.
/// </para>
/// </remarks>
public static class MobilizationReceivingArmy
{
    /// <summary>The army a mobilized unit joins, and the unit slot index it is written to.</summary>
    /// <param name="Army">The receiving army.</param>
    /// <param name="UnitSlotIndex">
    /// <see cref="FirstFreeUnitSlot"/>'s answer for <paramref name="Army"/> — one past its highest
    /// occupied slot.
    /// </param>
    public sealed record Choice(ArmyState Army, int UnitSlotIndex);

    /// <summary>
    /// One past the highest unit slot of <paramref name="army"/> that holds troops — <em>not</em> the
    /// first hole.
    /// </summary>
    /// <remarks>
    /// <strong>[confirmed: decompiled-mobilization-and-mercenary-restock.md §3]</strong>:
    /// "<c>FUN_0044a66c</c> returns <c>lastOccupiedSlot + 1</c>, not the first hole — it scans all 20
    /// slots and remembers the highest index with <c>troops &gt; 0</c>. Gaps are never reused.
    /// Returning <c>20</c> is the rejection." An empty army answers <c>0</c>.
    /// </remarks>
    /// <param name="army">The army to scan.</param>
    public static int FirstFreeUnitSlot(ArmyState army)
    {
        ArgumentNullException.ThrowIfNull(army);

        var lastOccupied = -1;
        for (var i = 0; i < army.Units.Count; i++)
        {
            if (army.Units[i].Troops > 0)
            {
                lastOccupied = i;
            }
        }

        return lastOccupied + 1;
    }

    /// <summary>
    /// Whether an army of <paramref name="control"/>'s nation standing at Chebyshev distance
    /// <paramref name="distance"/> from the training city is an adjacency candidate.
    /// </summary>
    /// <param name="distance">Chebyshev distance between the army and the city.</param>
    /// <param name="control">The mobilizing nation's seat.</param>
    /// <param name="rules">Supplies both seats' radii.</param>
    /// <param name="model">
    /// The ruleset's <see cref="RulesetFlags.SeatAsymmetry"/> setting. Under
    /// <see cref="SeatAsymmetryModel.Normalized"/> every seat gets the human predicate, so the AI keeps
    /// no reach the player lacks — see <see cref="Recruitment.MobilizationReadiness.MinStateCode"/>'s
    /// remarks for the same <c>[designed]</c> choice and what was searched for it.
    /// </param>
    public static bool Accepts(int distance, SeatControl control, RecruitmentRules rules, SeatAsymmetryModel model)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return model == SeatAsymmetryModel.Faithful && control == SeatControl.Ai
            ? distance <= rules.MobilizationReceivingArmyRangeAiSeat
            : distance == rules.MobilizationReceivingArmyRangeHumanSeat;
    }

    /// <summary>
    /// The army that will receive a <paramref name="incomingTroops"/>-strong unit mobilized at
    /// <paramref name="city"/>, or <see langword="null"/> when the caller must create one.
    /// </summary>
    /// <param name="state">The live state to scan.</param>
    /// <param name="city">The training city the recruit is collected at.</param>
    /// <param name="nation">The mobilizing nation.</param>
    /// <param name="ruleset">Supplies the radii, the unit cap and the troop cap.</param>
    /// <param name="incomingTroops">The mobilizing slot's troops, weighed against the troop cap.</param>
    public static Choice? Find(
        GameState state, CityState city, NationState nation, Ruleset ruleset, int incomingTroops)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(ruleset);

        var cityPoint = new GridPoint(city.X, city.Y);
        var model = ruleset.Flags.SeatAsymmetry;

        ArmyState? candidate = null;
        foreach (var army in state.Armies)
        {
            if (!string.Equals(army.Nation, nation.Id, StringComparison.Ordinal) || army.IsEmbarked)
            {
                continue;
            }

            var distance = LandingTile.ChebyshevDistance(new GridPoint(army.X, army.Y), cityPoint);
            if (Accepts(distance, nation.Control, ruleset.Recruitment, model))
            {
                // Deliberately no break: the original's loop runs to the end and the last match wins.
                candidate = army;
            }
        }

        if (candidate is null)
        {
            return null;
        }

        var slotIndex = FirstFreeUnitSlot(candidate);
        var management = ruleset.ArmyManagement;
        // ">=", where the original writes "!= 20": no engine seam can produce an army holding more
        // units than the cap (T15 refuses at join and at hire, and this path creates a second army
        // rather than overflowing), so the two agree everywhere reachable -- and where they could not,
        // ">=" fails closed. The troop test is the original's "total + incoming < 0x186a1", i.e. at
        // most MaxTroopsPerArmy, written the same way JoinArmiesCommandHandler writes it.
        if (slotIndex >= management.MaxUnitsPerArmy
            || candidate.TotalTroops + incomingTroops > management.MaxTroopsPerArmy)
        {
            // Full: the caller creates a new army rather than looking further back. T15 enforces the
            // same MaxUnitsPerArmy cap at join and at hire as a rejection; mobilization routes around
            // it exactly as the original does, and never past it.
            return null;
        }

        return new Choice(candidate, slotIndex);
    }
}
