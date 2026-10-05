using IC2.Engine.Model;

namespace IC2.Slice.UI;

/// <summary>
/// The city panel's recruit troop box range for one unit type, computed from the ruleset's own
/// <see cref="UnitTypeRules.StandardBattalionSize"/> rather than a literal.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Fix #519.</strong> The box used to be one invented
/// <c>MinValue = 10, MaxValue = 2000, Step = 10, Value = 200</c> for every unit type, with no
/// provenance. The original's dialog sizes each type to its standard battalion
/// (<c>unitTypes[].standardBattalionSize</c>, confirmed from DAT <c>+0x1A</c>), so the box's maximum
/// is that battalion and its minimum and default are a fifth of it. Switching the type re-applies
/// the new type's bounds.
/// </para>
/// <para>
/// Deliberately <strong>Godot-free</strong> (no <c>using Godot</c>), so a plain xunit test in
/// <c>tests/IC2.Engine.Tests/Ui/</c> can pin every type's bounds against the shipped
/// <c>classical-faithful</c> ruleset — the same seam <c>godot/UI/RecruitmentPanelViewModel.cs</c>
/// already established.
/// </para>
/// </remarks>
/// <param name="Minimum">The box's <c>MinValue</c>: one fifth of the battalion, rounded down.</param>
/// <param name="Maximum">The box's <c>MaxValue</c>: the type's standard battalion size.</param>
/// <param name="DefaultValue">The box's starting <c>Value</c>: the same fifth as <see cref="Minimum"/>.</param>
/// <param name="Step">The box's arrow <c>Step</c>: 100, the original dialog's arrow step.</param>
/// <param name="PageStep">How far a page key moves the value: 1,000, the original dialog's page step.</param>
public readonly record struct RecruitTroopBounds(
    int Minimum,
    int Maximum,
    int DefaultValue,
    int Step,
    int PageStep)
{
    /// <summary>The original dialog's arrow step.</summary>
    public const int StepSize = 100;

    /// <summary>How far the original dialog's page keys move the value.</summary>
    public const int PageStepSize = 1000;

    /// <summary>The divisor turning a battalion size into the box's minimum and default.</summary>
    public const int MinimumDivisor = 5;

    /// <summary>
    /// The box's bounds for <paramref name="unitTypeId"/>, read from <paramref name="ruleset"/>'s own
    /// <see cref="UnitTypeRules.StandardBattalionSize"/>. The value returned for one type is the whole
    /// answer the panel needs, so a type switch is just a second call.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="ruleset"/> has no unit type
    /// <paramref name="unitTypeId"/>.</exception>
    public static RecruitTroopBounds For(Ruleset ruleset, string unitTypeId)
    {
        ArgumentNullException.ThrowIfNull(ruleset);

        var type = ruleset.UnitTypeById(unitTypeId)
                   ?? throw new ArgumentException(
                       $"The ruleset '{ruleset.Id}' has no unit type '{unitTypeId}'.", nameof(unitTypeId));

        var fifth = type.StandardBattalionSize / MinimumDivisor;
        return new RecruitTroopBounds(fifth, type.StandardBattalionSize, fifth, StepSize, PageStepSize);
    }
}
