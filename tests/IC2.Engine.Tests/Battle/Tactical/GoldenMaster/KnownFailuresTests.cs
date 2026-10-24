using Xunit;
using Xunit.Sdk;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

public sealed class KnownFailuresTests
{
    private const string Listed = "[{\"Case\":\"case\",\"FirstDifference\":\"slot 0, target: port -1, original 0\",\"Issue\":1103}]";

    [Fact]
    public void Exact_discrepancy_is_required_and_fixed_cases_fail_until_deleted()
    {
        var registry = new KnownFailureRegistry(Listed);
        registry.Check("case", () => KnownFailures.Require(false, "slot 0, target: port -1, original 0"));
        Assert.ThrowsAny<XunitException>(() => registry.Check("case", () => { }));
        Assert.ThrowsAny<XunitException>(() => registry.Check("case", () => KnownFailures.Require(false, "slot 0, target: port -2, original 0")));
        new KnownFailureRegistry("[]").Check("case", () => { });
        Assert.ThrowsAny<XunitException>(() => new KnownFailureRegistry("[]").Check("case", () => KnownFailures.Require(false, "new difference")));
    }

    [Fact]
    public void Fixture_and_unrelated_exceptions_are_never_expected_failures()
    {
        var registry = new KnownFailureRegistry(Listed);
        Assert.Throws<InvalidDataException>(() => registry.Check("case", () => throw new InvalidDataException("bad fixture")));
        Assert.Throws<InvalidOperationException>(() => registry.Check("case", () => throw new InvalidOperationException("bad replay")));
        Assert.Throws<FailException>(() => registry.Check("case", () => Assert.Fail("unrelated assertion")));
    }

    [Fact]
    public void Duplicate_malformed_and_unused_entries_fail()
    {
        Assert.Throws<InvalidDataException>(() => new KnownFailureRegistry(Listed[..^1] + "," + Listed[1..]));
        Assert.Throws<InvalidDataException>(() => new KnownFailureRegistry(Listed.Replace("1103", "0")));
        Assert.Throws<InvalidDataException>(() => new KnownFailureRegistry(Listed.Replace("\"Case\":\"case\"", "\"Case\":\"\"")));
        Assert.Throws<InvalidDataException>(() => new KnownFailureRegistry(Listed.Replace("\"Issue\":1103", "\"Issue\":1103,\"Issue\":1103")));
        Assert.Throws<InvalidDataException>(() => new KnownFailureRegistry(Listed.Replace("\"Issue\":1103", "\"Unexpected\":1103")));
        Assert.ThrowsAny<XunitException>(() => new KnownFailureRegistry(Listed).ValidateCases(new[] { "another case" }));
        new KnownFailureRegistry(Listed).ValidateCases(new[] { "case" });
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("[{\"Case\":\"case\",\"FirstDifference\":\"difference\",\"Issue\":\"1103\"}]")]
    [InlineData("[{\"Case\":\"case\",\"FirstDifference\":\"difference\",\"Issue\":1103.5}]")]
    [InlineData("[{\"Case\":\"case\",\"FirstDifference\":\"difference\",\"Issue\":2147483648}]")]
    [InlineData("[{\"Case\":7,\"FirstDifference\":\"difference\",\"Issue\":1103}]")]
    public void Invalid_JSON_syntax_and_field_types_are_fixture_errors(string json) =>
        Assert.Throws<InvalidDataException>(() => new KnownFailureRegistry(json));

    [Fact]
    public void Followup_1377_has_no_remaining_expected_failures()
    {
        Assert.DoesNotContain(KnownFailures.Loaded.Entries, entry => entry.Issue == 1377);
        Assert.Throws<InvalidDataException>(() => new KnownFailureRegistry(
            "[{\"Case\":\"other\",\"FirstDifference\":\"difference\",\"Issue\":1377}]"));
    }

    [SkippableFact]
    public void Every_entry_names_a_current_comparison_case()
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);
        Assert.Equal(318, SeriesCatalogue.HookedTrials().Count);
        KnownFailures.Loaded.ValidateCases(KnownFailures.Cases());
    }
}
