using System.Text.Json.Nodes;
using IC2.Engine.Persistence;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Persistence;

/// <summary>
/// T71 (#346): every save format version from <see cref="SaveFormat.MinimumSupportedVersion"/> up to
/// <see cref="SaveFormat.CurrentVersion"/> minus one has a migration step, so bumping
/// <see cref="SaveFormat.CurrentVersion"/> without adding one fails here, and a missing step is reported
/// as such -- never as "written by a newer build".
/// </summary>
public sealed class SaveMigrationStepCoverageTests
{
    public static TheoryData<int> VersionsThatNeedAStep()
    {
        var data = new TheoryData<int>();
        for (var v = SaveFormat.MinimumSupportedVersion; v < SaveFormat.CurrentVersion; v++)
        {
            data.Add(v);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(VersionsThatNeedAStep))]
    public void Every_version_below_the_current_one_has_a_migration_step(int version)
    {
        // An empty envelope is not a well-formed save, so the step itself may reject it as malformed.
        // That is fine: the only failure this test is about is the fall-through arm, which has no step.
        var toy = PersistenceTestbed.Toy;
        var envelope = new JsonObject { [SaveFormat.VersionField] = version, [SaveFormat.PayloadField] = new JsonObject() };
        var thrown = Record.Exception(
            () => SaveManager.Load("probe.json", envelope.ToJsonString(), toy.World, toy.Ruleset));

        Assert.False(
            thrown is MissingMigrationStepException,
            $"save format version {version} has no migration step: {thrown?.Message}");
    }

    [Fact]
    public void A_version_inside_the_supported_range_with_no_step_names_the_missing_step()
    {
        // The stepping loop reaches the fall-through arm for a version it has no step for. Reproduced
        // here with a loop-visible version that no step handles: one past the current version cannot
        // be reached through MigrateToCurrent (it stops at CurrentVersion), so the message is
        // asserted on the exception itself, which is what the fall-through arm throws.
        var error = new MissingMigrationStepException("old.ic2save.json", 2, 3);

        Assert.Equal(2, error.MissingFromVersion);
        Assert.Contains("no migration step from version 2 to version 3", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("newer build", error.Message, StringComparison.Ordinal);
    }
}
