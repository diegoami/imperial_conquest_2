namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// Splits a CLI transcript (a script's own committed golden, or one built in-process the same way) into
/// one block per submitted line: the echoed <c>&gt; &lt;line&gt;</c> prompt, split into its own verb and
/// full text, paired with everything printed in reply up to (not including) the next prompt.
/// </summary>
/// <remarks>
/// T80 rework round 1, B1: <c>CommandCoverageTests</c> used to search the whole golden text for
/// <c>"{kind} accepted"</c> as a substring, which also matches
/// <c>diplomacy.declare-war accepted (composed ahead of the attack).</c> — a line
/// <c>attack-army</c>/<c>besiege-city</c> print from their own block, not a <c>declare-war</c> script
/// line's own outcome. Attributing each outcome to the exact prompt block it followed, and requiring the
/// block's own verb to match, is what tells the two apart. The same blocks let
/// <see cref="Presentation.SuccessScriptTests"/> check every mutating line's own outcome directly (N2),
/// rather than scanning the whole transcript for one fixed substring.
/// </remarks>
internal static class GoldenTranscriptBlocks
{
    /// <summary>One submitted line and everything it printed.</summary>
    /// <param name="Line">The full text after <c>"&gt; "</c>, e.g. <c>"declare-war armenia"</c>.</param>
    /// <param name="Verb">The line's first whitespace-separated token, e.g. <c>"declare-war"</c>.</param>
    /// <param name="Outcome">Every line printed in reply, joined with <c>'\n'</c>, not including the next prompt.</param>
    internal readonly record struct Block(string Line, string Verb, string Outcome);

    /// <summary>Parses <paramref name="transcript"/> into one <see cref="Block"/> per <c>"&gt; "</c> prompt.</summary>
    internal static IReadOnlyList<Block> Parse(string transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        var blocks = new List<Block>();
        string? currentLine = null;
        var currentOutcome = new System.Text.StringBuilder();

        void Flush()
        {
            if (currentLine is not null)
            {
                var verb = currentLine.Split(' ', 2)[0];
                blocks.Add(new Block(currentLine, verb, currentOutcome.ToString()));
            }
        }

        foreach (var rawLine in transcript.Split('\n'))
        {
            if (rawLine.StartsWith("> ", StringComparison.Ordinal))
            {
                Flush();
                currentLine = rawLine[2..];
                currentOutcome.Clear();
            }
            else if (currentLine is not null)
            {
                currentOutcome.Append(rawLine).Append('\n');
            }
        }

        Flush();
        return blocks;
    }
}
