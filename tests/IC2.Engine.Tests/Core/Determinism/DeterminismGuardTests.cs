using IC2.Engine.Tests.Core;
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
/// <para>
/// Follow-up #353: the deliberate-violation scratch file used to be written under
/// <c>src/IC2.Engine/Core/__determinism-guard-scratch__/</c> -- inside the very tree every <c>src/**</c>
/// scanner in the suite reads. This class already joins <see cref="RepositorySourcesCollection"/>, which
/// serialises it against <c>SystemRegistrationTests</c>, but <c>BattleDeterminismTests</c>'
/// <c>NoProductionCodeOutsideCandidatesReferencesTheCandidates</c> -- also a full <c>src/</c>+<c>godot/</c>
/// scan -- is in a different, unnamed xUnit collection and so could run in parallel with this test's write,
/// occasionally reading the scratch file mid-write or racing its deletion (bug #320). Writing it under the
/// process's own temp directory instead means no scanner of the real tree -- present or future, collected
/// or not -- ever has a path into it, which removes the race outright rather than widening the collection
/// to name every scanner that must avoid it. <see cref="The_guard_fails_on_a_deliberately_added_new_Random_under_src_engine"/>
/// proves <see cref="DeterminismScanner.Scan"/> still does its job pointed at that temp directory, which is
/// all the standing guard needs: it always scans the real <see cref="EngineRoot"/>, never the scratch one.
/// </para>
/// </remarks>
[Collection(RepositorySourcesCollection.Name)]
public class DeterminismGuardTests
{
    private static string EngineRoot => Path.Combine(ModelTestPaths.RepositoryRoot, "src", "IC2.Engine");

    /// <summary>
    /// Where the deliberate violation is written: the process's own temp directory, never under
    /// <c>src/IC2.Engine</c> (follow-up #353) -- so no scanner of the real source tree, in this suite or a
    /// future one, can race this test's write or deletion. Named so that a file left behind by a killed
    /// test run is unmistakable.
    /// </summary>
    private static string ScratchDirectory =>
        Path.Combine(Path.GetTempPath(), "__ic2-determinism-guard-scratch__");

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

            // Scans the scratch directory, never EngineRoot (follow-up #353) -- proving the scanner itself
            // still catches every banned construct, without ever putting the violation under src/IC2.Engine
            // where a concurrent scanner of the real tree could see it.
            var violations = DeterminismScanner.Scan(ScratchDirectory, ModelTestPaths.RepositoryRoot);

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

        // Removed, and the guard is green again — the second half of the DoD line. The real tree was never
        // touched, so this is really just confirming EngineRoot was clean throughout.
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

    /// <summary>
    /// Every way of enumerating a dictionary, one case each.
    /// </summary>
    /// <remarks>
    /// The first version of this guard matched only <c>foreach (… in name)</c> with no parenthesis in
    /// between, so both deconstruction spellings escaped it — and <c>foreach (var (id, city) in _byId)</c>
    /// is how most people actually iterate a dictionary. A wave-3 task writing the idiomatic form would
    /// have got a green guard while enumerating in hash order, which is precisely the failure the guard
    /// exists to prevent.
    /// </remarks>
    [Theory]
    [InlineData("foreach (var entry in _byName)")]
    [InlineData("foreach (var (key, value) in _byName)")]
    [InlineData("foreach ((var key, var value) in _byName)")]
    [InlineData("foreach ((string key, int value) in _byName)")]
    [InlineData("foreach (var key in _byName.Keys)")]
    [InlineData("foreach (var value in _byName.Values)")]
    [InlineData("var total = _byName.Values.Sum();")]
    public void Every_spelling_of_dictionary_enumeration_is_a_violation(string enumeration)
    {
        var source = $$"""
            public sealed class Unordered
            {
                private readonly Dictionary<string, int> _byName = new();

                public void Walk()
                {
                    {{enumeration}}
                }
            }
            """;

        var violations = DeterminismScanner.ScanText(source, "Fake.cs");
        Assert.True(
            violations.Any(violation => violation.Rule == "unordered enumeration"),
            $"'{enumeration}' was not reported as unordered enumeration.");
    }

