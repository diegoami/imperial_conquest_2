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

        var city = state.CityById(command.CityId);
        if (city is null)
        {
            return CommandOutcome.Reject(
                BuySupplyRejections.UnknownCity, $"'{command.CityId}' is not a known city.");
        }

        var sellingCityNation = state.NationById(city.Owner);
        if (sellingCityNation is null)
        {
            return CommandOutcome.Reject(
                BuySupplyRejections.UnknownCity, $"City '{city.Id}''s owner '{city.Owner}' is not a known nation.");
        }

        if (command.Tons <= 0)
        {
            return CommandOutcome.Reject(
                BuySupplyRejections.InvalidAmount, "Must buy a positive number of tons.");
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
}
