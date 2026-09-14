using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// Quarterly ship and army upkeep, wired against real <see cref="GameState"/> — the part of
/// <c>docs/build-orchestration-plan.md</c> "T08 Economy, supply, and purses" Done-when 2, 3 and 7 that
/// runs end to end through T06's quarterly boundary hook, not only as a pure formula.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Tax income is deliberately not wired here.</strong> <c>income = nationTaxBase × taxRatePercent
/// / 100</c> (<see cref="TaxIncome"/>, Done-when 1) needs a per-nation <c>nationTaxBase</c> field the
/// original's nation record carries (solved to 2,440 for Rome) and <see cref="Model.NationState"/> does
/// not model — it is a different field from <see cref="Model.NationState.Wealth"/> (the reparation
/// formula's <c>+0x44C</c>) and from the capture-only <c>taxBase</c> the fixtures corpus's
/// <c>capture.taxBaseMultiplier</c> entry names. Adding it is a <see cref="Model.NationState"/> change,
/// outside this task's <c>src/IC2.Engine/Economy/**</c> Owns list — <see cref="TaxIncome"/> stays a pure
/// formula, ready to wire in here the day that field exists, rather than this system reaching for a
/// stand-in (<see cref="Model.NationState.Wealth"/> or <see cref="Model.NationState.Population"/>) that
/// would silently misrepresent which field the original actually multiplies.
/// </para>
/// <para>
/// What <em>is</em> wired: ship upkeep (<see cref="ShipUpkeep"/>) over every launched fleet a nation owns,
/// army upkeep (<see cref="ArmyUpkeep"/>) over every unit of every army it owns, both summed and charged
/// against the nation's treasury every quarter — and, when the treasury cannot cover the total
/// (<see cref="UpkeepEnforcement.NationCanPayUpkeep"/>), the mutiny consequence
/// (<see cref="UpkeepEnforcement.ApplyMutinyToArmy"/>) applied to every one of that nation's armies, with
/// an <see cref="ArmyMutinied"/> event per army. Unity also decays by
/// <see cref="EconomyRules.UnityDecayPerQuarter"/>, floored at 0
/// <strong>[confirmed: decompiled-quarterly-billing-and-economy.md]</strong>.
/// </para>
/// <para>
/// A fleet still under construction (<see cref="Model.FleetState.IsUnderConstruction"/>) is not yet
/// "deployed" and pays no upkeep — the original's launch state sets money/supplies fresh on completion,
/// per <c>fleet.launchState</c> in the fixtures corpus; nothing charges a fleet before it exists on the map.
/// </para>
/// </remarks>
[QuarterBoundaryHandler("economy.quarterly-billing")]
public sealed class QuarterlyEconomySystem : IQuarterBoundaryHandler
{
    /// <inheritdoc/>
    public GameState OnQuarterBoundary(QuarterBoundaryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ruleset = context.Ruleset;
        var state = context.State;

        var nations = new List<NationState>(state.Nations.Count);
        var mutiniedArmyIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var nation in state.Nations)
        {
            var shipTotal = 0;
            foreach (var fleet in state.Fleets)
            {
                if (string.Equals(fleet.Nation, nation.Id, StringComparison.Ordinal) && !fleet.IsUnderConstruction)
                {
                    shipTotal += fleet.Ships;
                }
            }

            var armyUpkeepTotal = 0;
            var nationArmyIds = new List<string>();
            foreach (var army in state.Armies)
            {
                if (!string.Equals(army.Nation, nation.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                nationArmyIds.Add(army.Id);
                armyUpkeepTotal += ArmyUpkeep.Compute(army.Units, ruleset);
            }

            var shipUpkeepTotal = ShipUpkeep.Compute(shipTotal, ruleset);
            var totalUpkeep = shipUpkeepTotal + armyUpkeepTotal;
            var canPay = UpkeepEnforcement.NationCanPayUpkeep(nation.Treasury, totalUpkeep);

            var updatedNation = nation with
            {
                Treasury = nation.Treasury - totalUpkeep,
                Unity = Math.Max(0, nation.Unity - ruleset.Economy.UnityDecayPerQuarter),
            };
            nations.Add(updatedNation);

            if (!canPay)
            {
                foreach (var armyId in nationArmyIds)
                {
                    mutiniedArmyIds.Add(armyId);
                    context.Events.Publish(new ArmyMutinied(armyId, nation.Id));
                }
            }
        }

        if (mutiniedArmyIds.Count > 0)
        {
            var updatedArmies = state.Armies.Select(a =>
                mutiniedArmyIds.Contains(a.Id) ? UpkeepEnforcement.ApplyMutinyToArmy(a, ruleset) : a);
            state = state with { Armies = ValueList.From(updatedArmies) };
        }

        return state with { Nations = ValueList.From(nations) };
    }
}
