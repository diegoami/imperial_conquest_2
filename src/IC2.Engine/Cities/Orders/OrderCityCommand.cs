using IC2.Engine.Core;

namespace IC2.Engine.Cities.Orders;

/// <summary>
/// Issue a city order (currently fortification only) on a city owned by the issuing nation.
/// </summary>
/// <param name="IssuingNationId">The nation placing the order at one of its own cities.</param>
/// <param name="CityId">The city to place the order on.</param>
/// <param name="OrderId">The order type, e.g. "fortify" — must exist in the ruleset.</param>
/// <param name="Points">The number of percentage points to order, 1 to <c>MaxOrderablePoints</c>.</param>
public sealed record OrderCityCommand(
    string IssuingNationId,
    string CityId,
    string OrderId,
    int Points) : ICommand
{
    public string Kind => "city.order";
}
