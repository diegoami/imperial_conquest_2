namespace IC2.Slice.UI;

/// <summary>
/// Fix #484: the one rule behind what a command's own result label shows — the <em>last non-empty</em>
/// line of <c>GameSession.Submit</c>'s rendered output.
/// </summary>
/// <remarks>
/// <para>
/// <c>SessionOutput.Lines</c> always ends with a blank separator line (<c>GameSession.Submit</c> appends
/// <see cref="string.Empty"/> after every command), so the last entry is never the answer: reading
/// <c>lines[^1]</c> is what left the main game screen's last-command label empty for every order ever
/// issued. Every genuine outcome line — an acceptance, a rejection, an <c>end</c> round's own closing
/// summary — sits above that separator.
/// </para>
/// <para>
/// <strong>Why the last non-empty line, not the whole outcome block.</strong> It is the same rule T95's
/// Save confirmation label already used (<c>LastOrDefault(line =&gt; line.Length &gt; 0)</c>), so the
/// screen now has exactly one shared rule instead of two. It also fits the control: the label is a
/// single <c>Label</c> built by <c>UiKit.MakeLabel</c> with no autowrap, so an <c>end</c>'s whole block
/// (a dozen AI seats, weather, the round footer and the news) would either overflow one line or balloon
/// the screen. The last non-empty line answers the only question the label exists for — did that order
/// work, and if not, why — in one line, and it is the engine's own closing summary in every case: the
/// acceptance/rejection line for an order, and the newest news entry (or <c>"Now: Week …"</c>) for an
/// <c>end</c>.
/// </para>
/// </remarks>
public static class CommandOutcomeText
{
    /// <summary>
    /// The text a one-line result label should show for <paramref name="lines"/>: the last entry that is
    /// not the empty separator, or <see cref="string.Empty"/> when every entry is (a command that
    /// produced no output at all) — never <see langword="null"/>.
    /// </summary>
    public static string LastNonEmptyLine(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (lines[i].Length > 0)
            {
                return lines[i];
            }
        }

        return string.Empty;
    }
}
