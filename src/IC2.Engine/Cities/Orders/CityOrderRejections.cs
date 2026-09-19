using IC2.Engine.Core;

namespace IC2.Engine.Cities.Orders;

/// <summary>Rejection codes for city orders.</summary>
public static class CityOrderRejections
{
    /// <summary>The command names a city the state does not contain.</summary>
    public static readonly RejectionCode UnknownCity = new("city-order.unknown-city");

    /// <summary>The nation does not own the city.</summary>
    public static readonly RejectionCode NotCityOwner = new("city-order.not-owner");

    /// <summary>The city is already at the maximum fortification percentage.</summary>
    public static readonly RejectionCode FortifyRefusedAtMaxPercent = new("city-order.fortify-refused-at-max-percent");

    /// <summary>The city is under siege and fortification is refused.</summary>
    public static readonly RejectionCode FortifyRefusedWhileUnderSiege = new("city-order.fortify-refused-while-under-siege");

    /// <summary>A fortification order is already pending at this city.</summary>
    public static readonly RejectionCode FortifyRefusedOrderPending = new("city-order.fortify-refused-order-pending");

    /// <summary>The nation does not have enough talents to pay for the order.</summary>
    public static readonly RejectionCode InsufficientTreasury = new("city-order.insufficient-treasury");

    /// <summary>The command names an order type the ruleset does not contain.</summary>
    public static readonly RejectionCode UnknownOrderType = new("city-order.unknown-order-type");

    /// <summary>The requested order points value is zero or negative.</summary>
    public static readonly RejectionCode InvalidOrderPoints = new("city-order.invalid-order-points");
}