    /// <summary>
    /// Every evasion of the banned-API rules that a person might plausibly write, one case each.
    /// </summary>
    /// <remarks>
    /// Reflection (<c>Type.GetType("System.Random")</c>) is deliberately absent: it is a way round the
    /// guard rather than a mistake anyone makes, and the scanner's own remarks say so rather than
    /// pretending otherwise.
    /// </remarks>
    [Theory]
    [InlineData("System.Random", "public int R() => new Random().Next();")]
    [InlineData("System.Random", "public int R() => new System.Random().Next();")]
    [InlineData("System.Random", "public int R() => new global::System.Random().Next();")]
    [InlineData("System.Random", "public int R() => Random.Shared.Next();")]
    [InlineData("System.Random", "private static readonly System.Random Source = null!;")]
    [InlineData("wall clock", "public long T() => DateTime.Now.Ticks;")]
    [InlineData("wall clock", "public long T() => DateTime.UtcNow.Ticks;")]
    [InlineData("wall clock", "public long T() => DateTimeOffset.UtcNow.Ticks;")]
    [InlineData("wall clock", "public long T() => TimeProvider.System.GetUtcNow().Ticks;")]
    [InlineData("wall clock", "public long T() => Stopwatch.GetTimestamp();")]
    [InlineData("Guid.NewGuid", "public string I() => Guid.NewGuid().ToString();")]
    [InlineData("Environment.TickCount", "public int T() => Environment.TickCount;")]
    [InlineData("Environment.TickCount", "public long T() => Environment.TickCount64;")]
    [InlineData("cryptographic randomness", "public int R() => RandomNumberGenerator.GetInt32(6);")]
    public void Every_banned_api_is_caught_however_it_is_written(string rule, string body)
    {
        var nested = $$"""
            public sealed class Outer
            {
                public sealed class Inner
                {
                    {{body}}
                }
            }
            """;

        var violations = DeterminismScanner.ScanText(nested, "Fake.cs");
        Assert.True(
            violations.Any(violation => violation.Rule == rule),
            $"'{body}' was not reported as '{rule}'. Found: [{string.Join("; ", violations)}]");
    }

    [Fact]
    public void A_using_alias_for_a_banned_type_is_caught_at_its_declaration()
    {
        const string source = """
            using Rnd = System.Random;

            public sealed class Aliased
            {
                public int Roll() => new Rnd().Next(6);
            }
            """;

        // Caught at the alias, which is enough: the alias must itself be inside the scanned tree for the
        // use to compile. A use in another file whose alias is declared outside src/IC2.Engine is in the
        // scanner's stated blind spot.
        Assert.Contains(DeterminismScanner.ScanText(source, "Fake.cs"), v => v.Rule == "System.Random");
    }

    [Fact]
    public void A_hash_code_call_is_flagged_but_can_be_argued_down()
    {
        // .NET randomizes string hashing per process, so a gameplay value derived from GetHashCode differs
        // between two runs -- which is exactly why RngStreams writes out FNV-1a. But GetHashCode is also
        // how one correctly implements value equality, as T02's ValueList<T> does, so it is suppressible
        // rather than banned outright.
        const string flagged = """
            public sealed class Keyed
            {
                public int Key(string name) => name.GetHashCode();
            }
            """;

        Assert.Contains(DeterminismScanner.ScanText(flagged, "Fake.cs"), v => v.Rule == "GetHashCode");

        const string argued = """
            public sealed class Keyed
            {
                // determinism-ok: feeds an equality override, never a persisted or gameplay value.
                public override int GetHashCode() => _items.GetHashCode();
            }
            """;

        Assert.Empty(DeterminismScanner.ScanText(argued, "Fake.cs"));
    }

    [Fact]
    public void Unordered_parallelism_is_flagged()
    {
        const string source = """
            public sealed class Fanned
            {
                public void Walk(List<int> values)
                {
                    Parallel.ForEach(values, v => Handle(v));
                }
            }
            """;

        Assert.Contains(
            DeterminismScanner.ScanText(source, "Fake.cs"),
            v => v.Rule == "unordered parallelism");
    }

    [Fact]
    public void A_raw_interpolated_string_is_stripped_rather_than_misread()
    {
        // The $" branch used to be tested before any raw-string check, so $""" was read as an empty
        // interpolated string followed by a stray quote and everything after it was mis-stripped -- which
        // turned a raw string merely containing the words "new Random()" into a false positive.
        var source = "public sealed class Templated\r\n"
                     + "{\r\n"
                     + "    public string Describe(int n) => $\"\"\"\r\n"
                     + "        this text mentions new Random() and DateTime.Now on purpose: {n}\r\n"
                     + "        \"\"\";\r\n"
                     + "}\r\n";

        Assert.Empty(DeterminismScanner.ScanText(source, "Fake.cs"));
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
