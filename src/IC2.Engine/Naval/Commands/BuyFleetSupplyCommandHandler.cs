using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;

namespace IC2.Engine.Naval.Commands;

/// <summary>See <see cref="BuyFleetSupplyCommand"/> for the full rule and its provenance.</summary>
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

        if (command.Tons <= 0)
        {
            return CommandOutcome.Reject(
                BuyFleetSupplyRejections.InvalidAmount, "Must buy a positive number of tons.");
        }

        var hasCityProvider = command.ProviderCityId is not null;
        var hasFleetProvider = command.ProviderFleetId is not null;
        if (hasCityProvider == hasFleetProvider)
        {
            return CommandOutcome.Reject(
                BuyFleetSupplyRejections.InvalidProvider,
                "Exactly one of a provider city or a provider fleet must be named.");
        }

        return hasCityProvider
            ? HandleCityProvider(command, context, state, fleet)
            : HandleFleetProvider(command, context, state, fleet);
    }

    private static CommandOutcome HandleCityProvider(
        BuyFleetSupplyCommand command, CommandContext context, GameState state, FleetState fleet)
    {
        var city = state.CityById(command.ProviderCityId!);
        if (city is null)
        {
            return CommandOutcome.Reject(
                BuyFleetSupplyRejections.UnknownCity, $"'{command.ProviderCityId}' is not a known city.");
        }

        if (ChebyshevDistance(fleet.X, fleet.Y, city.X, city.Y) > 1)
        {
            return CommandOutcome.Reject(
                BuyFleetSupplyRejections.CityNotWithinRange,
                $"City '{city.Id}' is not within one tile of fleet '{fleet.Id}'.");
        }

        var sellingCityNation = state.NationById(city.Owner);
        if (sellingCityNation is null)
        {
            return CommandOutcome.Reject(
                BuyFleetSupplyRejections.UnresolvableCityOwner,
                $"City '{city.Id}''s owner '{city.Owner}' is not a known nation.");
        }

        var isOwnCity = string.Equals(city.Owner, fleet.Nation, StringComparison.Ordinal);
        if (!isOwnCity)
        {
            var warCode = context.Ruleset.Diplomacy.StateCodes.War;
            if (state.Relations.Get(fleet.Nation, city.Owner) == warCode)
            {
                return CommandOutcome.Reject(
                    BuyFleetSupplyRejections.CityOwnerAtWar,
                    $"City '{city.Id}''s owner '{city.Owner}' is at war with '{fleet.Nation}'.");
            }
        }

        var result = SupplyPurchase.BuyForFleet(
            fleet, city, context.IssuingNation, sellingCityNation, command.Tons, context.Ruleset);

        context.Events.Publish(new FleetSupplyPurchased(
            fleet.Id, fleet.Nation, city.Id, null, command.Tons, result.AdmittedTons, result.TalentsPaid, result.WasFreeOwnCity));

        var updatedFleets = state.Fleets.Select(f =>
            string.Equals(f.Id, fleet.Id, StringComparison.Ordinal) ? result.Fleet : f);
        var updatedCities = state.Cities.Select(c =>
            string.Equals(c.Id, city.Id, StringComparison.Ordinal) ? result.City : c);
        var updatedNations = state.Nations.Select(n =>
            string.Equals(n.Id, result.BuyerNation.Id, StringComparison.Ordinal)
                ? result.BuyerNation
                : string.Equals(n.Id, result.SellingCityNation.Id, StringComparison.Ordinal)
                    ? result.SellingCityNation
                    : n);

        return CommandOutcome.Accept(state with
        {
            Fleets = ValueList.From(updatedFleets),
            Cities = ValueList.From(updatedCities),
            Nations = ValueList.From(updatedNations),
        });
    }

    private static CommandOutcome HandleFleetProvider(
        BuyFleetSupplyCommand command, CommandContext context, GameState state, FleetState fleet)
    {
        var providerFleetId = command.ProviderFleetId!;

        // DoD 4 (T14 round-2 review, B9): reject a same-fleet provider before any transfer math runs --
        // see FleetToFleetSupplyTransfer's remarks for the exact defect this closes.
        if (string.Equals(providerFleetId, fleet.Id, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                BuyFleetSupplyRejections.SelfSupply, $"Fleet '{fleet.Id}' cannot buy supply from itself.");
        }

        var provider = state.FleetById(providerFleetId);
        if (provider is null)
        {
            return CommandOutcome.Reject(
                BuyFleetSupplyRejections.UnknownFleet, $"'{providerFleetId}' is not a known fleet.");
        }

        // TAFSupply_FindProviders offers only the buyer's own nation's fleets -- a fleet provider is
        // never foreign, unlike a city provider.
        if (!string.Equals(provider.Nation, fleet.Nation, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                BuyFleetSupplyRejections.ProviderFleetNotYours,
                $"Fleet '{provider.Id}' belongs to '{provider.Nation}', not '{fleet.Nation}'.");
        }

        if (provider.IsUnderConstruction)
        {
            return CommandOutcome.Reject(
                BuyFleetSupplyRejections.UnderConstruction, $"Fleet '{provider.Id}' is still under construction.");
        }

        if (ChebyshevDistance(fleet.X, fleet.Y, provider.X, provider.Y) > 1)
        {
            return CommandOutcome.Reject(
                BuyFleetSupplyRejections.ProviderFleetNotWithinRange,
                $"Fleet '{provider.Id}' is not within one tile of fleet '{fleet.Id}'.");
        }

        var transfer = FleetToFleetSupplyTransfer.Transfer(fleet, provider, command.Tons, context.Ruleset);

        // DoD 8 [open]: no talents change hands on this path -- see FleetToFleetSupplyTransfer's remarks.
        context.Events.Publish(new FleetSupplyPurchased(
            fleet.Id, fleet.Nation, null, provider.Id, command.Tons, transfer.AdmittedTons, TalentsPaid: 0, WasFreeOwnCity: true));

        var updatedFleets = state.Fleets.Select(f =>
            string.Equals(f.Id, transfer.Buyer.Id, StringComparison.Ordinal) ? transfer.Buyer :
            string.Equals(f.Id, transfer.Provider.Id, StringComparison.Ordinal) ? transfer.Provider : f);

        return CommandOutcome.Accept(state with { Fleets = ValueList.From(updatedFleets) });
    }

    private static int ChebyshevDistance(int ax, int ay, int bx, int by) =>
        Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));
}
