using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Engine.Armies.Commands;

/// <summary>
/// <c>TUnitMap_ArmyToArmyTransfer</c> / <c>TArmyToArmy</c> / <c>TArmyToArmy_OK</c>. See
/// <see cref="ArmyTransferCommand"/>'s remarks.
/// </summary>
[CommandHandler]
public sealed class ArmyTransferCommandHandler : ICommandHandler<ArmyTransferCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(ArmyTransferCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        if (string.Equals(command.FromArmyId, command.ToArmyId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(ArmyTransferRejections.SameArmy, "An army cannot transfer to itself.");
        }

        var state = context.State;
        var source = state.ArmyById(command.FromArmyId);
        if (source is null)
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.UnknownArmy, $"'{command.FromArmyId}' is not a known army.");
        }

        var target = state.ArmyById(command.ToArmyId);
        if (target is null)
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.UnknownArmy, $"'{command.ToArmyId}' is not a known army.");
        }

        if (!string.Equals(source.Nation, command.IssuingNationId, StringComparison.Ordinal)
            || !string.Equals(target.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.NotYourArmy, "Both armies must belong to the issuing nation.");
        }

        // The original's partner rule: Chebyshev distance exactly 1 (FUN_00449D64 through FUN_004492A0's
        // distance == 1). Exactly, so distance 0 and distance 2 are both refused -- not AreAdjacent, whose
        // <= 1 would accept a co-located sibling.
        if (ChebyshevDistance(source.X, source.Y, target.X, target.Y) != 1)
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.NotAdjacent,
                $"Armies '{source.Id}' and '{target.Id}' must be exactly one tile apart.");
        }

        if (command.SupplyTons < 0 || command.Money < 0
            || command.BackSupplyTons < 0 || command.BackMoney < 0)
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.InvalidAmount, "Cannot transfer a negative amount of any resource.");
        }

        if (command.UnitIndexes.Count == 0 && command.SupplyTons == 0 && command.Money == 0
            && command.BackUnitIndexes.Count == 0 && command.BackSupplyTons == 0 && command.BackMoney == 0)
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.InvalidAmount,
                "Must transfer at least one unit, some supply or some money in either direction.");
        }

        // Each side's unit indexes are its own, read before the order (the dialog clamps every click to
        // what the giver currently holds). Both directions are partitioned before anything is written.
        if (!TryPartition(source.Units, command.UnitIndexes,
                out var moving, out var remainingUnits, out var sourceRejection, out var sourceMessage))
        {
            return CommandOutcome.Reject(sourceRejection, $"{sourceMessage} (army '{source.Id}')");
        }

        if (!TryPartition(target.Units, command.BackUnitIndexes,
                out var returning, out var remainingTarget, out var targetRejection, out var targetMessage))
        {
            return CommandOutcome.Reject(targetRejection, $"{targetMessage} (army '{target.Id}')");
        }

        // Each side gives at most the supply and money it holds before the order.
        if (command.SupplyTons > source.SupplyTons)
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.InsufficientSupply,
                $"Army '{source.Id}' has only {source.SupplyTons} tons of supply.");
        }

        if (command.Money > source.Money)
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.InsufficientMoney,
                $"Army '{source.Id}' has only {source.Money} money.");
        }

        if (command.BackSupplyTons > target.SupplyTons)
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.InsufficientSupply,
                $"Army '{target.Id}' has only {target.SupplyTons} tons of supply.");
        }

        if (command.BackMoney > target.Money)
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.InsufficientMoney,
                $"Army '{target.Id}' has only {target.Money} money.");
        }

        // Both compositions after both directions, since each side now both gives and receives.
        var rules = context.Ruleset.ArmyManagement;
        var finalSourceUnits = ValueList.From(remainingUnits.Concat(returning));
        var finalTargetUnits = ValueList.From(remainingTarget.Concat(moving));
        if (finalSourceUnits.Count > rules.MaxUnitsPerArmy || finalTargetUnits.Count > rules.MaxUnitsPerArmy)
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.CombinedUnitsTooLarge,
                $"There would be more than {rules.MaxUnitsPerArmy} units in one of the armies.");
        }

        var finalSourceTroops = SumTroops(finalSourceUnits);
        var finalTargetTroops = SumTroops(finalTargetUnits);
        if (finalSourceTroops > rules.MaxTroopsPerArmy || finalTargetTroops > rules.MaxTroopsPerArmy)
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.CombinedTroopsTooLarge,
                $"There would be more than {rules.MaxTroopsPerArmy} troops in one of the armies.");
        }

        var sourceIsEmpty = finalSourceUnits.Count == 0;
        var targetIsEmpty = finalTargetUnits.Count == 0;

        // Both directions are committed at once, then TArmyToArmy_OK's steps run once, in its own order.
        var supplySource = source.SupplyTons - command.SupplyTons + command.BackSupplyTons;
        var supplyTarget = target.SupplyTons - command.BackSupplyTons + command.SupplyTons;
        var moneySource = source.Money - command.Money + command.BackMoney;
        var moneyTarget = target.Money - command.BackMoney + command.Money;

        // The purse cap is the dialog's money stepper, min(step, 1000 - money(receiver), money(giver)); it
        // bounds only money moved between two surviving armies (the merge below pools uncapped).
        if (!sourceIsEmpty && !targetIsEmpty)
        {
            var purseCap = context.Ruleset.Economy.PurseCapPerUnit;
            if (command.BackMoney > 0 && moneySource > purseCap)
            {
                return CommandOutcome.Reject(
                    ArmyTransferRejections.PurseCapExceeded,
                    $"Army '{source.Id}' can hold {purseCap} money, not {moneySource}.");
            }

            if (command.Money > 0 && moneyTarget > purseCap)
            {
                return CommandOutcome.Reject(
                    ArmyTransferRejections.PurseCapExceeded,
                    $"Army '{target.Id}' can hold {purseCap} money, not {moneyTarget}.");
            }
        }

        // Step 1: capA = troops(A) div 100; A's excess goes to B, A is left at capA.
        // Step 2: capB = troops(B) div 100, including step 1's push; B's excess goes back to A.
        var capacitySource = SupplyCapacity.ArmyCapacityTons(finalSourceTroops, context.Ruleset);
        if (supplySource > capacitySource)
        {
            var excess = supplySource - capacitySource;
            supplySource = capacitySource;
            supplyTarget += excess;
        }

        var capacityTarget = SupplyCapacity.ArmyCapacityTons(finalTargetTroops, context.Ruleset);
        if (supplyTarget > capacityTarget)
        {
            var excess = supplyTarget - capacityTarget;
            supplyTarget = capacityTarget;
            supplySource += excess;
        }

        // Steps 3/4: an army left with no units is merged into the other and disbanded, taking all of its
        // supply and money uncapped -- the original's FUN_0044ab90, as T106 already did.
        string? disbandedArmyId = null;
        if (sourceIsEmpty)
        {
            supplyTarget += supplySource;
            moneyTarget += moneySource;
            supplySource = 0;
            moneySource = 0;
            disbandedArmyId = source.Id;
        }
        else if (targetIsEmpty)
        {
            supplySource += supplyTarget;
            moneySource += moneyTarget;
            supplyTarget = 0;
            moneyTarget = 0;
            disbandedArmyId = target.Id;
        }

        var updatedArmies = BuildArmies(
            state, source, target, finalSourceUnits, finalTargetUnits,
            supplySource, supplyTarget, moneySource, moneyTarget, sourceIsEmpty, targetIsEmpty);

        return CommandOutcome.Accept(state with
        {
            Armies = updatedArmies,
            Fleets = ClearFleetBackReference(state, disbandedArmyId),
        });
    }

    /// <summary>
    /// Partitions <paramref name="giverUnits"/> into the listed ones, in the giver's own order, and the
    /// rest. An out-of-range index or one listed twice is a rejection; <paramref name="rejection"/> and
    /// <paramref name="message"/> are only meaningful when the method returns <see langword="false"/>.
    /// </summary>
    private static bool TryPartition(
        ValueList<UnitSlot> giverUnits,
        ValueList<int> indexes,
        out List<UnitSlot> moving,
        out List<UnitSlot> remaining,
        out RejectionCode rejection,
        out string message)
    {
        moving = new List<UnitSlot>();
        remaining = new List<UnitSlot>();
        var seen = new HashSet<int>();
        foreach (var index in indexes)
        {
            if (index < 0 || index >= giverUnits.Count)
            {
                rejection = ArmyTransferRejections.UnknownUnitIndex;
                message = $"There is no unit at index {index}";
                return false;
            }

            if (!seen.Add(index))
            {
                rejection = ArmyTransferRejections.DuplicateUnitIndex;
                message = $"Unit index {index} is listed more than once";
                return false;
            }
        }

        for (var i = 0; i < giverUnits.Count; i++)
        {
            if (seen.Contains(i))
            {
                moving.Add(giverUnits[i]);
            }
            else
            {
                remaining.Add(giverUnits[i]);
            }
        }

        rejection = default;
        message = string.Empty;
        return true;
    }

    private static ValueList<ArmyState> BuildArmies(
        GameState state,
        ArmyState source,
        ArmyState target,
        ValueList<UnitSlot> finalSourceUnits,
        ValueList<UnitSlot> finalTargetUnits,
        int supplySource,
        int supplyTarget,
        int moneySource,
        int moneyTarget,
        bool sourceIsEmpty,
        bool targetIsEmpty)
    {
        var updatedSource = source with
        {
            Units = finalSourceUnits,
            SupplyTons = supplySource,
            Money = moneySource,
        };
        var updatedTarget = target with
        {
            Units = finalTargetUnits,
            SupplyTons = supplyTarget,
            Money = moneyTarget,
        };

        if (sourceIsEmpty)
        {
            return ValueList.From(state.Armies
                .Where(a => !string.Equals(a.Id, source.Id, StringComparison.Ordinal))
                .Select(a => string.Equals(a.Id, target.Id, StringComparison.Ordinal) ? updatedTarget : a));
        }

        if (targetIsEmpty)
        {
            return ValueList.From(state.Armies
                .Where(a => !string.Equals(a.Id, target.Id, StringComparison.Ordinal))
                .Select(a => string.Equals(a.Id, source.Id, StringComparison.Ordinal) ? updatedSource : a));
        }

        return ValueList.From(state.Armies.Select(a =>
            string.Equals(a.Id, source.Id, StringComparison.Ordinal) ? updatedSource :
            string.Equals(a.Id, target.Id, StringComparison.Ordinal) ? updatedTarget : a));
    }

    /// <summary>
    /// Deletion sweep (build-process.md §4.2 gate 5): a disbanded army's fleet must drop its own
    /// <see cref="FleetState.CarriedArmyId"/> back-reference, keyed on the fleet's own claim, the same
    /// choice QuarterlyEconomySystem and EliminationForces make.
    /// </summary>
    private static ValueList<FleetState> ClearFleetBackReference(GameState state, string? disbandedArmyId)
    {
        if (disbandedArmyId is null)
        {
            return state.Fleets;
        }

        return ValueList.From(state.Fleets.Select(f =>
            string.Equals(f.CarriedArmyId, disbandedArmyId, StringComparison.Ordinal)
                ? f with { CarriedArmyId = null }
                : f));
    }

    private static int SumTroops(ValueList<UnitSlot> units)
    {
        var total = 0;
        foreach (var unit in units)
        {
            total += unit.Troops;
        }

        return total;
    }

    /// <summary>Chebyshev distance through the engine's single shared metric.</summary>
    private static int ChebyshevDistance(int fromX, int fromY, int toX, int toY) =>
        LandingTile.ChebyshevDistance(new GridPoint(fromX, fromY), new GridPoint(toX, toY));
}
