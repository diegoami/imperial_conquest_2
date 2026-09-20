using System.Text.RegularExpressions;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T41 Thin CLI demo on the toy world", Done-when 5: "The CLI and
/// Presentation code carry no gameplay constants or rules. Every number printed comes from
/// <c>GameState</c> or the ruleset." A text scanner, in the same spirit as
/// <c>tests/IC2.Engine.Tests/Core/Determinism/DeterminismScanner.cs</c>: it finds every bare integer
/// literal in <c>src/IC2.Engine/Presentation/**</c> and <c>src/IC2.Cli/**</c> and fails if one is not on
/// a short, explicit allow-list of purely structural uses (a loop start, an array index, a command's
/// argument count, a process exit code). A real gameplay number -- a cost, a cap, a threshold -- would
/// never legitimately be one of those, so a later change that types one in here as a literal is caught
/// the moment it lands, without this test having to understand what the number means.
/// </summary>
/// <remarks>
/// Deliberately narrower than <see cref="Core.Determinism.DeterminismScanner"/>: it does not need to
/// strip nested comments/raw strings with the same care, because this task's own source (unlike the
/// whole engine) never has a reason to mention a numeral inside prose about numerals. Comments and string
/// literals are still stripped first, so a doc comment that quotes a number (e.g. this task's own PR
/// description) can never trip it.
/// </remarks>
public sealed class GameplayConstantScannerTests
{
    /// <summary>
    /// Every integer literal this task's Presentation/CLI code legitimately contains, each one structural
    /// (indexing, argument counts, loop bookkeeping, process exit codes) rather than a gameplay number.
    /// <c>5</c> and <c>6</c> joined this list with T23's Done-when 1: <c>fleet-transfer</c> takes five
    /// arguments (<c>tokens.Length != 6</c>, and its last token index is <c>5</c>) — still argument-count
    /// and index bookkeeping, the same structural class as the rest of this list, just for a command with
    /// more parameters than <c>move</c> or <c>buy</c> had.
    /// </summary>
    private static readonly HashSet<string> AllowedLiterals =
        new(StringComparer.Ordinal) { "0", "1", "2", "3", "4", "5", "6" };

    private static readonly Regex IntegerLiteral = new(@"(?<!\w)\d+(?!\w)", RegexOptions.Compiled);

    [Theory]
    [InlineData("Presentation")]
    [InlineData("Cli")]
    public void Presentation_and_cli_source_contains_no_unvetted_integer_literal(string projectSuffix)
    {
        var root = projectSuffix == "Cli"
            ? Path.Combine(ModelTestPaths.RepositoryRoot, "src", "IC2.Cli")
            : Path.Combine(ModelTestPaths.RepositoryRoot, "src", "IC2.Engine", "Presentation");

        Assert.True(Directory.Exists(root), $"Expected '{root}' to exist.");

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var stripped = StripCommentsAndStrings(File.ReadAllText(file));
            foreach (Match match in IntegerLiteral.Matches(stripped))
            {
                if (!AllowedLiterals.Contains(match.Value))
                {
                    var line = stripped[..match.Index].Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetFileName(file)}:{line}: literal '{match.Value}' is not on the allow-list");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Unvetted numeric literal(s) found:\n" + string.Join('\n', offenders));
    }

    /// <summary>
    /// Blanks out <c>//</c> and <c>/* */</c> comments and every string/char literal, preserving length
    /// and line breaks so reported line numbers still point at the right line. Simpler than
    /// <see cref="Core.Determinism.DeterminismScanner"/>'s stripper: this task's own files contain no raw
    /// string literals, so that case is not handled here.
    /// </summary>
    private static string StripCommentsAndStrings(string source)
    {
        var output = new System.Text.StringBuilder(source.Length);
        var index = 0;
        while (index < source.Length)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';

            if (current == '/' && next == '/')
            {
                while (index < source.Length && source[index] != '\n')
                {
                    output.Append(' ');
                    index++;
                }

                continue;
            }

            if (current == '/' && next == '*')
            {
                output.Append("  ");
                index += 2;
                while (index < source.Length && !(source[index] == '*' && index + 1 < source.Length && source[index + 1] == '/'))
                {
                    output.Append(source[index] == '\n' ? '\n' : ' ');
                    index++;
                }

                if (index < source.Length)
                {
                    output.Append("  ");
                    index += 2;
                }

                continue;
            }

            if (current is '"' or '\'')
            {
                var quote = current;
                output.Append(' ');
                index++;
                while (index < source.Length)
                {
                    var c = source[index];
                    if (c == '\\' && index + 1 < source.Length)
                    {
                        output.Append("  ");
                        index += 2;
                        continue;
                    }

                    output.Append(c == '\n' ? '\n' : ' ');
                    index++;
                    if (c == quote || c == '\n')
                    {
                        break;
                    }
                }

                continue;
            }

            output.Append(current);
            index++;
        }

        return output.ToString();
    }
}
