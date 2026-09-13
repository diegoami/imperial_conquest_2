using System.Text;
using System.Text.RegularExpressions;

namespace IC2.Engine.Tests.Core.Determinism;

/// <summary>One thing the scanner found: where it is, what rule it broke, and the line itself.</summary>
/// <param name="File">The offending file's path, relative to the repository root, with forward slashes.</param>
/// <param name="Line">One-based line number.</param>
/// <param name="Rule">The rule's name, for the failure message.</param>
/// <param name="Text">The offending line, trimmed.</param>
public sealed record DeterminismViolation(string File, int Line, string Rule, string Text)
{
    /// <inheritdoc/>
    public override string ToString() => $"{File}({Line}): {Rule} -- {Text}";
}

/// <summary>
/// A source scanner that fails the build on the constructs that make a game irreproducible.
/// </summary>
/// <remarks>
/// <para>
/// Required by this task's scope: "a test that scans <c>src/IC2.Engine</c> sources and fails on
/// <c>System.Random</c>, <c>DateTime.Now</c>/<c>UtcNow</c>, <c>Guid.NewGuid</c>,
/// <c>Environment.TickCount</c>, or unordered-dictionary enumeration in gameplay paths". It is a text
/// scanner rather than a Roslyn analyzer for one concrete reason: a Roslyn analyzer is a new NuGet
/// dependency, and adding one is an escalation under the plan's §6.6 case 7, not an implementer's call.
/// The limits that choice imposes are stated under <see cref="Scan"/> rather than glossed over.
/// </para>
/// <para>
/// <strong>Comments and string literals are stripped before any rule runs.</strong> Not a nicety: T02's
/// <c>SaveGame.cs</c> already contains the words "<c>DateTime.Now</c>" inside a doc comment explaining
/// that it does not use one, and a guard that failed on its own documentation would be turned off within
/// a week.
/// </para>
/// </remarks>
public static class DeterminismScanner
{
    /// <summary>
    /// The banned APIs. Each one is a source of a value that differs between two runs of the same game,
    /// which is exactly what <c>docs/game-design.md</c> principle 4 forbids. There is no legitimate use
    /// of any of them in engine code, so, unlike the ordering rule below, these cannot be suppressed.
    /// </summary>
    private static readonly (string Rule, Regex Pattern)[] BannedApis =
    {
        ("System.Random", new Regex(@"\bnew\s+(?:System\s*\.\s*)?Random\s*\(", RegexOptions.Compiled)),
        ("System.Random", new Regex(@"\bRandom\s*\.\s*Shared\b", RegexOptions.Compiled)),
        ("System.Random", new Regex(@"\bSystem\s*\.\s*Random\b", RegexOptions.Compiled)),
        ("wall clock", new Regex(@"\bDateTime(?:Offset)?\s*\.\s*(?:Now|UtcNow|Today)\b", RegexOptions.Compiled)),
        ("wall clock", new Regex(@"\bStopwatch\s*\.\s*GetTimestamp\s*\(", RegexOptions.Compiled)),
        ("Guid.NewGuid", new Regex(@"\bGuid\s*\.\s*NewGuid\s*\(", RegexOptions.Compiled)),
        ("Environment.TickCount", new Regex(@"\bEnvironment\s*\.\s*TickCount(?:64)?\b", RegexOptions.Compiled)),

        // Beyond the five the task scope names, because each is the same class of hazard reached by a
        // different API and a later task would reasonably expect a guard called "determinism" to catch it.
        ("cryptographic randomness", new Regex(@"\bRandomNumberGenerator\s*\.", RegexOptions.Compiled)),
        ("wall clock", new Regex(@"\bTimeProvider\s*\.", RegexOptions.Compiled)),
    };

    /// <summary>
    /// Hazards with a genuinely legitimate use, so a reasoned <c>// determinism-ok:</c> can silence them —
    /// the same treatment the unordered-enumeration rule gets, and for the same reason.
    /// </summary>
    /// <remarks>
    /// <c>GetHashCode</c> is the interesting one. .NET randomizes string hashing per process, so a gameplay
    /// value derived from it differs between two runs — this codebase's own
    /// <see cref="RngStreams.HashStreamName"/> exists precisely because of that. But writing
    /// <c>GetHashCode</c> is also how one correctly implements value equality, as T02's
    /// <c>ValueList&lt;T&gt;</c> does, so a hard ban would be wrong. Flagged, arguable, suppressible.
    /// </remarks>
    private static readonly (string Rule, Regex Pattern)[] SuppressibleApis =
    {
        ("GetHashCode", new Regex(@"\.\s*GetHashCode\s*\(", RegexOptions.Compiled)),
        ("unordered parallelism", new Regex(@"\bParallel\s*\.\s*For(?:Each)?\b", RegexOptions.Compiled)),
        ("unordered parallelism", new Regex(@"\.\s*AsParallel\s*\(", RegexOptions.Compiled)),
    };

