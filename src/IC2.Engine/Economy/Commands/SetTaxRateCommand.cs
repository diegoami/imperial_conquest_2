using IC2.Engine.Core;

namespace IC2.Engine.Economy.Commands;

/// <summary>
/// Sets the issuing nation's tax rate immediately — <c>docs/tasks/T103.md</c> "A command sets the
/// nation's tax rate", bug
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/468">#468</see>. The original's
/// <c>TChangeTax</c> dialog writes nation <c>+0x44A</c> on OK
/// <strong>[confirmed: decompiled-fleet-tax-and-mercenary-formulas.md,
/// rome-tax-increase-and-sidon-capture.md; the OK write is derived: <c>TChangeTax_OK</c>]</strong>.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in the clone wrote <see cref="Model.NationState.TaxRatePercent"/> before this command; the
/// quarterly consumers that already read it — <see cref="TaxIncome"/>, <see cref="NationUnityUpdate"/>,
/// <see cref="CityPopulationGrowth"/> and <see cref="CityLoyaltyDraws"/> — therefore use the new rate from
/// the next quarter on, with no further wiring.
/// </para>
/// <para>
/// <strong>The accepted range is the dialog's own, not the percentage's 0–100.</strong> The original's
/// <c>TChangeTax</c> slider reports minimum 0 and maximum 40, line size 1 and page size 5
/// <strong>[Wine candidate: 2026-10-02-unit-map-mouse-orders-and-tax-range.md, (g)]</strong>. The
/// two inclusive bounds live in the ruleset's <c>economy</c> block
/// (<see cref="Model.EconomyRules.TaxRateMinPercent"/>/<see cref="Model.EconomyRules.TaxRateMaxPercent"/>)
/// and the handler reads them from there, never from a C# literal. The dialog's 1-per-arrow and
/// 5-per-Page keyboard steps belong to the dialog (T109); the command takes any integer in range.
/// </para>
/// <para>
/// It prints no news line: no report shows one for a tax change, so the handler publishes no event.
/// </para>
/// </remarks>
/// <param name="IssuingNationId">The nation whose rate is set — the active seat, checked by the dispatcher.</param>
/// <param name="TaxRatePercent">The new whole-percent rate, already an integer; range-checked in the handler.</param>
public sealed record SetTaxRateCommand(string IssuingNationId, int TaxRatePercent) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "economy.set-tax";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class SetTaxRateRejections
{
    /// <summary>The requested rate falls outside the ruleset's inclusive <c>economy</c> bounds.</summary>
    public static readonly RejectionCode OutOfRange = new("economy.tax-rate-out-of-range");
}
