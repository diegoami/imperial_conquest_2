using IC2.Engine.Core;
using IC2.Engine.Armies;
using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// <c>FUN_0044B79C</c>: zeroes the fleet's moves, sets the carried-army link both ways, clears the
/// army's covered map cell, snaps the army onto the fleet, and zeroes the army's moves too — trimming an
/// over-capacity army first when <see cref="EmbarkArmyCommand"/>'s <c>seatAsymmetry</c> gating admits it.
/// </summary>
[CommandHandler]
public sealed class EmbarkArmyCommandHandler : ICommandHandler<EmbarkArmyCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(EmbarkArmyCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var army = state.ArmyById(command.ArmyId);
        if (army is null)
        {
            return CommandOutcome.Reject(
                EmbarkArmyRejections.UnknownArmy, $"'{command.ArmyId}' is not a known army.");
        }

        var fleet = state.FleetById(command.FleetId);
        if (fleet is null)
        {
            return CommandOutcome.Reject(
                EmbarkArmyRejections.UnknownFleet, $"'{command.FleetId}' is not a known fleet.");
        }

        if (!string.Equals(army.Nation, command.IssuingNationId, StringComparison.Ordinal)
            || !string.Equals(fleet.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                EmbarkArmyRejections.NotYours, "Both the army and the fleet must belong to the issuing nation.");
        }

        if (fleet.IsUnderConstruction)
        {
            return CommandOutcome.Reject(
                EmbarkArmyRejections.UnderConstruction, $"Fleet '{fleet.Id}' is still under construction.");
        }

        if (army.IsEmbarked)
        {
            return CommandOutcome.Reject(
                EmbarkArmyRejections.ArmyAlreadyEmbarked, $"Army '{army.Id}' is already embarked.");
        }

        if (fleet.IsCarryingArmy)
        {
            return CommandOutcome.Reject(
                EmbarkArmyRejections.FleetAlreadyCarrying, $"Fleet '{fleet.Id}' is already carrying an army.");
        }

        // The original's rule (the user's statement of 2026-09-27, bug #453): an army embarks from a tile
        // adjacent to its fleet and then moves onto the fleet's tile. "Adjacent" is the same radius
        // disembark already uses -- Economy.CommandAdjacencyRadiusTiles (T70, bug #345) -- measured with
        // the same LandingTile.ChebyshevDistance. A Wine run of the original confirms the shape
        // [Wine candidate: 2026-10-02-fleet-orders-live.md]: army 0 at (101,45) boarded fleet 2 at
        // (101,46) and took the fleet's tile. The decompile's own embark check (TUnitMap_SelectUnit,
        // 0x004466CC, decompiled-unit-map-orders-and-record-fields.md) records the capacity test and the
        // FUN_0044B79C call with no distance test -- it is a click on a friendly fleet -- while
        // FUN_0044B79C itself "snaps the army's coordinates to the fleet's", the move the check below
        // admits. Searched the decompile for a distance constant on the embark path and found none: the
        // radius is the user's rule, not a reported field.
        var distance = LandingTile.ChebyshevDistance(new GridPoint(army.X, army.Y), new GridPoint(fleet.X, fleet.Y));
        if (distance > context.Ruleset.Economy.CommandAdjacencyRadiusTiles)
        {
            return CommandOutcome.Reject(
                EmbarkArmyRejections.NotAdjacent,
                $"Army '{army.Id}' at ({army.X}, {army.Y}) is not adjacent to fleet '{fleet.Id}' "
                + $"at ({fleet.X}, {fleet.Y}).");
        }

        var capacity = fleet.Ships * context.Ruleset.Naval.TransportTroopsPerShip;
        var totalTroops = army.TotalTroops;
        var seatIsAi = context.IssuingNation.Control == SeatControl.Ai;
        // Research83, 2026-10-26-human-transport-rounding83.md: the human UI
        // compares ships with troops div 500, admitting 5,499 on ten ships.
        // This is separate from the AI whole-slot allocator and Improved's
        // explicitly normalized exact-capacity refusal.
        var faithfulHuman = context.Ruleset.Flags.SeatAsymmetry == SeatAsymmetryModel.Faithful && !seatIsAi;
        var overCapacity = faithfulHuman
            ? totalTroops / context.Ruleset.Naval.TransportTroopsPerShip > fleet.Ships
            : totalTroops > capacity;

        // seatAsymmetry gating (design-audit.md Q6, EmbarkArmyCommand's own remarks): classical-faithful
        // trims an AI seat and refuses a human one; improved refuses every seat.
        var trims = overCapacity
            && context.Ruleset.Flags.SeatAsymmetry == SeatAsymmetryModel.Faithful
            && seatIsAi;
        var refuses = overCapacity && !trims;

        if (refuses)
        {
            return CommandOutcome.Reject(
                EmbarkArmyRejections.ArmyTooLarge,
                "The army is too large for this fleet.");
        }

        if (trims)
        {
            if (capacity <= 0)
                return CommandOutcome.Reject(EmbarkArmyRejections.ArmyTooLarge,
                    $"Fleet '{fleet.Id}' has no capacity for army '{army.Id}'.");
            // T224 / Research89: B79C boards before F8FC searches. Stage that
            // context without committing it: the source cell is now free and
            // the search is centered on the fleet. Allocation failure remains
            // an owner-approved atomic refusal, not destructive original writes.
            var boardedArmy = army with { X = fleet.X, Y = fleet.Y, Moves = 0,
                CoveredTileCode = null, AboardFleetId = fleet.Id };
            var boardedState = state with
            {
                Armies = ValueList.From(state.Armies.Select(a => a.Id == army.Id ? boardedArmy : a)),
                Fleets = ValueList.From(state.Fleets.Select(f => f.Id == fleet.Id
                    ? f with { Moves = 0, CarriedArmyId = army.Id } : f)),
            };
            if (state.Armies.Count >= context.Ruleset.ArmyManagement.MaxArmies
                || SplitPlacement.ArmyCellFor(boardedState, context.World, boardedArmy) is not { } land)
                return CommandOutcome.Reject(EmbarkArmyRejections.NoSafeOverflowReceiver,
                    $"Army '{army.Id}' cannot embark over capacity because no safe land receiver is available.");

            var partition = ArmyTransportTrim.PartitionWholeUnits(army.Units, capacity);
            var landMoney = army.Money / 3;
            var landSupply = army.SupplyTons / 3;
            var receiverId = $"{army.Id}-overflow";
            for (var suffix = 2; state.ArmyById(receiverId) is not null; suffix++)
                receiverId = $"{army.Id}-overflow-{suffix}";
            var terrain = context.World.Terrain.Decode(context.World.Width, context.World.Height);
            var receiver = new ArmyState(receiverId, army.Nation, land.X, land.Y,
                0, 59,
                landMoney, landSupply, terrain[land.Y * context.World.Width + land.X], null,
                ValueList.From(partition.Ashore));
            if (partition.Aboard.Count == 0)
                receiver = receiver with { Money = army.Money, SupplyTons = army.SupplyTons };
            var carrier = fleet with { Moves = 0,
                CarriedArmyId = partition.Aboard.Count == 0 ? null : army.Id };
            var updatedFleetsWithOverflow = ValueList.From(state.Fleets.Select(f =>
                f.Id == fleet.Id ? carrier : f));
            var updatedArmiesWithOverflow = partition.Aboard.Count == 0
                ? ValueList.From(state.Armies.Select(a => a.Id == army.Id ? receiver : a))
                : ValueList.From(state.Armies.Select(a => a.Id == army.Id
                    ? boardedArmy with
                    {
                        Units = ValueList.From(partition.Aboard),
                        Money = army.Money - landMoney,
                        SupplyTons = army.SupplyTons - landSupply,
                    }
                    : a).Append(receiver));
            // When every slot is ashore, the receiver replaces the emptied
            // source at its original live-model index. This is the native
            // conservation policy, not original raw-record compaction (#1439).
            return CommandOutcome.Accept(state with
            {
                Armies = updatedArmiesWithOverflow,
                Fleets = updatedFleetsWithOverflow,
            });
        }

        var units = army.Units;

        var updatedArmy = army with
        {
            X = fleet.X,
            Y = fleet.Y,
            Moves = 0,
            CoveredTileCode = null,
            AboardFleetId = fleet.Id,
            Units = units,
        };
        var updatedFleet = fleet with { CarriedArmyId = army.Id, Moves = 0 };

        var updatedArmies = state.Armies.Select(a =>
            string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? updatedArmy : a);
        var updatedFleets = state.Fleets.Select(f =>
            string.Equals(f.Id, fleet.Id, StringComparison.Ordinal) ? updatedFleet : f);

        return CommandOutcome.Accept(state with
        {
            Armies = ValueList.From(updatedArmies),
            Fleets = ValueList.From(updatedFleets),
        });
    }
}
