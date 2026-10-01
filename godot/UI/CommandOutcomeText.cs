namespace IC2.Slice.UI;

/// <summary>
/// Fix #484's one rule for what a command's result label shows: the command's <em>whole own outcome</em>
/// — every non-blank line <c>GameSession.Submit</c> rendered for that command after its own
/// <c>&gt; &lt;command&gt;</c> echo line, up to but not including the round footer's <c>News:</c> header
/// and the news entries after it.
/// </summary>
/// <remarks>
/// <para>
/// <c>SessionOutput.Lines</c> always ends with a blank separator line (<c>GameSession.Submit</c> appends
/// <see cref="string.Empty"/> after every command), so reading <c>lines[^1]</c> is what left the main
/// game screen's last-command label empty for every order ever issued. Every genuine outcome line sits
/// above that separator. Each line is trimmed, a whitespace-only entry (the news log's own
/// <c>"   "</c> spacer) is skipped rather than shown, and the engine's own <c>&gt; &lt;command&gt;</c>
/// echo line (the input the player just gave, not an outcome) is skipped too, so the label can never be
/// visually blank while a real outcome exists.
/// </para>
/// <para>
/// <strong>The block starts <em>after</em> the echo line (T96, R1-B2).</strong> <c>Submit</c> prepends
/// <c>GameSession._pendingPrelude</c> — the AI seats played before a non-first human seat's first turn
/// (<c>GameSession.cs</c>, <c>AdvanceToHumanSeat</c>) — above the echo, and nothing in the GUI consumes
/// it first. Scanning from the top therefore buried a non-first seat's first order under AI-turn lines;
/// the prelude is not part of the command's own outcome, so the scan starts at the first line after the
/// echo. The engine emits no line beginning with <c>"&gt; "</c> other than the echo.
/// </para>
/// <para>
/// <strong>Why the whole own outcome and not a single line.</strong> Round 1 shipped "the last
/// non-empty line"; its review (B1, N2, N3) found the three defects this block closes. An <c>end</c>'s
/// closing block is <c>"Now: Week …, Active seat: …"</c> followed by <c>News:</c> and the round's news
/// entries — the last non-empty line of that is always the news log's own week banner, so the label
/// never reached the footer. An attack on a nation at peace composes its declaration of war
/// (<c>GameSession.Commands.cs</c>'s <c>ComposeDeclareWarIfNeeded</c>) <em>before</em> the attack's own
/// result, and a single line hid that declaration whenever the attack itself was rejected. And the
/// spacer is whitespace-only, so "non-empty" had to become "non-blank" to keep the label honest. The
/// news section itself is excluded because it belongs to the news log panel, not to this command: it
/// repeats whatever the log already shows and the next round re-renders anyway.
/// </para>
/// <para>
/// The block can be long (an <c>end</c> carries every AI seat's turn line and the weather), so the
/// screen's label wraps, shows the block's <em>last</em> three lines with an ellipsis, and keeps the
/// full text in its tooltip — see <c>MainGameScreen</c> — and a long rejection or Save path can never
/// widen the window or push the context panel off-screen. Showing the last lines is what surfaces an
/// <c>end</c>'s <c>Now: Week …</c> footer, which the first-three-lines ceiling hid (T96, B1 as
/// displayed).
/// </para>
/// </remarks>
public static class CommandOutcomeText
{
    /// <summary>
    /// The text a result label should show for <paramref name="lines"/>: every non-blank line after
    /// <c>Submit</c>'s own <c>&gt; &lt;command&gt;</c> echo line up to, but not including, a <c>News:</c>
    /// header, each trimmed and joined with newlines. When every such line is blank, the last non-blank
    /// line after the echo instead; and <see cref="string.Empty"/> when there is no non-blank line at all
    /// — never <see langword="null"/>.
    /// </summary>
    public static string OutcomeBlock(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        // Submit's own echo line is "> <the command just typed>" (GameSession.Submit's echo of its
        // input) -- what the player issued, not an outcome. Anything above it is the pending prelude
        // (AI seats played before a non-first human seat's first turn), also not this command's own
        // outcome, so the scan starts just after the first echo line. The engine emits no other line
        // beginning with this marker; when there is none (a defensive fallback for callers that pass
        // output from somewhere else), the whole list is scanned.
        var start = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("> ", StringComparison.Ordinal))
            {
                start = i + 1;
                break;
            }
        }

        var block = new List<string>();
        for (var i = start; i < lines.Count; i++)
        {
            if (string.Equals(lines[i].Trim(), "News:", StringComparison.Ordinal))
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                block.Add(lines[i].Trim());
            }
        }

        if (block.Count > 0)
        {
            return string.Join('\n', block);
        }

        for (var i = lines.Count - 1; i >= start; i--)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                return lines[i].Trim();
            }
        }

        return string.Empty;
    }
}