    /// <summary>
    /// Collection types whose enumeration order is not part of their contract. Deliberately does not
    /// include the <c>Sorted*</c> and <c>ImmutableSorted*</c> families, whose order is defined and
    /// therefore safe.
    /// </summary>
    private const string UnorderedTypes =
        @"(?:Concurrent)?Dictionary|IDictionary|IReadOnlyDictionary|Hashtable|HashSet|ISet|IReadOnlySet|ConcurrentBag";

    private static readonly Regex TypedDeclaration = new(
        $@"\b(?:{UnorderedTypes})\s*<[^;=()]*?>\s+(?<name>[A-Za-z_]\w*)\s*(?:[=;,)]|$)",
        RegexOptions.Compiled);

    private static readonly Regex InferredDeclaration = new(
        $@"\b(?:var|readonly)\s+(?<name>[A-Za-z_]\w*)\s*=\s*new\s+(?:{UnorderedTypes})\b",
        RegexOptions.Compiled);

    private static readonly Regex NonGenericDeclaration = new(
        $@"\b(?:{UnorderedTypes})\s+(?<name>[A-Za-z_]\w*)\s*(?:[=;,)]|$)",
        RegexOptions.Compiled);

    /// <summary>
    /// The marker that suppresses the ordering rule on one line: <c>// determinism-ok: &lt;reason&gt;</c>,
    /// on the offending line or the one above it.
    /// </summary>
    /// <remarks>
    /// Deliberately available for the ordering rule only. Unordered enumeration is sometimes provably
    /// harmless — summing into an order-independent total, for instance — whereas a wall-clock read never
    /// is. The reason text is mandatory so that a suppression is a sentence a reviewer can disagree with
    /// rather than a silent opt-out, and <see cref="Suppressions"/> lists every one of them so they stay
    /// countable.
    /// </remarks>
    private static readonly Regex SuppressionMarker = new(
        @"//\s*determinism-ok\s*:\s*(?<reason>\S.*)$", RegexOptions.Compiled);

    /// <summary>
    /// Scans every <c>.cs</c> file under <paramref name="root"/>, skipping <c>obj</c> and <c>bin</c>.
    /// </summary>
    /// <param name="root">A directory to scan; normally <c>src/IC2.Engine</c>.</param>
    /// <param name="repositoryRoot">The path violations are reported relative to.</param>
    /// <returns>Every violation found, in file then line order.</returns>
    /// <remarks>
    /// <para>
    /// <strong>What it catches.</strong> The banned APIs anywhere in code, including
    /// <c>new System.Random()</c> and <c>new global::System.Random()</c> written out in full, a
    /// <c>using</c> alias for one (at the alias declaration), and a use nested inside another class. The
    /// suppressible hazards — a <c>GetHashCode()</c> call, <c>Parallel.For</c>/<c>ForEach</c>,
    /// <c>AsParallel()</c>. And enumeration of a local, field or parameter whose declared type is one of
    /// the unordered collections: plain <c>foreach</c>, <strong>every deconstruction spelling</strong>
    /// (<c>foreach (var (k, v) in map)</c>, <c>foreach ((var k, var v) in map)</c>), and a
    /// <c>.Keys</c>/<c>.Values</c> projection.
    /// </para>
    /// <para>
    /// <strong>What it does not catch</strong>, said plainly so nobody mistakes a green guard for a
    /// proof: an unordered collection reached through a method call or a property rather than through a
    /// declared name (<c>Lookup().Values</c>); a collection whose declaration and enumeration are in
    /// different files; an alias declared outside the scanned tree; a raw string literal with a delimiter
    /// longer than three quotes; and reflection (<c>Type.GetType("System.Random")</c>), which is a
    /// deliberate evasion rather than a mistake anyone makes. It errs the other way too: the names of
    /// unordered collections are remembered for the whole file rather than per scope, so a second,
    /// unrelated variable of the same name elsewhere in the file would be flagged. Over-reporting is the
    /// right direction for a guard — it is argued down in review, whereas under-reporting is never
    /// noticed. The structural reason the gap is narrow is that T02's model stores no dictionaries at
    /// all — every collection in <see cref="Model.GameState"/> is a <see cref="Model.ValueList{T}"/>,
    /// whose whole purpose is one stable order — so engine code that wants to enumerate something
    /// unordered has to declare it locally first, which is the case this does catch.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<DeterminismViolation> Scan(string root, string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var violations = new List<DeterminismViolation>();
        foreach (var file in SourceFiles(root))
        {
            var relative = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
            violations.AddRange(ScanText(File.ReadAllText(file), relative));
        }

