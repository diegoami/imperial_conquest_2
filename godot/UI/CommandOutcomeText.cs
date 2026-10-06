using IC2.Engine.Model;

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
    public static string OutcomeBlock(IReadOnlyList<string> lines) => OutcomeBlock(lines, null);

    /// <summary>
    /// <see cref="OutcomeBlock(IReadOnlyList{string})"/>, with T147 (bug #781 point 3): when
    /// <paramref name="pendingPeaceOfferLineSets"/> holds a pending offer's dialog lines (as
    /// <c>GameSession.PendingPeaceOfferFor</c> reports them, built by
    /// <see cref="IC2.Engine.Presentation.GameSession.PeaceTreatyOfferDialogLines"/>), those lines and the
    /// single line that follows them — the CLI's <c>"Type 'peace-yes'…"</c> prompt, which has no builder —
    /// are dropped from the block, because the Offer of peace window is the way the app answers. The prompt
    /// is identified by its place after the offer's own lines, never by a copied string.
    /// </summary>
    /// <param name="lines">One <c>Submit</c> call's rendered lines.</param>
    /// <param name="pendingPeaceOfferLineSets">
    /// The pending offers' own <c>PendingPeaceOffer.Lines</c>, one list per offer, or <see langword="null"/>
    /// when the app holds no offer (nothing is dropped).
    /// </param>
    public static string OutcomeBlock(
        IReadOnlyList<string> lines,
        IReadOnlyList<IReadOnlyList<string>>? pendingPeaceOfferLineSets)
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

        var hadNonBlank = block.Count > 0;
        block = DropPeaceOfferLines(block, pendingPeaceOfferLineSets);

        if (block.Count > 0)
        {
            return string.Join('\n', block);
        }

        // Everything the order printed was the pending offer's own lines (now the window's to show), so
        // the output area is empty -- never fall back onto the prompt the filter just dropped.
        if (hadNonBlank)
        {
            return string.Empty;
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

    /// <summary>
    /// T147 (bug #781 point 4): the news entries <paramref name="current"/> holds that the command did
    /// not have before it ran — the entries an order added to <see cref="NewsLog"/>, in order, trimmed,
    /// with whitespace-only spacers dropped.
    /// </summary>
    /// <remarks>
    /// <strong>The comparison is by reference, never by slot count or text.</strong>
    /// <see cref="NewsLog.Append"/> evicts the oldest entry once the ring buffer is full, so the log's
    /// count stops growing and a count-based range (<c>for (i = previousCount; i &lt; current.Count; …)</c>)
    /// finds none of a later order's additions (Sol's review R1). The retained entries are the same
    /// <see cref="NewsEntry"/> objects the previous log held — <c>Append</c> copies
    /// <see cref="NewsLog.Slots"/> by reference — so a reference set finds exactly what a command
    /// appended at any fullness. Text equality would be wrong in the other direction: an appended line
    /// can repeat a retained one, and the repeated line is still this command's own addition.
    /// </remarks>
    /// <param name="previous">The log's slots before the command ran.</param>
    /// <param name="current">The log's slots after it ran.</param>
    public static IReadOnlyList<string> NewsAddedSince(
        IReadOnlyList<NewsEntry> previous,
        IReadOnlyList<NewsEntry> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var retained = new HashSet<NewsEntry>(previous, ReferenceEqualityComparer.Instance);
        var added = new List<string>();
        foreach (var entry in current)
        {
            if (retained.Contains(entry))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entry.Text))
            {
                added.Add(entry.Text.Trim());
            }
        }

        return added;
    }

    /// <summary>
    /// Drops each pending offer's own dialog lines and the line the engine prints immediately after them
    /// (the CLI prompt the window replaces). Matching is by the offer's own builder output, never by a
    /// copied prompt string; the prompt is found by its place after the offer's last line.
    /// </summary>
    private static List<string> DropPeaceOfferLines(
        List<string> block,
        IReadOnlyList<IReadOnlyList<string>>? pendingPeaceOfferLineSets)
    {
        if (pendingPeaceOfferLineSets is null || pendingPeaceOfferLineSets.Count == 0 || block.Count == 0)
        {
            return block;
        }

        var drop = new bool[block.Count];
        foreach (var offerLines in pendingPeaceOfferLineSets)
        {
            if (offerLines is null || offerLines.Count == 0)
            {
                continue;
            }

            for (var i = 0; i + offerLines.Count <= block.Count; i++)
            {
                var matches = true;
                for (var j = 0; j < offerLines.Count; j++)
                {
                    if (!string.Equals(block[i + j], offerLines[j].Trim(), StringComparison.Ordinal))
                    {
                        matches = false;
                        break;
                    }
                }

                if (!matches)
                {
                    continue;
                }

                for (var j = 0; j < offerLines.Count; j++)
                {
                    drop[i + j] = true;
                }

                // The very next line is the engine's own "Type 'peace-yes'…" prompt.
                if (i + offerLines.Count < block.Count)
                {
                    drop[i + offerLines.Count] = true;
                }

                i += offerLines.Count - 1;
            }
        }

        var kept = new List<string>(block.Count);
        for (var i = 0; i < block.Count; i++)
        {
            if (!drop[i])
            {
                kept.Add(block[i]);
            }
        }

        return kept;
    }
}
