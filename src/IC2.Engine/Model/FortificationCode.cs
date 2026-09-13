namespace IC2.Engine.Model;

/// <summary>
/// Decodes a city's fortification word, which does double duty in the original: a value at or below
/// the order's maximum is a finished percentage, and a value above it encodes an order in progress,
/// written as <c>fortification += points × radix</c>
/// <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</strong>.
/// </summary>
/// <remarks>
/// Every method takes the <see cref="CityOrderRule"/> the caller loaded from the <see cref="Ruleset"/>,
/// so the encoding's radix and maximum are ruleset data rather than literals in engine source. The
/// stored word is kept verbatim on <see cref="CityDefinition.FortificationCode"/> and
/// <see cref="CityState.FortificationCode"/> so a save round-trips the in-progress state without
/// needing a second field.
/// </remarks>
public static class FortificationCode
{
    /// <summary>Whether the stored word encodes a fortification order still under construction.</summary>
    /// <remarks>
    /// The threshold is the order's <see cref="CityOrderRule.MaxPercent"/>, not its
    /// <see cref="CityOrderRule.InProgressEncodingRadix"/>: the two happen to both be 100 in the
    /// original, but they mean different things, and a ruleset is free to set them apart. The radix is
    /// used for the arithmetic only.
    /// <para>
    /// One ambiguity is inherited deliberately: a city at 0% ordering exactly one point stores
    /// <c>0 + 1 × 100 = 100</c>, which reads back as "fortified to 100%, nothing pending". The original
    /// has the identical ambiguity in its own <c>fort &gt; 100</c> test, so reproducing it is faithful
    /// rather than a defect.
    /// </para>
    /// </remarks>
    public static bool IsOrderInProgress(int code, CityOrderRule rule) => code > rule.MaxPercent;

    /// <summary>The completed fortification percentage the stored word represents.</summary>
    public static int FinishedPercent(int code, CityOrderRule rule) =>
        IsOrderInProgress(code, rule) ? code % rule.InProgressEncodingRadix : code;

    /// <summary>The number of points still under construction, or zero when no order is pending.</summary>
    public static int PendingPoints(int code, CityOrderRule rule) =>
        IsOrderInProgress(code, rule) ? code / rule.InProgressEncodingRadix : 0;

    /// <summary>
    /// Whether a further fortification order may be placed at all. The original refuses outright while
    /// an order is already pending (<em>"This city is already being fortified."</em>) and at the
    /// maximum (<em>"This city cannot be fortified any further."</em>)
    /// <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</strong>. Sieges are the
    /// caller's business: the rule carries <see cref="CityOrderRule.RefusedWhileUnderSiege"/> and the
    /// besieged flag lives on the city, not in the stored word.
    /// </summary>
    public static bool CanPlaceOrder(int code, CityOrderRule rule) =>
        !IsOrderInProgress(code, rule) && FinishedPercent(code, rule) < rule.MaxPercent;

    /// <summary>
    /// The word that results from placing an order for <paramref name="points"/> further points on top
    /// of <paramref name="code"/>.
    /// </summary>
    public static int WithOrder(int code, int points, CityOrderRule rule) =>
        code + (points * rule.InProgressEncodingRadix);

    /// <summary>
    /// The word that results from a siege attempt, which wipes any pending order
    /// (<c>FUN_0044B27C</c>: <c>if (fort &gt; 100) fort = fort % 100</c>).
    /// </summary>
    public static int AfterSiegeAttempt(int code, CityOrderRule rule) =>
        IsOrderInProgress(code, rule) ? code % rule.InProgressEncodingRadix : code;

    /// <summary>
    /// The largest order, in points, that may still be placed on this city — zero while an order is
    /// already pending, since the original refuses a second one outright.
    /// </summary>
    /// <remarks>
    /// It returns zero rather than <c>maxPercent − finished − pending</c> on purpose. Allowing a
    /// top-up would let the finished-plus-pending total exceed
    /// <see cref="CityOrderRule.MaxPercent"/>, which the original's single-order-at-a-time rule makes
    /// unreachable; handing a caller a number that overshoots the maximum would be a trap for whoever
    /// implements city orders.
    /// </remarks>
    public static int MaxOrderablePoints(int code, CityOrderRule rule) =>
        CanPlaceOrder(code, rule) ? rule.MaxPercent - FinishedPercent(code, rule) : 0;
}
