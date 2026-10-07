using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Engine.Armies.Commands;

/// <summary><c>TUnitMap_JoinArmies</c>. See <see cref="JoinArmiesCommand"/>'s remarks.</summary>
[CommandHandler]
public sealed class JoinArmiesCommandHandler : ICommandHandler<JoinArmiesCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(JoinArmiesCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        if (string.Equals(command.SurvivingArmyId, command.AbsorbedArmyId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(JoinArmiesRejections.SameArmy, "An army cannot join itself.");
        }

        var state = context.State;
        var survivor = state.ArmyById(command.SurvivingArmyId);
        if (survivor is null)
        {
            return CommandOutcome.Reject(
                JoinArmiesRejections.UnknownArmy, $"'{command.SurvivingArmyId}' is not a known army.");
        }

        var absorbed = state.ArmyById(command.AbsorbedArmyId);
        if (absorbed is null)
        {
            return CommandOutcome.Reject(
                JoinArmiesRejections.UnknownArmy, $"'{command.AbsorbedArmyId}' is not a known army.");
        }

        if (!string.Equals(survivor.Nation, command.IssuingNationId, StringComparison.Ordinal)
            || !string.Equals(absorbed.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                JoinArmiesRejections.NotYourArmy, "Both armies must belong to the issuing nation.");
        }

        if (survivor.IsEmbarked || absorbed.IsEmbarked)
        {
            return CommandOutcome.Reject(
                JoinArmiesRejections.ArmyEmbarked, "An army on a fleet cannot be combined with another.");
        }

        // The original's partner rule: Chebyshev distance exactly 1, the same gate T106's
        // ArmyTransferCommandHandler applies (FUN_00449D64 through FUN_004492A0's distance == 1).
        // Exactly, so distance 0 and distance 2 are both refused.
        if (LandingTile.ChebyshevDistance(new GridPoint(survivor.X, survivor.Y), new GridPoint(absorbed.X, absorbed.Y)) != 1)
        {
            return CommandOutcome.Reject(
                JoinArmiesRejections.NotAdjacent,
                $"Armies '{survivor.Id}' and '{absorbed.Id}' must be exactly one tile apart.");
        }

        var rules = context.Ruleset.ArmyManagement;
        var combinedUnits = survivor.Units.Count + absorbed.Units.Count;
        if (combinedUnits > rules.MaxUnitsPerArmy)
        {
            return CommandOutcome.Reject(
                JoinArmiesRejections.CombinedUnitsTooLarge,
                $"There are more than {rules.MaxUnitsPerArmy} units in these armies combined.");
        }

        var combinedTroops = survivor.TotalTroops + absorbed.TotalTroops;
        if (combinedTroops > rules.MaxTroopsPerArmy)
        {
            return CommandOutcome.Reject(
                JoinArmiesRejections.CombinedTroopsTooLarge,
                $"There are more than {rules.MaxTroopsPerArmy} troops in these armies combined.");
        }

        // T72 (bug #315): the join's pooled purse is an UNCAPPED add. 2026-10-05-army-purse-writes-and-
        // the-1000-cap.md row 7 reads TUnitMap_JoinArmies (:46992-46993, 0x004472FC) as
        // `kept.purse += partner.purse` — a 16-bit add with no cap [derived], and 1,000 + 1,000 gave
        // 2,000 in play [Wine candidate: Q1_05_before_join.SAV → Q1_06_after_join.SAV]; the
        // decompiled-unit-map-orders-and-record-fields.md Join armies row ("supplies and money add",
        // its caps being 20 units and 100,000 troops, no money cap) and IP016.sav army 1 at 1,066
        // (bug #315) agree. No 1,000 clamp and no treasury diversion: the original's join never touches
        // a treasury. The add is the field's own: PurseAccounting.Credit enforces 0 … 32,767 and the
        // original's 16-bit WRAP above 32,767 (row 7 [derived: code, not played]) is not reproduced
        // [designed: the user's 2026-10-05 choice not to reproduce overflow glitches, PR #758's R2
        // resolution] — a sum above PurseAccounting.PurseFieldMax lands at 32,767 and the excess goes
        // NOWHERE, not to a treasury, because this path moves money between the two army records only.
        var joined = survivor with
        {
            Units = ValueList.From(survivor.Units.Concat(absorbed.Units)),
            SupplyTons = survivor.SupplyTons + absorbed.SupplyTons,
            Money = PurseAccounting.Credit(survivor.Money, absorbed.Money),
            Moves = 0,
        };

        var updatedArmies = state.Armies
            .Where(a => !string.Equals(a.Id, absorbed.Id, StringComparison.Ordinal))
            .Select(a => string.Equals(a.Id, survivor.Id, StringComparison.Ordinal) ? joined : a);

        return CommandOutcome.Accept(state with { Armies = ValueList.From(updatedArmies) });
    }
}
