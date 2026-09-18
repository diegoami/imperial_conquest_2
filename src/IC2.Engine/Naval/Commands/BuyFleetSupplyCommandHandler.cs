using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Naval.Commands;

/// <summary><c>TUnitMap_SupplyFleet</c> / <c>TAFSupply</c>. See <see cref="BuyFleetSupplyCommand"/>'s remarks.</summary>
[CommandHandler]
public sealed class BuyFleetSupplyCommandHandler : ICommandHandler<BuyFleetSupplyCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(BuyFleetSupplyCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var fleet = state.FleetById(command.FleetId);
        if (fleet is null)
        {
            return CommandOutcome.Reject(
                BuyFleetSupplyRejections.UnknownFleet, $"'{command.FleetId}' is not a known fleet.");
        }

        if (!string.Equals(fleet.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                BuyFleetSupplyRejections.NotYourFleet,
                $"Fleet '{fleet.Id}' belongs to '{fleet.Nation}', not '{command.IssuingNationId}'.");
        }

        if (fleet.IsUnderConstruction)
        {
            return CommandOutcome.Reject(
                BuyFleetSupplyRejections.UnderConstruction, $"Fleet '{fleet.Id}' is still under construction.");
        }

        var hasCityProvider = command.ProviderCityId is not null;
        var hasFleetProvider = command.ProviderFleetId is not null;
        if (hasCityProvider == hasFleetProvider) // both set, or neither.
        {
            return CommandOutcome.Reject(
                BuyFleetSupplyRejections.ExactlyOneProviderRequired,
                "Exactly one of a provider city or a provider fleet must be named.");
        }

        if (command.Tons <= 0)
        {
            return CommandOutcome.Reject(BuyFleetSupplyRejections.InvalidAmount, "Must request a positive number of tons.");
        }

        var fleetPoint = new GridPoint(fleet.X, fleet.Y);

        if (hasCityProvider)
        {
            var city = state.CityById(command.ProviderCityId!);
            if (city is null)
            {
                return CommandOutcome.Reject(
                    BuyFleetSupplyRejections.UnknownCity, $"'{command.ProviderCityId}' is not a known city.");
            }

            if (LandingTile.ChebyshevDistance(fleetPoint, new GridPoint(city.X, city.Y)) > 1)
            {
                return CommandOutcome.Reject(
                    BuyFleetSupplyRejections.ProviderTooFar, $"City '{city.Id}' is not within one tile of fleet '{fleet.Id}'.");
            }

            var sellingCityNation = state.NationById(city.Owner);
            if (sellingCityNation is null)
            {
                return CommandOutcome.Reject(
                    BuyFleetSupplyRejections.UnresolvableCityOwner,
                    $"City '{city.Id}''s owner '{city.Owner}' is not a known nation.");
            }

            if (!string.Equals(city.Owner, fleet.Nation, StringComparison.Ordinal))
            {
                var relation = state.Relations.Get(fleet.Nation, city.Owner);
                if (relation == context.Ruleset.Diplomacy.StateCodes.War)
                {
                    return CommandOutcome.Reject(
                        BuyFleetSupplyRejections.ProviderAtWar,
                        $"City '{city.Id}''s owner '{city.Owner}' is at war with '{fleet.Nation}'.");
                }
            }

            var result = SupplyPurchase.BuyForFleet(
                fleet, city, context.IssuingNation, sellingCityNation, command.Tons, context.Ruleset);

            var updatedFleets = state.Fleets.Select(f =>
                string.Equals(f.Id, fleet.Id, StringComparison.Ordinal) ? result.Fleet : f);
            var updatedCities = state.Cities.Select(c =>
                string.Equals(c.Id, city.Id, StringComparison.Ordinal) ? result.City : c);
            var updatedNations = state.Nations.Select(n =>
                string.Equals(n.Id, result.BuyerNation.Id, StringComparison.Ordinal) ? result.BuyerNation
                : string.Equals(n.Id, result.SellingCityNation.Id, StringComparison.Ordinal) ? result.SellingCityNation
                : n);

            return CommandOutcome.Accept(state with
            {
                Fleets = ValueList.From(updatedFleets),
                Cities = ValueList.From(updatedCities),
                Nations = ValueList.From(updatedNations),
            });
        }

        // Fleet provider: "the nation's own fleets" only -- see BuyFleetSupplyCommand's remarks.
        var providerFleet = state.FleetById(command.ProviderFleetId!);
        if (providerFleet is null)
        {
            return CommandOutcome.Reject(
                BuyFleetSupplyRejections.UnknownProviderFleet, $"'{command.ProviderFleetId}' is not a known fleet.");
        }

        if (!string.Equals(providerFleet.Nation, fleet.Nation, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                BuyFleetSupplyRejections.ProviderFleetNotOwnNation,
                $"Fleet '{providerFleet.Id}' belongs to '{providerFleet.Nation}', not the buyer's own nation '{fleet.Nation}'.");
        }

        if (LandingTile.ChebyshevDistance(fleetPoint, new GridPoint(providerFleet.X, providerFleet.Y)) > 1)
        {
            return CommandOutcome.Reject(
                BuyFleetSupplyRejections.ProviderTooFar,
                $"Fleet '{providerFleet.Id}' is not within one tile of fleet '{fleet.Id}'.");
        }

        var transfer = FleetToFleetSupplyTransfer.Transfer(fleet, providerFleet, command.Tons, context.Ruleset);

        var updatedFleetsFromFleet = state.Fleets.Select(f =>
            string.Equals(f.Id, fleet.Id, StringComparison.Ordinal) ? transfer.Buyer
            : string.Equals(f.Id, providerFleet.Id, StringComparison.Ordinal) ? transfer.Provider
            : f);

        return CommandOutcome.Accept(state with { Fleets = ValueList.From(updatedFleetsFromFleet) });
    }
}
