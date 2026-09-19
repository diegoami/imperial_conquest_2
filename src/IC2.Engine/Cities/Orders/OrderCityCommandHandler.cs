using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Cities.Orders;

/// <summary>Handles city order commands (fortification, etc.).</summary>
[CommandHandler]
public sealed class OrderCityCommandHandler : ICommandHandler<OrderCityCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(OrderCityCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var ruleset = context.Ruleset;

        // Find the city
        var city = state.CityById(command.CityId);
        if (city is null)
        {
            return CommandOutcome.Reject(
                CityOrderRejections.UnknownCity,
                $"City '{command.CityId}' not found.");
        }

        // Verify ownership
        if (!string.Equals(city.Owner, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                CityOrderRejections.NotCityOwner,
                $"Nation '{command.IssuingNationId}' does not own city '{city.Name}'.");
        }

        // Find the order type
        var orderRule = ruleset.CityOrders.Orders.FirstOrDefault(o => string.Equals(o.Id, command.OrderId, StringComparison.Ordinal));
        if (orderRule is null)
        {
            return CommandOutcome.Reject(
                CityOrderRejections.UnknownOrderType,
                $"Order type '{command.OrderId}' not found in ruleset.");
        }

        // Validate points
        if (command.Points <= 0)
        {
            return CommandOutcome.Reject(
                CityOrderRejections.InvalidOrderPoints,
                $"Order points must be positive, got {command.Points}.");
        }

        // Check if already at maximum
        var currentPercent = FortificationCode.FinishedPercent(city.FortificationCode, orderRule);
        if (currentPercent >= orderRule.MaxPercent)
        {
            return CommandOutcome.Reject(
                CityOrderRejections.FortifyRefusedAtMaxPercent,
                $"City '{city.Name}' is already fully {command.OrderId}ified.");
        }

        // Check if order already pending
        if (FortificationCode.IsOrderInProgress(city.FortificationCode, orderRule))
        {
            return CommandOutcome.Reject(
                CityOrderRejections.FortifyRefusedOrderPending,
                $"City '{city.Name}' already has a {command.OrderId} order pending.");
        }

        // Check if under siege (if order refuses under siege)
        if (orderRule.RefusedWhileUnderSiege && city.UnderSiege)
        {
            return CommandOutcome.Reject(
                CityOrderRejections.FortifyRefusedWhileUnderSiege,
                $"City '{city.Name}' is under siege and cannot be {command.OrderId}ified.");
        }

        // Check points don't exceed max available
        var maxOrderablePoints = FortificationCode.MaxOrderablePoints(city.FortificationCode, orderRule);
        if (command.Points > maxOrderablePoints)
        {
            return CommandOutcome.Reject(
                CityOrderRejections.InvalidOrderPoints,
                $"Cannot order {command.Points} points; maximum is {maxOrderablePoints}.");
        }

        // Calculate cost
        var costPerPoint = orderRule.CostPerPointPerPopulationThousand;
        var totalCost = costPerPoint * command.Points * city.PopulationThousands;

        // Find the nation and check treasury
        var nation = state.NationById(command.IssuingNationId);
        if (nation is null || nation.Treasury < totalCost)
        {
            var availableTreasury = nation?.Treasury ?? 0;
            return CommandOutcome.Reject(
                CityOrderRejections.InsufficientTreasury,
                $"Insufficient treasury ({availableTreasury}) for {command.OrderId} order costing {totalCost} talents.");
        }

        // Apply the order
        var newFortificationCode = FortificationCode.WithOrder(city.FortificationCode, command.Points, orderRule);
        var updatedCity = city with { FortificationCode = newFortificationCode };

        var updatedNation = nation with { Treasury = nation.Treasury - totalCost };

        var updatedState = state with
        {
            Cities = ReplaceCity(state.Cities, updatedCity),
            Nations = ReplaceNation(state.Nations, updatedNation),
        };

        return CommandOutcome.Accept(updatedState);
    }

    private static ValueList<CityState> ReplaceCity(ValueList<CityState> cities, CityState updated)
    {
        var list = new List<CityState>(cities.Count);
        foreach (var city in cities)
        {
            list.Add(string.Equals(city.Id, updated.Id, StringComparison.Ordinal) ? updated : city);
        }
        return ValueList.From(list);
    }

    private static ValueList<NationState> ReplaceNation(ValueList<NationState> nations, NationState updated)
    {
        var list = new List<NationState>(nations.Count);
        foreach (var nation in nations)
        {
            list.Add(string.Equals(nation.Id, updated.Id, StringComparison.Ordinal) ? updated : nation);
        }
        return ValueList.From(list);
    }
}