        return violations;
    }

    /// <summary>Every suppression comment under <paramref name="root"/>, so a reviewer can count them.</summary>
    public static IReadOnlyList<DeterminismViolation> Suppressions(string root, string repositoryRoot)
    {
        var found = new List<DeterminismViolation>();
        foreach (var file in SourceFiles(root))
        {
            var relative = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
            var lines = File.ReadAllText(file).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var match = SuppressionMarker.Match(lines[i]);
                if (match.Success)
                {
                    found.Add(new DeterminismViolation(
                        relative, i + 1, "suppression", match.Groups["reason"].Value.Trim()));
                }
            }
        }

        return found;
    }

    /// <summary>Scans one file's text. Exposed so the guard's own behaviour can be tested directly.</summary>
    public static IReadOnlyList<DeterminismViolation> ScanText(string source, string fileLabel)
    {
        ArgumentNullException.ThrowIfNull(source);

        var rawLines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var codeLines = StripCommentsAndLiterals(source)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');

        var violations = new List<DeterminismViolation>();
        var unorderedNames = new List<string>();

        for (var i = 0; i < codeLines.Length; i++)
        {
            var code = codeLines[i];
            var raw = i < rawLines.Length ? rawLines[i] : string.Empty;

            foreach (var (rule, pattern) in BannedApis)
            {
                if (pattern.IsMatch(code))
                {
                    violations.Add(new DeterminismViolation(fileLabel, i + 1, rule, raw.Trim()));
                }
            }

            CollectUnorderedNames(code, unorderedNames);

            if (IsSuppressed(rawLines, i))
            {
                continue;
            }

            foreach (var (rule, pattern) in SuppressibleApis)
            {
                if (pattern.IsMatch(code))
                {
                    violations.Add(new DeterminismViolation(fileLabel, i + 1, rule, raw.Trim()));
                }
            }

            foreach (var name in unorderedNames)
            {
                if (EnumeratesUnordered(code, name))
                {
                    violations.Add(new DeterminismViolation(
                        fileLabel, i + 1, "unordered enumeration", raw.Trim()));
                    break;
                }
            }
        }

        return violations;
    }

    private static IReadOnlyList<string> SourceFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Nothing to scan: '{root}' does not exist.");
        }

        var files = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');
            if (normalized.Contains("/obj/", StringComparison.Ordinal)
                || normalized.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            files.Add(file);
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    private static bool IsSuppressed(IReadOnlyList<string> rawLines, int index)
    {
        if (SuppressionMarker.IsMatch(rawLines[index]))
        {
            return true;
        }

        return index > 0 && SuppressionMarker.IsMatch(rawLines[index - 1]);
    }

    private static void CollectUnorderedNames(string code, List<string> names)
    {
        foreach (Match match in TypedDeclaration.Matches(code))
        {
            Remember(match.Groups["name"].Value, names);
        }

        foreach (Match match in InferredDeclaration.Matches(code))
        {
            Remember(match.Groups["name"].Value, names);
        }

        foreach (Match match in NonGenericDeclaration.Matches(code))
        {
            Remember(match.Groups["name"].Value, names);
        }
    }

    private static void Remember(string name, List<string> names)
    {
        if (name.Length > 0 && !names.Contains(name, StringComparer.Ordinal))
        {
            names.Add(name);
        }
    }

    private static bool EnumeratesUnordered(string code, string name)
    {
        var escaped = Regex.Escape(name);

        // foreach (var x in map), and every deconstruction spelling of the same thing:
        //     foreach (var (key, value) in map)
        //     foreach ((var key, var value) in map)
        //     foreach ((string key, int value) in map)
        // The pattern before `in` is matched lazily rather than as "no closing parenthesis", because a
        // deconstruction pattern contains its own parentheses — which is how the most idiomatic way to
        // iterate a dictionary slipped past the first version of this rule.
        if (Regex.IsMatch(code, $@"\bforeach\s*\(.*?\bin\s+{escaped}\s*(?:\.\s*(?:Keys|Values)\s*)?\)"))
        {
            return true;
        }

        // Any projection of the key or value sequence, which is where a LINQ chain over an unordered
        // collection usually starts.
        return Regex.IsMatch(code, $@"\b{escaped}\s*\.\s*(?:Keys|Values)\b");
    }

    /// <summary>
    /// Replaces every comment, string and character literal with spaces, keeping the text's length and
    /// line structure so reported line numbers still point at the right line.
    /// </summary>
    private static string StripCommentsAndLiterals(string source)
    {
        var output = new StringBuilder(source.Length);
        var index = 0;

        while (index < source.Length)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';

            if (current == '/' && next == '/')
            {
                index = BlankUntil(source, output, index, static (text, i) => text[i] == '\n');
                continue;
            }

            if (current == '/' && next == '*')
            {
                output.Append("  ");
                index += 2;
                index = BlankUntil(source, output, index, static (text, i) =>
                    i + 1 < text.Length && text[i] == '*' && text[i + 1] == '/');
                if (index < source.Length)
                {
                    output.Append("  ");
                    index += 2;
                }

                continue;
            }

            // Raw strings, plain and interpolated: """ … """, $""" … """, $$""" … """. Checked before the
            // $" branch, because otherwise $""" is read as an empty interpolated string followed by a
            // quote, and everything after it is mis-stripped — which turned a raw string containing the
            // words "new Random()" into a false-positive violation.
            if (IsRawStringStart(source, index, out var rawContentStart))
            {
                while (index < rawContentStart)
                {
                    output.Append(' ');
                    index++;
                }

                index = BlankUntil(source, output, index, static (text, i) =>
                    i + 2 < text.Length && text[i] == '"' && text[i + 1] == '"' && text[i + 2] == '"');
                if (index < source.Length)
                {
                    output.Append("   ");
                    index += 3;
                }

                continue;
            }

            if (current == '@' && next == '"')
            {
                output.Append("  ");
                index += 2;
                index = BlankVerbatimString(source, output, index);
                continue;
            }

            if (current == '$' && next == '"')
            {
                output.Append("  ");
                index += 2;
                index = BlankString(source, output, index, '"');
                continue;
            }

            if (current is '"' or '\'')
            {
                output.Append(' ');
                index++;
                index = BlankString(source, output, index, current);
                continue;
            }

            output.Append(current);
            index++;
        }

        return output.ToString();
    }

    /// <summary>
    /// Whether a raw string literal opens at <paramref name="index"/>, allowing any number of leading
    /// <c>$</c> interpolation markers, and if so where its content begins.
    /// </summary>
    private static bool IsRawStringStart(string source, int index, out int contentStart)
    {
        var cursor = index;
        while (cursor < source.Length && source[cursor] == '$')
        {
            cursor++;
        }

        var quotes = 0;
        while (cursor + quotes < source.Length && source[cursor + quotes] == '"')
        {
            quotes++;
        }

        if (quotes < 3)
        {
            contentStart = index;
            return false;
        }

        contentStart = cursor + quotes;
        return true;
    }

    private static int BlankUntil(string source, StringBuilder output, int index, Func<string, int, bool> isEnd)
    {
        while (index < source.Length && !isEnd(source, index))
        {
            output.Append(source[index] == '\n' ? '\n' : ' ');
            index++;
        }

        return index;
    }

    private static int BlankString(string source, StringBuilder output, int index, char quote)
    {
        while (index < source.Length)
        {
            var current = source[index];
            if (current == '\\' && index + 1 < source.Length)
            {
                output.Append("  ");
                index += 2;
                continue;
            }

            output.Append(current == '\n' ? '\n' : ' ');
            index++;

            if (current == quote || current == '\n')
            {
                break;
            }
        }

        return index;
    }

    private static int BlankVerbatimString(string source, StringBuilder output, int index)
    {
        while (index < source.Length)
        {
            var current = source[index];
            if (current == '"')
            {
                if (index + 1 < source.Length && source[index + 1] == '"')
                {
                    output.Append("  ");
                    index += 2;
                    continue;
                }

                output.Append(' ');
                index++;
                break;
            }

            output.Append(current == '\n' ? '\n' : ' ');
            index++;
        }

        return index;
    }
}
