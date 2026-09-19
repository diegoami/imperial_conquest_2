using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace IC2.Data.Tests;

/// <summary>
/// T53 (issue #204) Done-when line 2 — the one check in this project that is never allowed to skip.
/// Every review this week reported "35 passed / 90 skipped (pre-existing #155)" and moved on, because
/// nothing forced anyone to look past the skip count: the 90 skips were hiding seven real failures
/// (issue #203), and there was no configuration anywhere in the review pipeline where the asset-
/// dependent tests ran at all.
///
/// This is a plain <see cref="FactAttribute"/>, not a <c>[SkippableFact]</c> — it always runs:
/// - nothing configured (worktree, unconfigured machine): PASSES, and states in the test output how
///   many other tests are sitting skipped and why, so "N skipped" is never mistaken for benign.
/// - anything configured (a developer's <c>assets.local.ini</c>, or CI's <c>IC2_FIXTURES_DIR</c>):
///   every one of the twelve named fixtures this project resolves by name, plus the DAT, must resolve
///   via <see cref="FixtureResolver"/> — a stale path or a fixture that quietly stopped existing on
///   this machine fails here, loudly, naming every one it could not find, instead of vanishing back
///   into "just another skip" the way issue #203's seven failures did.
/// </summary>
public class FixtureResolutionTests
{
    /// <summary>The twelve named saves this project resolves by name (ic2-test-fixtures' own README:
    /// "the twelve save files IC2.Data.Tests reads") — every literal <c>.sav</c> name referenced
    /// anywhere in this project outside the corpus-sweep family, which sweeps whatever corpus happens
    /// to be configured rather than naming files up front.</summary>
    public static readonly IReadOnlyList<string> NamedFixtures = new[]
    {
        "11.sav",
        "11_ptol.sav",
        "11_supply.sav",
        "1_cartago_271_spring_5.sav",
        "1_rome_270_summer_7.sav",
        "1_rome_270_winter_11.sav",
        "1_rome_270_winter_7_b.sav",
        "1_rome_270_winter_9.sav",
        "1_rome_270_winter_9_b.sav",
        "1_thracia_271_autumn_1.sav",
        "1_thracia_271_spring_1.sav",
        "1_thracia_271_spring_3.sav",
    };

    private readonly ITestOutputHelper _output;

    public FixtureResolutionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void All_twelve_named_fixtures_and_the_dat_resolve_or_this_fails_loudly_naming_every_miss()
    {
        if (!LocalAssets.IsConfigured)
        {
            var skippableMethodCount = CountSkippableTestMethods();
            _output.WriteLine(
                $"Not configured for asset-dependent tests: {LocalAssets.SkipReason} " +
                $"— {skippableMethodCount} [SkippableFact]/[SkippableTheory] test methods in this " +
                "project (some expanding into several per-case results, e.g. the corpus sweep) are " +
                "reporting Skipped, not run. This is expected here and is not a passing result for " +
                "any of them: configure assets.local.ini locally, or set IC2_FIXTURES_DIR, to actually " +
                "exercise them.");
            return;
        }

        var checkedNames = NamedFixtures.Append(FixtureResolver.DatFileName).ToList();
        var missing = checkedNames.Where(name => FixtureResolver.TryResolve(name) is null).ToList();

        var source = LocalAssets.IsCiFixtureMode ? "IC2_FIXTURES_DIR" : "assets.local.ini";
        Assert.True(missing.Count == 0,
            $"Configured via {source}, but {missing.Count} of {checkedNames.Count} fixtures could not " +
            $"be resolved (searched saves-processed/, saves/ and releases/*/ under the configured " +
            $"directory, by name): {string.Join(", ", missing)}. A stale path or a fixture that moved " +
            "or vanished must fail here, not silently subtract tests from the run.");
    }

    /// <summary>Counts test METHODS decorated <c>[SkippableFact]</c>/<c>[SkippableTheory]</c> in this
    /// assembly — an approximation of "how many tests are skipped" (a <c>[SkippableTheory]</c> method
    /// expands into one result per case at run time, which reflection alone can't enumerate without
    /// invoking the runner), computed rather than hardcoded so it can't quietly drift out of date the
    /// way the 90-skips-are-fine assumption itself did.</summary>
    private static int CountSkippableTestMethods() =>
        typeof(LocalAssets).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Count(m => m.IsDefined(typeof(SkippableFactAttribute), inherit: false)
                        || m.IsDefined(typeof(SkippableTheoryAttribute), inherit: false));
}
