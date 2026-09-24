using System.Text.RegularExpressions;
using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Battle;

/// <summary>
/// The two properties every Done-when line above rests on: the resolver is reproducible under a fixed
/// seed, and it draws from <see cref="IRng"/> alone — plus the hazard guard that keeps the reserve
/// tactical research out of this diff.
/// </summary>
public class BattleDeterminismTests
{
    private const string Attacker = "north-attacker";
    private const string Defender = "south-defender";

    /// <summary>The same seed resolves the same battle identically, result and state alike.</summary>
    [Fact]
    public void TheSameSeedProducesTheSameBattleTwice()
    {
        var first = InstantBattleResolver.ResolveField(
            FieldBattleTests.Fixture(), Attacker, Defender, BattleTestbed.Scatter, BattleTestbed.World,
            new SplitMix64Rng(BattleTestbed.Seed), NullEventSink.Instance);

        var second = InstantBattleResolver.ResolveField(
            FieldBattleTests.Fixture(), Attacker, Defender, BattleTestbed.Scatter, BattleTestbed.World,
            new SplitMix64Rng(BattleTestbed.Seed), NullEventSink.Instance);

        Assert.Equal(first.Result, second.Result);
        Assert.Equal(GameJson.Serialize(first.State), GameJson.Serialize(second.State));
    }

    /// <summary>
    /// The companion: a different seed moves the seeded outcomes, so the reproducibility above is the
    /// generator doing its job rather than the resolver having no randomness in it at all.
    /// </summary>
    [Fact]
    public void ADifferentSeedMovesTheSeededOutcomes()
    {
        var promotionPatterns = new List<string>();
        var distances = new List<int>();

        for (ulong seed = 1; seed <= 24; seed++)
        {
            var (_, result) = InstantBattleResolver.ResolveField(
                FieldBattleTests.Fixture(), Attacker, Defender, BattleTestbed.Scatter, BattleTestbed.World,
                new SplitMix64Rng(seed), NullEventSink.Instance);

            promotionPatterns.Add(string.Concat(result.Promotions.Select(p => p.PromotedByRoll ? "1" : "0")));
            if (result.Scatter is { } scatter)
            {
                distances.Add(scatter.RequestedDistance);
            }
        }

        Assert.True(promotionPatterns.Distinct().Count() > 1, "the promotion roll must depend on the seed");
        Assert.True(distances.Distinct().Count() > 1, "the scatter distance must depend on the seed");
    }

    /// <summary>
    /// Both rulesets consume the same draws for everything the original itself does, which is what makes
    /// "unaffected by <c>combat.onDefeat</c>" true of the winner's side of the battle rather than
    /// coincidentally true of one fixture.
    /// </summary>
    [Fact]
    public void TheTwoRulesetsAgreeOnEverythingTheOriginalDoes()
    {
        var (_, destroyed) = InstantBattleResolver.ResolveField(
            FieldBattleTests.Fixture(), Attacker, Defender, BattleTestbed.Destroyed, BattleTestbed.World,
            BattleTestbed.BattleRng(), NullEventSink.Instance);

        var (_, scattered) = InstantBattleResolver.ResolveField(
            FieldBattleTests.Fixture(), Attacker, Defender, BattleTestbed.Scatter, BattleTestbed.World,
            BattleTestbed.BattleRng(), NullEventSink.Instance);

        Assert.Equal(destroyed.AttackerPower, scattered.AttackerPower);
        Assert.Equal(destroyed.DefenderPower, scattered.DefenderPower);
        Assert.Equal(destroyed.Winner, scattered.Winner);
        Assert.Equal(destroyed.WinnerCasualties, scattered.WinnerCasualties);
        Assert.Equal(destroyed.UnitCasualties, scattered.UnitCasualties);
        Assert.Equal(destroyed.Promotions, scattered.Promotions);
        Assert.Equal(destroyed.AbsorbedMoney, scattered.AbsorbedMoney);
        Assert.Equal(destroyed.AbsorbedSupplyTons, scattered.AbsorbedSupplyTons);
        Assert.Equal(destroyed.WinnerUnityDelta, scattered.WinnerUnityDelta);
        Assert.Equal(destroyed.LoserUnityDelta, scattered.LoserUnityDelta);
        Assert.Equal(destroyed.PeaceTreatyFired, scattered.PeaceTreatyFired);

        // Only the loser's fate differs.
        Assert.NotEqual(destroyed.LoserFate, scattered.LoserFate);
    }

