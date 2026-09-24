using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// <c>TUnitMap_ScuttleFleet</c>: near one of the fleet's own nation's cities, the fleet's money goes to
/// the treasury and its supplies to that city's stock; the fleet is then removed.
/// </summary>
[CommandHandler]
public sealed class ScuttleFleetCommandHandler : ICommandHandler<ScuttleFleetCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(ScuttleFleetCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var fleet = state.FleetById(command.FleetId);
        if (fleet is null)
        {
            return CommandOutcome.Reject(
                ScuttleFleetRejections.UnknownFleet, $"'{command.FleetId}' is not a known fleet.");
        }

        if (!string.Equals(fleet.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                ScuttleFleetRejections.NotYourFleet,
                $"Fleet '{fleet.Id}' belongs to '{fleet.Nation}', not '{command.IssuingNationId}'.");
        }

        if (fleet.IsUnderConstruction)
        {
            return CommandOutcome.Reject(
                ScuttleFleetRejections.UnderConstruction, $"Fleet '{fleet.Id}' is still under construction.");
        }

        if (fleet.IsCarryingArmy)
        {
            return CommandOutcome.Reject(
                ScuttleFleetRejections.CarryingArmy, $"Fleet '{fleet.Id}' is carrying an army and cannot be scuttled.");
        }

        CityState? nearbyOwnedCity = null;
        foreach (var city in state.Cities)
        {
            if (!string.Equals(city.Owner, fleet.Nation, StringComparison.Ordinal))
            {
                continue;
            }

            var distance = LandingTile.ChebyshevDistance(new GridPoint(city.X, city.Y), new GridPoint(fleet.X, fleet.Y));
            if (distance <= context.Ruleset.Economy.CommandAdjacencyRadiusTiles)
            {
                nearbyOwnedCity = city;
                break;
            }
        }

        if (nearbyOwnedCity is null)
        {
            return CommandOutcome.Reject(
                ScuttleFleetRejections.NotNearOwnedCity,
                $"Fleet '{fleet.Id}' must be near one of its own nation's cities to scuttle.");
        }

        var updatedCity = nearbyOwnedCity with { SupplyTons = nearbyOwnedCity.SupplyTons + fleet.SupplyTons };
        var updatedNation = context.IssuingNation with { Treasury = context.IssuingNation.Treasury + fleet.Money };

        var updatedFleets = state.Fleets.Where(f => !string.Equals(f.Id, fleet.Id, StringComparison.Ordinal));
        var updatedCities = state.Cities.Select(c =>
            string.Equals(c.Id, updatedCity.Id, StringComparison.Ordinal) ? updatedCity : c);
        var updatedNations = state.Nations.Select(n =>
            string.Equals(n.Id, updatedNation.Id, StringComparison.Ordinal) ? updatedNation : n);

        return CommandOutcome.Accept(state with
        {
            Fleets = ValueList.From(updatedFleets),
            Cities = ValueList.From(updatedCities),
            Nations = ValueList.From(updatedNations),
        });
    }
}
