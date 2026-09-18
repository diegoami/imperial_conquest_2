using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy.Commands;

/// <summary>
/// Wires <see cref="SupplyPurchase.BuyForArmy"/> behind the command seam —
/// <c>docs/task-catalogue.md</c> "T41 Thin CLI demo on the toy world", moved to the new API by "T38
/// Supply dialog follow-ups, treasury ↔ purse transfers, and automatic resupply" (issue #78). Every
/// rejection this handler adds is a command-layer concern (does the army/city exist, is it yours, is the
/// request even well-formed); the purchase rule itself — free at an owned city, <c>amount / 5</c> abroad,
/// clamped by stock/room/money rather than rejected for exceeding any of them — stays entirely inside
/// <see cref="SupplyPurchase"/>.
/// </summary>
/// <remarks>
/// T38 (#78, Done-when 1): <see cref="SupplyPurchase.BuyForArmy"/> no longer throws for a request that
/// exceeds the city's stock or the buyer's affordable room — it clamps <c>AdmittedTons</c> instead, so
/// this handler no longer needs the pre-check or the <c>catch</c> the old, throwing version required.
/// It still validates the city holds a resolvable owner nation, since <see cref="SupplyPurchase.BuyForArmy"/>
/// now takes that nation as a parameter (Done-when 5, the seller's credit).
/// <para>
/// Issue #147's folded direction (T46 Done-when 11) adds the fleet-provider branch: exactly one of
/// <see cref="BuySupplyCommand.CityId"/>/<see cref="BuySupplyCommand.ProviderFleetId"/> must be set, and
/// the fleet-provider path moves tons only — no talents change hands — matching
/// <see cref="BuySupplyCommand"/>'s own remarks on the payment leg's <c>[open]</c> tag. The buyer's room
/// uses <see cref="SupplyCapacity.ArmyDialogCapacityTons"/> (the dialog's <c>+1</c> bonus), the same cap
/// <see cref="SupplyPurchase.BuyForArmy"/> already uses for the city path — this is the same
/// <c>TUnitMap_SupplyArmy</c> → <c>TAFSupply</c> dialog either way, only the provider kind differs.
/// </para>
/// </remarks>
[CommandHandler]
public sealed class BuySupplyCommandHandler : ICommandHandler<BuySupplyCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(BuySupplyCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var army = state.ArmyById(command.ArmyId);
        if (army is null)
        {
            return CommandOutcome.Reject(
                BuySupplyRejections.UnknownArmy, $"'{command.ArmyId}' is not a known army.");
        }

        if (!string.Equals(army.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                BuySupplyRejections.NotYourArmy,
                $"Army '{army.Id}' belongs to '{army.Nation}', not '{command.IssuingNationId}'.");
        }

        if (command.Tons <= 0)
        {
            return CommandOutcome.Reject(
                BuySupplyRejections.InvalidAmount, "Must buy a positive number of tons.");
        }

        var hasCityProvider = command.CityId is not null;
        var hasFleetProvider = command.ProviderFleetId is not null;
        if (hasCityProvider == hasFleetProvider)
        {
            return CommandOutcome.Reject(
                BuySupplyRejections.InvalidProvider,
                "Exactly one of a provider city or a provider fleet must be named.");
        }

        return hasCityProvider
            ? HandleCityProvider(command, context, state, army)
            : HandleFleetProvider(command, context, state, army);
    }

    private static CommandOutcome HandleCityProvider(BuySupplyCommand command, CommandContext context, GameState state, ArmyState army)
    {
        var city = state.CityById(command.CityId!);
        if (city is null)
        {
            return CommandOutcome.Reject(
                BuySupplyRejections.UnknownCity, $"'{command.CityId}' is not a known city.");
        }

        // Review round 1, N7: the city itself is known -- its owner nation is the problem -- so this is
        // UnresolvableCityOwner, not UnknownCity. Defensive: unreachable while every city's Owner names a
        // real nation, which the loaded data always satisfies today.
        var sellingCityNation = state.NationById(city.Owner);
        if (sellingCityNation is null)
        {
            return CommandOutcome.Reject(
                BuySupplyRejections.UnresolvableCityOwner, $"City '{city.Id}''s owner '{city.Owner}' is not a known nation.");
        }

        var result = SupplyPurchase.BuyForArmy(army, city, context.IssuingNation, sellingCityNation, command.Tons, context.Ruleset);

        context.Events.Publish(new ArmySupplyPurchased(
            army.Id, army.Nation, city.Id, command.Tons, result.AdmittedTons, result.TalentsPaid, result.WasFreeOwnCity));

        var updatedArmies = state.Armies.Select(a =>
            string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? result.Army : a);
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
            Armies = ValueList.From(updatedArmies),
            Cities = ValueList.From(updatedCities),
            Nations = ValueList.From(updatedNations),
        });
    }

    /// <summary>Issue #147's folded direction: the army buys supply from one of its own nation's fleets.</summary>
    private static CommandOutcome HandleFleetProvider(BuySupplyCommand command, CommandContext context, GameState state, ArmyState army)
    {
        var providerFleetId = command.ProviderFleetId!;
        var provider = state.FleetById(providerFleetId);
        if (provider is null)
        {
            return CommandOutcome.Reject(
                BuySupplyRejections.UnknownProviderFleet, $"'{providerFleetId}' is not a known fleet.");
        }

        // TAFSupply_FindProviders offers only the buyer's own nation's fleets -- a fleet provider is
        // never foreign, unlike a city provider.
        if (!string.Equals(provider.Nation, army.Nation, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                BuySupplyRejections.ProviderFleetNotYours,
                $"Fleet '{provider.Id}' belongs to '{provider.Nation}', not '{army.Nation}'.");
        }

        if (provider.IsUnderConstruction)
        {
            return CommandOutcome.Reject(
                BuySupplyRejections.ProviderFleetUnderConstruction, $"Fleet '{provider.Id}' is still under construction.");
        }

        var distance = Math.Max(Math.Abs(army.X - provider.X), Math.Abs(army.Y - provider.Y));
        if (distance > 1)
        {
            return CommandOutcome.Reject(
                BuySupplyRejections.ProviderFleetNotWithinRange,
                $"Fleet '{provider.Id}' is not within one tile of army '{army.Id}'.");
        }

        // DoD 8/#147 [open]: no talents change hands here -- see BuySupplyCommand's remarks. Same dialog
        // capacity (with the +1 bonus) SupplyPurchase.BuyForArmy already uses for the city path.
        var capacity = SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops, context.Ruleset);
        var room = capacity - army.SupplyTons; // not floored at 0, matching SupplyPurchase's model.
        var admittedTons = Math.Min(command.Tons, Math.Min(provider.SupplyTons, room));

        var updatedArmy = army with { SupplyTons = army.SupplyTons + admittedTons };
        var updatedProvider = provider with { SupplyTons = provider.SupplyTons - admittedTons };

        context.Events.Publish(new ArmySupplyPurchasedFromFleet(army.Id, army.Nation, provider.Id, command.Tons, admittedTons));

        var updatedArmies = state.Armies.Select(a =>
            string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? updatedArmy : a);
        var updatedFleets = state.Fleets.Select(f =>
            string.Equals(f.Id, provider.Id, StringComparison.Ordinal) ? updatedProvider : f);

        return CommandOutcome.Accept(state with
        {
            Armies = ValueList.From(updatedArmies),
            Fleets = ValueList.From(updatedFleets),
        });
    }
}
