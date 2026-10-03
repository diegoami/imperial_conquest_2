using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Engine.Armies.Commands;

/// <summary><c>TUnitMap_ArmyToArmyTransfer</c> / <c>TArmyToArmy</c>. See <see cref="ArmyTransferCommand"/>'s remarks.</summary>
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

        if (command.SupplyTons < 0 || command.Money < 0)
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.InvalidAmount, "Cannot transfer a negative amount of any resource.");
        }

        if (command.UnitIndexes.Count == 0 && command.SupplyTons == 0 && command.Money == 0)
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.InvalidAmount, "Must transfer at least one unit, some supply or some money.");
        }

        var movingIndexes = new HashSet<int>();
        foreach (var index in command.UnitIndexes)
        {
            if (index < 0 || index >= source.Units.Count)
            {
                return CommandOutcome.Reject(
                    ArmyTransferRejections.UnknownUnitIndex,
                    $"Army '{source.Id}' has no unit at index {index}.");
            }

            if (!movingIndexes.Add(index))
            {
                return CommandOutcome.Reject(
                    ArmyTransferRejections.DuplicateUnitIndex,
                    $"Unit index {index} is listed more than once.");
            }
        }

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

        // Build both sides in the source list's own order, so the units keep their relative order and a
        // repeated run appends identically.
        var moving = new List<UnitSlot>();
        var remainingUnits = new List<UnitSlot>();
        for (var i = 0; i < source.Units.Count; i++)
        {
            if (movingIndexes.Contains(i))
            {
                moving.Add(source.Units[i]);
            }
            else
            {
                remainingUnits.Add(source.Units[i]);
            }
        }

        var movedTroops = 0;
        foreach (var unit in moving)
        {
            movedTroops += unit.Troops;
        }

        var rules = context.Ruleset.ArmyManagement;
        if (target.Units.Count + moving.Count > rules.MaxUnitsPerArmy)
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.CombinedUnitsTooLarge,
                $"There would be more than {rules.MaxUnitsPerArmy} units in the receiving army.");
        }

        if (target.TotalTroops + movedTroops > rules.MaxTroopsPerArmy)
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.CombinedTroopsTooLarge,
                $"There would be more than {rules.MaxTroopsPerArmy} troops in the receiving army.");
        }

        return remainingUnits.Count == 0
            ? AcceptEmptiedSource(context, state, source, target, moving)
            : AcceptPartialTransfer(command, context, state, source, target, moving, remainingUnits, movedTroops);
    }

    /// <summary>
    /// The source still holds units after the move, so both armies survive: enforce the receiving army's
    /// supply capacity and purse cap, then subtract exactly what moved.
    /// </summary>
    private static CommandOutcome AcceptPartialTransfer(
        ArmyTransferCommand command,
        CommandContext context,
        GameState state,
        ArmyState source,
        ArmyState target,
        List<UnitSlot> moving,
        List<UnitSlot> remainingUnits,
        int movedTroops)
    {
        // The receiving army's post-transfer troop count drives its supply capacity.
        var capacity = SupplyCapacity.ArmyDialogCapacityTons(target.TotalTroops + movedTroops, context.Ruleset);
        if (target.SupplyTons + command.SupplyTons > capacity)
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.SupplyExceedsCapacity,
                $"Army '{target.Id}' can hold {capacity} tons, not {target.SupplyTons + command.SupplyTons}.");
        }

        var purseCap = context.Ruleset.Economy.PurseCapPerUnit;
        if (target.Money + command.Money > purseCap)
        {
            return CommandOutcome.Reject(
                ArmyTransferRejections.PurseCapExceeded,
                $"Army '{target.Id}' can hold {purseCap} money, not {target.Money + command.Money}.");
        }

        var updatedTarget = target with
        {
            Units = ValueList.From(target.Units.Concat(moving)),
            SupplyTons = target.SupplyTons + command.SupplyTons,
            Money = target.Money + command.Money,
        };
        var updatedSource = source with
        {
            Units = ValueList.From(remainingUnits),
            SupplyTons = source.SupplyTons - command.SupplyTons,
            Money = source.Money - command.Money,
        };

        var updatedArmies = state.Armies.Select(a =>
            string.Equals(a.Id, source.Id, StringComparison.Ordinal) ? updatedSource :
            string.Equals(a.Id, target.Id, StringComparison.Ordinal) ? updatedTarget : a);

        return CommandOutcome.Accept(state with { Armies = ValueList.From(updatedArmies) });
    }

    /// <summary>
    /// The source holds no units after the move, so the original merges its full supply and money into the
    /// other army and disbands it (<c>FUN_0044ab90</c>) — the <c>OK</c> branch
    /// <c>army-to-army-transfer-confirmed.md</c> reads from code. Supply pools uncapped, exactly like
    /// <see cref="JoinArmiesCommandHandler"/>; the pooled purse is capped at
    /// <see cref="EconomyRules.PurseCapPerUnit"/>, with any excess credited to the issuing nation's
    /// treasury so the money is conserved, never destroyed.
    /// </summary>
    private static CommandOutcome AcceptEmptiedSource(
        CommandContext context,
        GameState state,
        ArmyState source,
        ArmyState target,
        List<UnitSlot> moving)
    {
        var pooledMoney = target.Money + source.Money;
        var cappedMoney = PurseAccounting.Credit(target.Money, source.Money, context.Ruleset);
        var excessToTreasury = pooledMoney - cappedMoney;

        var mergedTarget = target with
        {
            Units = ValueList.From(target.Units.Concat(moving)),
            SupplyTons = target.SupplyTons + source.SupplyTons,
            Money = cappedMoney,
        };

        var updatedArmies = state.Armies
            .Where(a => !string.Equals(a.Id, source.Id, StringComparison.Ordinal))
            .Select(a => string.Equals(a.Id, target.Id, StringComparison.Ordinal) ? mergedTarget : a);

        if (excessToTreasury == 0)
        {
            return CommandOutcome.Accept(state with { Armies = ValueList.From(updatedArmies) });
        }

        var updatedNation = context.IssuingNation with
        {
            Treasury = context.IssuingNation.Treasury + excessToTreasury,
        };
        var updatedNations = state.Nations.Select(n =>
            string.Equals(n.Id, updatedNation.Id, StringComparison.Ordinal) ? updatedNation : n);

        return CommandOutcome.Accept(state with
        {
            Armies = ValueList.From(updatedArmies),
            Nations = ValueList.From(updatedNations),
        });
    }

    /// <summary>Chebyshev distance through the engine's single shared metric.</summary>
    private static int ChebyshevDistance(int fromX, int fromY, int toX, int toY) =>
        LandingTile.ChebyshevDistance(new GridPoint(fromX, fromY), new GridPoint(toX, toY));
}
