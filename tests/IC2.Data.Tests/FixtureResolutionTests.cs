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
/// - nothing configured at all (worktree, untouched machine — <see cref="LocalAssets.WasRequested"/>
///   <c>== false</c>): PASSES, and states in the test output how many other tests are sitting skipped
///   and why, so "N skipped" is never mistaken for benign.
/// - anything configured, OR configured but unusable (<see cref="LocalAssets.IsConfigured"/>
///   <c>== true</c>, or <see cref="LocalAssets.WasRequested"/> <c>== true</c> while
///   <c>IsConfigured == false</c> — T53 review round 1, finding B1: those are not the same as "nothing
///   configured" and must not take the quiet path above): every one of the twelve named fixtures this
///   project resolves by name, plus the DAT, must resolve via <see cref="FixtureResolver"/> — a stale
///   path, a fixture that quietly stopped existing, or a fixtures source that is present but broken
///   (wrong layout, missing DAT, bad directory) fails here, loudly, naming every fixture it could not
///   find, instead of vanishing back into "just another skip" the way issue #203's seven failures did.
/// </summary>
public class FixtureResolutionTests
{
    /// <summary>The twelve saves this project's non-sweep, real-data tests reference by literal name
    /// (every <c>.sav</c> name hardcoded anywhere outside the corpus-sweep family, which reads whatever
    /// corpus is configured by directory rather than naming files up front — see
    /// <c>CorpusFixtures/CorpusSweepTests</c>). This list is deliberately narrower than what
    /// <c>ic2-test-fixtures</c> itself now ships: that repository holds the WHOLE corpus (issue #207),
    /// not just these twelve, because the sweep family needs the rest. Keeping this list at exactly
    /// twelve is correct — it is what "the twelve named fixtures" in Done-when line 2 refers to — but
    /// nothing here should be read as a claim about the repository's own contents.</summary>
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
        // N4: runs unconditionally, before either branch below, so replacing NamedFixtures with an
        // empty (or otherwise wrong-sized) list can never leave this check vacuously green — the one
        // check in the project whose entire point is that it cannot be emptied without anything
        // noticing.
        Assert.Equal(12, NamedFixtures.Count);

        // B1 (T53 review round 1): LocalAssets.IsConfigured alone cannot distinguish "nothing
        // configured at all" from "a source was configured but is unusable" — both read false. Only
        // WasRequested tells them apart. Taking the quiet path below on the latter is exactly issue
        // #203's shape one level up: a fixtures source that is present but broken must not look like an
        // untouched machine.
        if (!LocalAssets.IsConfigured && !LocalAssets.WasRequested)
        {
            var skippableMethodCount = CountSkippableTestMethods();
            var message =
                $"Not configured for asset-dependent tests: {LocalAssets.SkipReason} " +
                $"— {skippableMethodCount} [SkippableFact]/[SkippableTheory] test methods in this " +
                "project (some expanding into several per-case results, e.g. the corpus sweep) are " +
                "reporting Skipped, not run. This is expected here and is not a passing result for " +
                "any of them: configure assets.local.ini locally, or set IC2_FIXTURES_DIR, to actually " +
                "exercise them.";
            _output.WriteLine(message);
            // N3: ITestOutputHelper's own capture is only surfaced by `dotnet test` at detailed
            // verbosity, so CI's default `dotnet test IC2.sln --no-build --configuration Release` would
            // otherwise never show this. Console output is captured the same run regardless of logger
            // verbosity, so the plain-statement requirement (Done-when line 2) is actually visible where
            // it matters — in a CI log, not only in a local `--logger console;verbosity=detailed` run.
            Console.WriteLine(message);
            return;
        }

        var checkedNames = NamedFixtures.Append(FixtureResolver.DatFileName).ToList();
        var missing = checkedNames.Where(name => FixtureResolver.TryResolve(name) is null).ToList();

        var source = LocalAssets.IsCiFixtureMode ? "IC2_FIXTURES_DIR" : "assets.local.ini";
        // N9: names the actual configured root (or, when the source is present but never loaded to a
        // directory at all, the reason why) rather than only describing which kinds of folders were
        // tried — a developer or a CI log reader debugging a failure here needs the real path.
        var root = LocalAssets.Settings?.DirectoryPath ?? $"(none — {LocalAssets.SkipReason})";
        Assert.True(missing.Count == 0,
            $"Configured via {source} (root: {root}), but {missing.Count} of {checkedNames.Count} " +
            "fixtures could not be resolved (searched saves-processed/, saves/ and releases/*/ under " +
            $"that root, by name): {string.Join(", ", missing)}. A stale path, a fixture that moved or " +
            "vanished, or a fixtures source that is present but unusable must fail here, not silently " +
            "subtract tests from the run.");
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
