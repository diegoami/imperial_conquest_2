using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Core.Determinism;

/// <summary>
/// Definition of Done item 4: "The determinism guard test fails when a deliberately-added
/// <c>new Random()</c> is present in a scratch file under <c>src/IC2.Engine</c> (test proves the guard
/// works, then removes it)."
/// </summary>
/// <remarks>
/// The standing guard — <see cref="The_engine_contains_no_nondeterministic_construct"/> — is the one that
/// every later task inherits: it scans the whole of <c>src/IC2.Engine</c>, so a system merged by T08 or
/// T16 is covered without that task doing anything. The rest of this class tests the guard itself, on the
/// principle that a green check nobody has ever seen go red is not evidence of anything.
/// </remarks>
public class DeterminismGuardTests
{
    private static string EngineRoot => Path.Combine(ModelTestPaths.RepositoryRoot, "src", "IC2.Engine");

    /// <summary>
    /// Where the deliberate violation is written. Named so that a file left behind by a killed test run
    /// is unmistakable, and placed inside this task's own Owns list.
    /// </summary>
    private static string ScratchDirectory =>
        Path.Combine(EngineRoot, "Core", "__determinism-guard-scratch__");

    [Fact]
    public void The_engine_contains_no_nondeterministic_construct()
    {
        // Belt and braces: if an earlier run of the scratch test was killed between writing the file and
        // its finally block, this test would fail for the wrong reason. Clean up first, then assert.
        RemoveScratch();

        var violations = DeterminismScanner.Scan(EngineRoot, ModelTestPaths.RepositoryRoot);

        Assert.True(
            violations.Count == 0,
            "src/IC2.Engine must contain no nondeterministic construct. Found:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void No_line_in_the_engine_suppresses_the_ordering_rule()
    {
        // The suppression exists so that a later task with a provably order-independent enumeration is
        // not forced into contortions. Today nothing uses it, and this test makes any future use visible
        // in a diff rather than silent.
        var suppressions = DeterminismScanner.Suppressions(EngineRoot, ModelTestPaths.RepositoryRoot);

        Assert.True(
            suppressions.Count == 0,
            "The engine now suppresses the ordering rule somewhere. Each suppression needs a reason a "
            + "reviewer agrees with:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, suppressions));
    }

    [Fact]
    public void The_guard_fails_on_a_deliberately_added_new_Random_under_src_engine()
    {
        var scratchFile = Path.Combine(ScratchDirectory, "DeliberateNondeterminism.cs");

        try
        {
            Directory.CreateDirectory(ScratchDirectory);
            File.WriteAllText(scratchFile, DeliberatelyNondeterministicSource());

            var violations = DeterminismScanner.Scan(EngineRoot, ModelTestPaths.RepositoryRoot);

            Assert.Contains(violations, violation =>
                violation.File.EndsWith("DeliberateNondeterminism.cs", StringComparison.Ordinal)
                && violation.Rule == "System.Random");

            // Every other banned API in the same scratch file is caught too, so the guard is not passing
            // on one pattern and quietly missing the rest.
            var rules = violations
                .Where(v => v.File.EndsWith("DeliberateNondeterminism.cs", StringComparison.Ordinal))
                .Select(v => v.Rule)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            Assert.Contains("wall clock", rules);
            Assert.Contains("Guid.NewGuid", rules);
            Assert.Contains("Environment.TickCount", rules);
            Assert.Contains("unordered enumeration", rules);
        }
        finally
        {
            RemoveScratch();
        }

        // Removed, and the guard is green again — the second half of the DoD line.
        Assert.Empty(DeterminismScanner.Scan(EngineRoot, ModelTestPaths.RepositoryRoot));
        Assert.False(Directory.Exists(ScratchDirectory));
    }

    [Fact]
    public void A_doc_comment_that_mentions_a_banned_api_is_not_a_violation()
    {
        // This is not hypothetical: T02's SaveGame.cs documents that game time advances by the calendar
        // "never by DateTime.Now". A guard that failed on its own documentation would be switched off.
        const string source = """
            /// <summary>Advances by the calendar, never by <c>DateTime.Now</c>.</summary>
            // Guid.NewGuid() is banned here.
            public sealed class Clock
            {
                public string Describe() => "not new Random() either";
            }
            """;

        Assert.Empty(DeterminismScanner.ScanText(source, "Fake.cs"));
    }

    [Fact]
    public void An_ordered_collection_is_not_a_violation()
    {
        const string source = """
            public sealed class Ordered
            {
                private readonly SortedDictionary<string, int> _byName = new();
                private readonly List<int> _values = new();

                public int Total()
                {
                    var total = 0;
                    foreach (var value in _byName.Values)
                    {
                        total += value;
                    }

                    foreach (var value in _values)
                    {
                        total += value;
                    }

                    return total;
                }
            }
            """;

        Assert.Empty(DeterminismScanner.ScanText(source, "Fake.cs"));
    }

    [Fact]
    public void A_membership_test_against_a_hash_set_is_not_a_violation()
    {
        // T02's ValueListLookup does exactly this. Adding to a set and asking whether it contains
        // something never depends on order; only enumerating it does.
        const string source = """
            public sealed class Lookup
            {
                public bool HasDistinct(IReadOnlyList<string> ids)
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var id in ids)
                    {
                        if (!seen.Add(id))
                        {
                            return false;
                        }
                    }

                    return true;
                }
            }
            """;

        Assert.Empty(DeterminismScanner.ScanText(source, "Fake.cs"));
    }

    [Fact]
    public void Enumerating_a_dictionary_is_a_violation()
    {
        const string source = """
            public sealed class Unordered
            {
                private readonly Dictionary<string, int> _byName = new();

                public int Total()
                {
                    var total = 0;
                    foreach (var entry in _byName)
                    {
                        total += entry.Value;
                    }

                    return total;
                }
            }
            """;

        var violations = DeterminismScanner.ScanText(source, "Fake.cs");
        Assert.Contains(violations, violation => violation.Rule == "unordered enumeration");
    }

    [Fact]
    public void A_reasoned_suppression_silences_the_ordering_rule_and_nothing_else()
    {
        const string suppressed = """
            public sealed class Unordered
            {
                private readonly Dictionary<string, int> _byName = new();

                public int Total()
                {
                    var total = 0;
                    // determinism-ok: summing integers is order-independent.
                    foreach (var entry in _byName)
                    {
                        total += entry.Value;
                    }

                    return total;
                }
            }
            """;

        Assert.Empty(DeterminismScanner.ScanText(suppressed, "Fake.cs"));

        // A banned API is never suppressible: there is no order-independence argument for a wall clock.
        const string notSuppressible = """
            public sealed class Clocked
            {
                // determinism-ok: I promise it is fine.
                public long Now() => Environment.TickCount64;
            }
            """;

        Assert.NotEmpty(DeterminismScanner.ScanText(notSuppressible, "Fake.cs"));
    }

    [Fact]
    public void The_scanner_reports_the_line_it_found_a_violation_on()
    {
        const string source = """
            public sealed class Offender
            {
                public int Roll() => new Random().Next();
            }
            """;

        var violation = Assert.Single(DeterminismScanner.ScanText(source, "Fake.cs"));
        Assert.Equal(3, violation.Line);
        Assert.Equal("Fake.cs", violation.File);
        Assert.Contains("new Random()", violation.Text, StringComparison.Ordinal);
    }

    private static void RemoveScratch()
    {
        if (Directory.Exists(ScratchDirectory))
        {
            Directory.Delete(ScratchDirectory, recursive: true);
        }
    }

    /// <summary>
    /// The scratch file's contents, built at run time rather than committed, so the repository never
    /// holds a <c>.cs</c> file under <c>src/</c> that would break the build if the cleanup were skipped.
    /// </summary>
    private static string DeliberatelyNondeterministicSource() =>
        string.Join(Environment.NewLine, new[]
        {
            "namespace IC2.Engine.Core.Scratch;",
            string.Empty,
            "internal static class DeliberateNondeterminism",
            "{",
            "    internal static int Roll() => new Random().Next(6);",
            string.Empty,
            "    internal static long Stamp() => DateTime.UtcNow.Ticks + Environment.TickCount64;",
            string.Empty,
            "    internal static string Id() => Guid.NewGuid().ToString();",
            string.Empty,
            "    internal static int Total(Dictionary<string, int> byName)",
            "    {",
            "        var total = 0;",
            "        foreach (var entry in byName)",
            "        {",
            "            total += entry.Value;",
            "        }",
            string.Empty,
            "        return total;",
            "    }",
            "}",
        });
}
