using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Naval.Commands;

/// <summary><c>TUnitMap_JoinFleets</c>.</summary>
/// <remarks>
/// <para>
/// <strong>The pooled purse adds UNCAPPED (T72, bug #315; supersedes T50 Done-when 4's capped reading,
/// which rested on T08's "every path that credits a purse" wording — see
/// <see cref="Economy.PurseAccounting"/>'s remarks for the three paths that really do cap).</strong>
/// The purse at 2026-10-05-army-purse-writes-and-the-1000-cap.md covers only the army record (its method
/// lists the army's <c>+0x0C</c> references), so the fleet join was not settled by it, and T72's
/// research check of Done-when 1 ran on <c>decompiled-unit-map-orders-and-record-fields.md</c>: its
/// Fleet orders row reads <c>TUnitMap_JoinFleets</c> (<c>0x00447A48</c>) — "ships, supplies and money
/// add; the survivor's moves are zeroed; the absorbed fleet is deleted", with the combined-ships bound
/// named (<c>&lt; 100</c>) and no money cap named, though the same report names the 1,000 clamp exactly
/// where it lives (its Supply section: "TAFSupply_ChangeMoney ... capped at 1,000 money per army or
/// fleet") <c>[derived: the decompiled row; no play evidence exists yet for the fleet join's purse — the
/// 2026-10-02-fleet-orders-live.md join run used fleets whose purses were 0]</c>. So a fleet join is
/// the naval twin of the army join's row 7 (TUnitMap_JoinArmies :46992-46993, 1,000 + 1,000 = 2,000
/// [Wine candidates Q1_05/Q1_06]): a plain add, no 1,000 clamp, and no treasury diversion — the
/// original's join touches no treasury. The field bound still holds: a sum above
/// <see cref="Economy.PurseAccounting.PurseFieldMax"/> lands at 32,767 and the excess goes NOWHERE, the
/// original's 16-bit wrap deliberately not reproduced [designed: the user's 2026-10-05 choice, PR #758's
/// R2 resolution].
/// </para>
/// </remarks>
[CommandHandler]
public sealed class JoinFleetsCommandHandler : ICommandHandler<JoinFleetsCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(JoinFleetsCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        if (string.Equals(command.SurvivingFleetId, command.AbsorbedFleetId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.SameFleet, "A fleet cannot join itself.");
        }

        var state = context.State;
        var survivor = state.FleetById(command.SurvivingFleetId);
        if (survivor is null)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.UnknownFleet, $"'{command.SurvivingFleetId}' is not a known fleet.");
        }

        var absorbed = state.FleetById(command.AbsorbedFleetId);
        if (absorbed is null)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.UnknownFleet, $"'{command.AbsorbedFleetId}' is not a known fleet.");
        }

        if (!string.Equals(survivor.Nation, command.IssuingNationId, StringComparison.Ordinal)
            || !string.Equals(absorbed.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.NotYourFleet, "Both fleets must belong to the issuing nation.");
        }

        if (survivor.IsUnderConstruction || absorbed.IsUnderConstruction)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.UnderConstruction, "Neither fleet may still be under construction.");
        }

        // The original's partner rule: Chebyshev distance exactly 1, the fleet twin (FUN_00449DD8) of
        // the army gate FUN_00449D64 reads through FUN_004492A0's distance == 1 [derived: code; audit
        // §1.6]. Exactly, so distance 0 and distance 2 are both refused.
        if (LandingTile.ChebyshevDistance(new GridPoint(survivor.X, survivor.Y), new GridPoint(absorbed.X, absorbed.Y)) != 1)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.NotAdjacent,
                $"Fleets '{survivor.Id}' and '{absorbed.Id}' must be exactly one tile apart.");
        }

        if (survivor.IsCarryingArmy || absorbed.IsCarryingArmy)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.CarryingArmy, "Neither fleet may carry an army to join.");
        }

        var rules = context.Ruleset.Naval;
        var combinedShips = survivor.Ships + absorbed.Ships;
        if (combinedShips > rules.JoinMaxShips)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.CombinedShipsTooLarge,
                $"There are more than {rules.JoinMaxShips} ships in these fleets combined.");
        }

        // T72 (bug #315): the join's pooled purse is an UNCAPPED add, the naval twin of
        // JoinArmiesCommandHandler's row 7 (see this type's remarks for the TUnitMap_JoinFleets reading
        // and the research check). No 1,000 clamp and no treasury diversion — the original's fleet join
        // moves money only between the two fleet records. PurseAccounting.Credit enforces only the
        // field's 0 … 32,767: a sum above it lands at 32,767 and the excess goes NOWHERE, the original's
        // 16-bit wrap deliberately not reproduced [designed: the user's 2026-10-05 choice, PR #758's R2
        // resolution].

        var joined = survivor with
        {
            Ships = combinedShips,
            SupplyTons = survivor.SupplyTons + absorbed.SupplyTons,
            Money = PurseAccounting.Credit(survivor.Money, absorbed.Money),
            Moves = 0,
        };

        var updatedFleets = state.Fleets
            .Where(f => !string.Equals(f.Id, absorbed.Id, StringComparison.Ordinal))
            .Select(f => string.Equals(f.Id, survivor.Id, StringComparison.Ordinal) ? joined : f);

        return CommandOutcome.Accept(state with { Fleets = ValueList.From(updatedFleets) });
    }
}
