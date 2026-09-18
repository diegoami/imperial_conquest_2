using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// Quarterly billing, wired against real <see cref="GameState"/> — <c>docs/task-catalogue.md</c> "T39
/// Quarterly upkeep: who pays, mercenary desertion, and deposition for debt", Done-when 1 through 5.
/// Replaces T08's original billing outright (bug <see href="https://github.com/diegoami/imperial_conquest_2/issues/80">#80</see>):
/// the treasury pays regulars, recruitment slots and launched ships with no balance check; each army's
/// own purse pays its mercenaries; an unpaid mercenary unit deserts as a whole unit, and nothing else
/// does.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Runs first in the quarter</strong>, at this hook's default order 0 — before
/// <see cref="QuarterlyCityEconomySystem"/> (order 100, growth/rebuild/loyalty) and
/// <see cref="QuarterlyNationEconomySystem"/> (order 200, mobilization decay, the treasury credit and
/// the unity update) — exactly <c>upkeep-payment-and-desertion.md</c>'s step order: "ship upkeep, army
/// and garrison upkeep" is step 1, wealth and tax base are zeroed in step 2, and the city/nation loops
/// follow. Nothing that decides payment here reads the treasury or the tax base, so the ordering matters
/// only for the debt test — <see cref="AiDepositionHandler"/>'s, which runs after this quarter's income
/// has already landed.
/// </para>
/// <para>
/// <strong>Tax income is still not wired here</strong> (unchanged from T08's own note): <c>income =
/// nationTaxBase × taxRatePercent / 100</c> is <see cref="TaxIncome"/>, credited by
/// <see cref="QuarterlyNationEconomySystem"/>, not this billing step.
/// </para>
/// <para>
/// A fleet still under construction (<see cref="Model.FleetState.IsUnderConstruction"/>) pays no ship
/// upkeep — the original charges only a fleet whose <c>+10</c> word already reads <c>-1</c> (launched).
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

        // 1a: ships, every launched fleet, no balance check.
        var shipUpkeepByNation = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var fleet in state.Fleets)
        {
            if (fleet.IsUnderConstruction)
            {
                continue;
            }

            var charge = ShipUpkeep.Compute(fleet.Ships, ruleset);
            shipUpkeepByNation[fleet.Nation] = shipUpkeepByNation.GetValueOrDefault(fleet.Nation) + charge;
        }

        // 1b: armies, in army-index order -- regulars to the treasury, mercenaries to the army's own
        // purse, with desertion on an empty purse.
        var regularUpkeepByNation = new Dictionary<string, int>(StringComparer.Ordinal);
        var updatedArmies = new List<ArmyState>(state.Armies.Count);
        foreach (var army in state.Armies)
        {
            var billed = MercenaryDesertion.BillArmy(army, ruleset);
            regularUpkeepByNation[army.Nation] =
                regularUpkeepByNation.GetValueOrDefault(army.Nation) + billed.RegularUpkeepCharged;

            if (billed.Army is { } survivingArmy)
            {
                updatedArmies.Add(survivingArmy);
            }
            // else: every unit deserted this quarter -- the army is deleted, dropped from the list.
        }

        // 1c: city units (recruitment slots), every nation, no balance check.
        var updatedNations = new List<NationState>(state.Nations.Count);
        foreach (var nation in state.Nations)
        {
            var shipCharge = shipUpkeepByNation.GetValueOrDefault(nation.Id);
            var armyCharge = regularUpkeepByNation.GetValueOrDefault(nation.Id);
            var garrisonCharge = GarrisonUpkeep.Compute(nation.RecruitmentSlots, ruleset);

            updatedNations.Add(nation with
            {
                Treasury = nation.Treasury - shipCharge - armyCharge - garrisonCharge,
            });
        }

        return state with
        {
            Nations = ValueList.From(updatedNations),
            Armies = ValueList.From(updatedArmies),
        };
    }
}
