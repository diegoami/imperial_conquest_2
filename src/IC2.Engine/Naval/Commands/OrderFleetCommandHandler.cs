using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// The build order itself — <c>docs/task-catalogue.md</c> "T14 Naval", Done-when 1 and 2: <c>10-100</c>
/// ships, cost <c>ships × 10</c>, a 24-tick construction countdown, coastal nations only. The record
/// starts under construction (<see cref="Model.FleetState.ConstructionTicksRemaining"/> set), and
/// <see cref="FleetTickSystem"/> — not this handler — decrements the countdown and launches it.
/// </summary>
[CommandHandler]
public sealed class OrderFleetCommandHandler : ICommandHandler<OrderFleetCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(OrderFleetCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var city = state.CityById(command.CityId);
        if (city is null)
        {
            return CommandOutcome.Reject(
                OrderFleetRejections.UnknownCity, $"'{command.CityId}' is not a known city.");
        }

        if (!string.Equals(city.Owner, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                OrderFleetRejections.NotYourCity,
                $"City '{city.Id}' belongs to '{city.Owner}', not '{command.IssuingNationId}'.");
        }

        if (!CoastalCity.IsCoastal(city, context.World))
        {
            return CommandOutcome.Reject(
                OrderFleetRejections.CityNotCoastal, $"City '{city.Id}' does not touch the sea.");
        }

        var rules = context.Ruleset.Naval;
        if (command.Ships < rules.OrderMinShips || command.Ships > rules.OrderMaxShips)
        {
            return CommandOutcome.Reject(
                OrderFleetRejections.ShipsOutOfRange,
                $"A fleet order must be {rules.OrderMinShips}..{rules.OrderMaxShips} ships, not {command.Ships}.");
        }

        if (state.FleetById(command.NewFleetId) is not null)
        {
            return CommandOutcome.Reject(
                OrderFleetRejections.DuplicateFleetId, $"Fleet id '{command.NewFleetId}' is already in use.");
        }

        // Matches the confirmed under-construction convention exactly (fleet-order-at-caere.md,
        // decompiled-unit-map-orders-and-record-fields.md part 4): every unlaunched fleet reads (0, 0),
        // regardless of its build city, because it is not really "on the map" yet. The real position --
        // a sea tile adjacent to the build city, since a fleet's own terrain-passability rule never
        // admits the city's own land tile -- is assigned on launch by FleetTickSystem.
        var fleet = new FleetState(
            Id: command.NewFleetId,
            Nation: command.IssuingNationId,
            X: 0,
            Y: 0,
            Moves: 0,
            Ships: command.Ships,
            ConditionPercent: 0,
            Money: 0,
            SupplyTons: 0,
            ConstructionTicksRemaining: rules.ConstructionTicks,
            BuildCityId: city.Id,
            CarriedArmyId: null,
            CoveredTileCode: null);

        var cost = command.Ships * rules.BuildCostPerShip;
        var updatedNation = context.IssuingNation with { Treasury = context.IssuingNation.Treasury - cost };

        var updatedFleets = state.Fleets.Append(fleet);
        var updatedNations = state.Nations.Select(n =>
            string.Equals(n.Id, updatedNation.Id, StringComparison.Ordinal) ? updatedNation : n);

        return CommandOutcome.Accept(state with
        {
            Fleets = ValueList.From(updatedFleets),
            Nations = ValueList.From(updatedNations),
        });
    }
}
