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
    public static bool IsOrderInProgress(int code, CityOrderRule rule) => code > rule.InProgressEncodingRadix;

    /// <summary>The completed fortification percentage the stored word represents.</summary>
    public static int FinishedPercent(int code, CityOrderRule rule) =>
        IsOrderInProgress(code, rule) ? code % rule.InProgressEncodingRadix : code;

    /// <summary>The number of points still under construction, or zero when no order is pending.</summary>
    public static int PendingPoints(int code, CityOrderRule rule) =>
        IsOrderInProgress(code, rule) ? code / rule.InProgressEncodingRadix : 0;

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

    /// <summary>The largest order, in points, that may still be placed on this city.</summary>
    public static int MaxOrderablePoints(int code, CityOrderRule rule) =>
        rule.MaxPercent - FinishedPercent(code, rule);
}
