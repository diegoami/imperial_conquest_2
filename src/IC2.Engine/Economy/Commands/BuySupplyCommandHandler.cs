using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy.Commands;

/// <summary>
/// Wires <see cref="SupplyPurchase.BuyForArmy"/> behind the command seam —
/// <c>docs/task-catalogue.md</c> "T41 Thin CLI demo on the toy world". Every rejection this handler adds
/// is a command-layer concern (does the army/city exist, is it yours, is the request even well-formed);
/// the purchase rule itself — free at an owned city, <c>amount / 5</c> abroad, capped at the dialog
/// capacity — stays entirely inside <see cref="SupplyPurchase"/>.
/// </summary>
/// <remarks>
/// <see cref="SupplyPurchase.BuyForArmy"/> throws <see cref="ArgumentException"/> for two conditions:
/// the city not holding the raw requested tons (checked here first, so that path never reaches it), and
/// a per-unit purse unable to cover the capacity-clamped, foreign-city cost — which this handler cannot
/// predict without re-deriving the same capacity clamp <see cref="SupplyPurchase"/> already applies
/// internally, so it is instead translated into <see cref="BuySupplyRejections.InsufficientFunds"/> at the
/// one call site, exactly as <see cref="ICommandHandler{TCommand}.Handle"/> requires: an illegal order is
/// an outcome, never an exception escaping to the dispatcher.
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

        if (command.Tons <= 0)
        {
            return CommandOutcome.Reject(
                BuySupplyRejections.InvalidAmount, "Must buy a positive number of tons.");
        }

        if (city.SupplyTons < command.Tons)
        {
            return CommandOutcome.Reject(
                BuySupplyRejections.InsufficientCitySupply,
                $"City '{city.Id}' holds only {city.SupplyTons} tons of supply, cannot sell {command.Tons}.");
        }

        SupplyPurchase.ArmyResult result;
        try
        {
            result = SupplyPurchase.BuyForArmy(army, city, context.IssuingNation, command.Tons, context.Ruleset);
        }
        catch (ArgumentException ex)
        {
            return CommandOutcome.Reject(BuySupplyRejections.InsufficientFunds, ex.Message);
        }

        var admittedTons = result.Army.SupplyTons - army.SupplyTons;

        context.Events.Publish(new ArmySupplyPurchased(
            army.Id, army.Nation, city.Id, command.Tons, admittedTons, result.TalentsPaid, result.WasFreeOwnCity));

        var updatedArmies = state.Armies.Select(a =>
            string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? result.Army : a);
        var updatedCities = state.Cities.Select(c =>
            string.Equals(c.Id, city.Id, StringComparison.Ordinal) ? result.City : c);
        var updatedNations = state.Nations.Select(n =>
            string.Equals(n.Id, result.BuyerNation.Id, StringComparison.Ordinal) ? result.BuyerNation : n);

        return CommandOutcome.Accept(state with
        {
            Armies = ValueList.From(updatedArmies),
            Cities = ValueList.From(updatedCities),
            Nations = ValueList.From(updatedNations),
        });
    }
}
