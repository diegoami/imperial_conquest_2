using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// Fix #519's reproduction: the city panel's recruit troop box carried one invented range
/// (<c>MinValue = 10, MaxValue = 2000, Step = 10, Value = 200</c>) for every unit type. The box's
/// bounds are now <see cref="RecruitTroopBounds.For"/>, read from the ruleset's own
/// <see cref="UnitTypeRules.StandardBattalionSize"/>.
/// </summary>
/// <remarks>
/// The box itself is a Godot <c>SpinBox</c>, which the Godot-free test project cannot instantiate, so
/// the assertion is on the same <see cref="RecruitTroopBounds"/> value the panel copies into the box's
/// <c>MinValue</c>/<c>MaxValue</c>/<c>Value</c>/<c>Step</c> — the seam
/// <c>godot/UI/RecruitmentPanelViewModel.cs</c> already established. Every type is read from the
/// shipped <c>classical-faithful</c> ruleset rather than hard-coded, so a ruleset change moves the
/// expectation with it.
/// </remarks>
public sealed class RecruitTroopBoundsTests
{
    private static Ruleset ClassicalFaithful() =>
        GameDataRepository.Load(ModelTestPaths.DataRoot).RulesetById("classical-faithful")!;

    [Fact]
    public void Every_unit_type_gets_its_battalion_as_the_maximum_and_a_fifth_as_minimum_and_default()
    {
        var ruleset = ClassicalFaithful();
        Assert.NotEmpty(ruleset.UnitTypes);

        foreach (var type in ruleset.UnitTypes)
        {
            var bounds = RecruitTroopBounds.For(ruleset, type.Id);

            Assert.Equal(type.StandardBattalionSize, bounds.Maximum);
            Assert.Equal(type.StandardBattalionSize / 5, bounds.Minimum);
            Assert.Equal(type.StandardBattalionSize / 5, bounds.DefaultValue);
            Assert.Equal(RecruitTroopBounds.StepSize, bounds.Step);
            Assert.Equal(RecruitTroopBounds.PageStepSize, bounds.PageStep);
        }
    }

    [Fact]
    public void Switching_the_unit_type_changes_the_bounds()
    {
        var ruleset = ClassicalFaithful();

        var lightInfantry = RecruitTroopBounds.For(ruleset, "light_infantry");
        var archers = RecruitTroopBounds.For(ruleset, "archers");

        Assert.Equal(15000, lightInfantry.Maximum);
        Assert.Equal(3000, lightInfantry.Minimum);
        Assert.Equal(3000, lightInfantry.DefaultValue);
        Assert.Equal(3500, archers.Maximum);
        Assert.Equal(700, archers.Minimum);
        Assert.Equal(700, archers.DefaultValue);
        Assert.NotEqual(lightInfantry, archers);
    }
}