    /// <summary>
    /// The hazard guard (<c>docs/task-catalogue.md</c> T16 Hazards): none of the reserve tactical
    /// research appears in this namespace's <em>code</em>. Comments are stripped first, because the files
    /// deliberately name these things in prose to record that they are absent — which is the opposite of
    /// using them, and must not trip the check.
    /// </summary>
    [Fact]
    public void NoReserveTacticalResearchAppearsInTheBattleNamespacesCode()
    {
        string[] reserved =
        {
            // The reserve's whole ruleset surface. StandardBattalionSize used to belong on this list
            // too -- before T63 it was read only by the reserve tactical rout mechanic's own threshold
            // (size / 25, still unimplemented, still reserve). It is REMOVED here, not merely
            // grandfathered: BattleCasualties.DeleteBelowThreshold now reads it for a different, SHIPPED
            // rule -- the small-unit deletion pass's own threshold (size / 10 national, size / 5
            // mercenary, bug #289, confirmed at FUN_0044AE20's second pass) -- so the field is no longer
            // exclusive to the reserve and the guard would false-positive on confirmed, shipped code.
            "DetailedResolver",
            "TypeEffectiveness",
            "MeleeLossCap",
            "MeleeBasePowerFloor",
            "MeleePowerDivisor",
            "InRangeShotMultiplier",
        };

        // Battle/Candidates/ is exempt (T59, granted narrowly by the user, main 8d3d298): the auto-resolve
        // candidates there ARE the reserve research, implemented as measurement code behind a seam the user
        // gates, and the shipped resolver never calls them. The companion test below asserts that no file in
        // the engine outside Candidates/ references them, so the research cannot reach the shipped path.
        var battleSources = Directory.GetFiles(
                Path.Combine(TestPaths.RepositoryRoot, "src", "IC2.Engine", "Battle"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsUnderCandidates(file))
            .ToArray();
        Assert.NotEmpty(battleSources);

        var offenders = new List<string>();
        foreach (var file in battleSources)
        {
            var code = StripComments(File.ReadAllText(file));
            foreach (var name in reserved)
            {
                if (code.Contains(name, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {name}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "The shipped instant resolver must not consume the reserve tactical research: "
            + string.Join(", ", offenders)
            + " (Battle/Candidates/ alone is exempt: T59's candidates are measurement code, gated by the user, "
            + "and never called by the shipped resolver.)");
    }

    /// <summary>
    /// The other half of the <c>Battle/Candidates/</c> exemption above (T59, main 8d3d298): no production
    /// source outside <c>src/IC2.Engine/Battle/Candidates/</c> may reference the candidates' namespace or any
    /// of their types, so the reserve research the candidates implement cannot leak into the shipped path.
    /// "Production" is every source tree that builds against the engine: all of <c>src/</c> (the engine, the
    /// CLI, the data and inspection tools) and <c>godot/</c> (the map viewer). Build output is skipped.
    /// </summary>
    [Fact]
    public void NoProductionCodeOutsideCandidatesReferencesTheCandidates()
    {
        const string CandidatesNamespace = "IC2.Engine.Battle.Candidates";
        var candidateTypes = typeof(InstantBattleResolver).Assembly.GetTypes()
            .Where(t => t.Namespace is { } ns
                        && (ns == CandidatesNamespace || ns.StartsWith(CandidatesNamespace + ".", StringComparison.Ordinal))
                        && !t.IsNested
                        && !t.Name.Contains('<', StringComparison.Ordinal))
            .Select(t => t.Name.Split('`')[0])
            .ToList();
        Assert.NotEmpty(candidateTypes);

        // "Candidates" catches every way to name the namespace: a using, a qualified name, a namespace
        // declaration. The type names catch anything else.
        var tokens = candidateTypes.Append("Candidates").Distinct(StringComparer.Ordinal).ToList();
        var productionSources = new[] { "src", "godot" }
            .Select(root => Path.Combine(TestPaths.RepositoryRoot, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !IsUnderCandidates(file) && !IsBuildOutput(file) && !IsKnownTestScratch(file))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();
        Assert.Contains(productionSources, f => f.Contains($"{Path.DirectorySeparatorChar}IC2.Engine{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        Assert.Contains(productionSources, f => f.Contains($"{Path.DirectorySeparatorChar}IC2.Cli{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var offenders = new List<string>();
        foreach (var file in productionSources)
        {
            var code = StripComments(File.ReadAllText(file));
            foreach (var token in tokens)
            {
                if (Regex.IsMatch(code, $@"\b{Regex.Escape(token)}\b"))
                {
                    offenders.Add($"{Path.GetRelativePath(TestPaths.RepositoryRoot, file)}: {token}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Only src/IC2.Engine/Battle/Candidates/ may use the auto-resolve candidates (T59 measurement code, "
            + "gated by the user); no production source under src/ or godot/ may reference them: " + string.Join(", ", offenders));
    }

    private static bool IsUnderCandidates(string file)
    {
        var candidates = Path.Combine(TestPaths.RepositoryRoot, "src", "IC2.Engine", "Battle", "Candidates") + Path.DirectorySeparatorChar;
        return Path.GetFullPath(file).StartsWith(Path.GetFullPath(candidates), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBuildOutput(string file)
    {
        var relative = Path.GetRelativePath(TestPaths.RepositoryRoot, file);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj" or ".godot");
    }

    /// <summary>
    /// The one exact path a test elsewhere in the suite deliberately writes real, valid <c>.cs</c>
    /// content into and then deletes, unrelated to whether that content references the candidates.
    /// Anchored to the writer's own relative path -- not merely a directory name that could recur
    /// anywhere under <c>src/</c> or <c>godot/</c> -- and named one entry per writer, so a future,
    /// unnamed writer (at this path or a different one) still fails this guard until it too is named
    /// here (review round 1, N2: a probe file at a different path with a matching directory name was
    /// not reported by the name-only version of this check).
    /// </summary>
    /// <remarks>
    /// <c>tests/IC2.Engine.Tests/Core/Determinism/DeterminismGuardTests.cs</c>'s
    /// <c>The_guard_fails_on_a_deliberately_added_new_Random_under_src_engine</c> writes a scratch
    /// <c>DeliberateNondeterminism.cs</c> under
    /// <c>src/IC2.Engine/Core/__determinism-guard-scratch__/</c> to prove the determinism scanner
    /// fires, then removes it in a <c>finally</c>. That file is outside this task's (T74's) Owns
    /// list, so it is reported rather than patched (bug #320,
    /// docs/build-process.md#46-bugs-and-follow-ups): xUnit runs distinct test classes in parallel by
    /// default, and that class is in a different collection from this one
    /// (<c>RepositorySourcesCollection</c> only pairs it against
    /// <c>SystemRegistrationTests</c>, which does the identical <c>src/**/*.cs</c> enumerate-then-read
    /// this guard does -- see that collection's own remarks in
    /// <c>tests/IC2.Engine.Tests/Core/CoreTestbed.cs</c>). This guard's own scan was never a member of
    /// that collection, so a run that enumerated the scratch file a moment before the writer's
    /// <c>finally</c> deleted it, then tried to read it, threw an unrelated
    /// <c>FileNotFoundException</c> -- a single, non-reproducing failure of exactly this guard,
    /// reported on #320 (T64's first CI run). Skipping the path by name removes the race without
    /// weakening what the guard actually checks: the skipped directory holds nothing but that one
    /// scratch file, never a real reference to the candidates.
    /// </remarks>
    private static bool IsKnownTestScratch(string file)
    {
        var relative = Path.GetRelativePath(TestPaths.RepositoryRoot, file);
        var scratchDirectory = Path.Combine("src", "IC2.Engine", "Core", "__determinism-guard-scratch__")
            + Path.DirectorySeparatorChar;
        return relative.StartsWith(scratchDirectory, StringComparison.Ordinal);
    }

    /// <summary>The guard's own regression case: it does see a reserve name when one is really in code.</summary>
    [Fact]
    public void TheReserveGuardDetectsARealUse()
    {
        const string Sample = """
            /// <summary>TypeEffectiveness is deliberately absent.</summary>
            public static int Broken(Ruleset r) => r.Combat.DetailedResolver.MeleeLossCapPercent;
            """;

        var code = StripComments(Sample);
        Assert.DoesNotContain("TypeEffectiveness", code, StringComparison.Ordinal);
        Assert.Contains("DetailedResolver", code, StringComparison.Ordinal);
    }

    private static string StripComments(string source)
    {
        var withoutBlock = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(withoutBlock, @"//.*?$", string.Empty, RegexOptions.Multiline);
    }
}
