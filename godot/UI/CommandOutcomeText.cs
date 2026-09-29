namespace IC2.Slice.UI;

/// <summary>
/// Fix #484's one rule for what a command's result label shows: the command's <em>whole own outcome</em>
/// — every non-blank line <c>GameSession.Submit</c> rendered for that command, up to but not including
/// the round footer's <c>News:</c> header and the news entries after it.
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
/// screen's label wraps, shows at most three lines with an ellipsis, and keeps the full text in its
/// tooltip — see <c>MainGameScreen</c> — and a long rejection or Save path can never widen the window
/// or push the context panel off-screen.
/// </para>
/// </remarks>
public static class CommandOutcomeText
{
    /// <summary>
    /// The text a result label should show for <paramref name="lines"/>: every non-blank line up to, but
    /// not including, a <c>News:</c> header — minus <c>Submit</c>'s own <c>&gt; &lt;command&gt;</c> echo
    /// line — each trimmed and joined with newlines. When every such line is blank, the last non-blank
    /// line instead; and <see cref="string.Empty"/> when there is no non-blank line at all — never
    /// <see langword="null"/>.
    /// </summary>
    public static string OutcomeBlock(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var block = new List<string>();
        foreach (var line in lines)
        {
            // Submit's own first line is "> <the command just typed>" (GameSession.Submit's echo of its
            // input) -- what the player issued, not an outcome, so the label must not repeat it. The
            // engine emits no other line beginning with this marker.
            if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(line.Trim(), "News:", StringComparison.Ordinal))
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                block.Add(line.Trim());
            }
        }

        if (block.Count > 0)
        {
            return string.Join('\n', block);
        }

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]) && !lines[i].StartsWith("> ", StringComparison.Ordinal))
            {
                return lines[i].Trim();
            }
        }

        return string.Empty;
    }
}
