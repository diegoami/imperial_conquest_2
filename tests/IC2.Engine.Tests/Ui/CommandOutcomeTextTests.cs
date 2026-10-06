using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// Fix #484's reproduction: <c>MainGameScreen.OnCommandIssued</c> used to set the shared last-command
/// label to <c>lines[^1]</c>, and <see cref="GameSession.Submit"/> always ends its output with a blank
/// separator line — so the label was empty after every command, and no order (accepted or rejected)
/// ever gave feedback. Every case here drives a <em>real</em> command through the real
/// <see cref="GameSession.Submit"/> over the shipped classical world/ruleset as Rome (the CLI's
/// <c>--seat rome</c> shape) and asserts what the label shows through
/// <see cref="CommandOutcomeText.OutcomeBlock"/> — the exact expression
/// <c>MainGameScreen.OnCommandIssued</c> now assigns to the label.
/// </summary>
/// <remarks>
/// <strong>The rule is the command's whole own outcome</strong> — every non-blank line <em>after</em>
/// <c>Submit</c>'s own <c>&gt; &lt;command&gt;</c> echo line, up to but not including the round footer's
/// <c>News:</c> header, trimmed and joined with newlines — chosen over "the last non-empty line" after
/// review round 1 (B1, N2, N3): that rule showed an <c>end</c>'s news banner instead of the round's own
/// closing summary, could select a whitespace-only spacer, and hid the declaration of war composed ahead
/// of a rejected attack. T96's re-review (R1-B2) added the echo start: <c>Submit</c> prepends the AI
/// seats played before a non-first human seat's first turn above the echo, and scanning from the top
/// buried that seat's first order under AI-turn lines. <see cref="CommandOutcomeText"/>'s own remarks
/// carry the full reasoning. Nothing here is a hand-written output line: each expected text is asserted
/// against the real <c>Submit</c> output or copied from the engine's own constants (the rejection codes).
/// </remarks>
public sealed class CommandOutcomeTextTests
{
    private const string RomeId = "rome";
    private const string RomeCityId = "rome";

    private static ResolvedScenario Classical() =>
        GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");

    /// <summary>A Rome-seated session over the shipped classical pair, the CLI's <c>--seat rome</c> shape.</summary>
    private static GameSession RomeSession(ulong seed = 1)
    {
        var classical = Classical();
        return new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: seed, humanSeatNationId: RomeId);
    }

    /// <summary>
    /// The bug, accepted half: the label must show the order's own acceptance line. Before the fix the
    /// screen assigned <c>lines[^1]</c> — the empty trailing separator this test also pins — so the
    /// label was empty.
    /// </summary>
    [Fact]
    public void An_accepted_order_shows_its_acceptance_line_not_the_blank_separator()
    {
        var session = RomeSession();
        var output = session.Submit($"recruit-standing {RomeCityId} light_infantry 15000");

        Assert.Contains("recruitment.recruit-standing-unit accepted.", output.Lines, StringComparer.Ordinal);

        var label = CommandOutcomeText.OutcomeBlock(output.Lines);

        Assert.Equal("recruitment.recruit-standing-unit accepted.", label);
        Assert.Empty(output.Lines[^1]);
        Assert.NotEqual(output.Lines[^1], label);
    }

    /// <summary>
    /// The bug, rejected half: the label must show the rejection's own reason text. Rome's army-0 starts
    /// far from Carthage's <c>misurata</c> (114, 96), and <c>AttackLegality</c> checks adjacency before
    /// the war gate, so this siege is deterministically refused with <c>battle.siege-not-adjacent</c>.
    /// Rome and Carthage start at peace, so <c>Submit</c> composes the declaration of war ahead of the
    /// refusal — the block's first line, asserted separately below because the one-line rule of round 1
    /// dropped it (review N3).
    /// </summary>
    [Fact]
    public void A_rejected_order_shows_the_rejections_own_reason_text()
    {
        var session = RomeSession();
        var output = session.Submit("besiege-city army-0 misurata");

        Assert.Contains(
            "diplomacy.declare-war accepted (composed ahead of the attack).", output.Lines, StringComparer.Ordinal);

        var label = CommandOutcomeText.OutcomeBlock(output.Lines);
        var labelLines = label.Split('\n');

        Assert.Equal("diplomacy.declare-war accepted (composed ahead of the attack).", labelLines[0]);
        Assert.StartsWith("battle.besiege-city rejected (battle.siege-not-adjacent): ", labelLines[^1], StringComparison.Ordinal);
        Assert.Contains("is not adjacent to 'misurata'", labelLines[^1], StringComparison.Ordinal);
        Assert.NotEqual(output.Lines[^1], label);
    }

    /// <summary>
    /// Review N3, its own case: an attack on a nation Rome is not at war with. <c>Submit</c> composes
    /// <c>diplomacy.declare-war</c> ahead of the attack and then reports the attack's own result, so the
    /// label must show <em>both</em> lines — the declaration is part of the command's own outcome, and
    /// round 1's "last non-empty line" showed only the second. Carthage's army-2 starts at (47, 62), far
    /// from Rome's army-0 (100, 37), so the attack is refused with <c>battle.not-adjacent</c>.
    /// </summary>
    [Fact]
    public void An_attack_that_composes_a_declaration_of_war_shows_both_its_lines()
    {
        var session = RomeSession();
        var output = session.Submit("attack-army army-0 army-2");

        Assert.Contains(
            "diplomacy.declare-war accepted (composed ahead of the attack).", output.Lines, StringComparer.Ordinal);
        Assert.Contains(
            output.Lines,
            line => line.StartsWith("battle.attack-army rejected (battle.not-adjacent): ", StringComparison.Ordinal)
                && line.Contains("is not adjacent to 'army-2'", StringComparison.Ordinal));

        var labelLines = CommandOutcomeText.OutcomeBlock(output.Lines).Split('\n');

        Assert.Equal(2, labelLines.Length);
        Assert.Equal("diplomacy.declare-war accepted (composed ahead of the attack).", labelLines[0]);
        Assert.StartsWith("battle.attack-army rejected (battle.not-adjacent): ", labelLines[1], StringComparison.Ordinal);
        Assert.Empty(output.Lines[^1]);
    }

    /// <summary>
    /// The bug, <c>end</c> half, tightened by review B1: the label must end on the round's own closing
    /// summary — the exact <c>"Now: Week …"</c> line the real output carries (prefix and week number
    /// and all) — and no line of the news section after the footer's <c>News:</c> header may appear.
    /// Round 1's assertion accepted any news banner here, and that is exactly what the label showed.
    /// </summary>
    [Fact]
    public void An_end_turn_shows_the_rounds_own_closing_summary_not_the_news_banner()
    {
        var session = RomeSession();
        var output = session.Submit("end");

        var expectedNowLine = Assert.Single(
            output.Lines, line => line.StartsWith("Now: Week ", StringComparison.Ordinal));
        Assert.StartsWith($"Now: Week {session.State.Calendar.Week},", expectedNowLine, StringComparison.Ordinal);

        var labelLines = CommandOutcomeText.OutcomeBlock(output.Lines).Split('\n');

        Assert.Equal(expectedNowLine, labelLines[^1]);
        Assert.DoesNotContain(labelLines, string.IsNullOrWhiteSpace);

        var newsHeaderIndex = FindNewsHeader(output.Lines);
        Assert.True(newsHeaderIndex >= 0, "the real end output carries the round footer's News: header");

        var newsLines = output.Lines
            .Skip(newsHeaderIndex + 1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToList();
        Assert.NotEmpty(newsLines);
        Assert.DoesNotContain(labelLines, newsLines.Contains);
        Assert.NotEqual(output.Lines[^1], labelLines[^1]);
    }

    /// <summary>
    /// Review N2, its own case: the news log carries a whitespace-only spacer entry (T42's round header),
    /// and <c>RenderNews</c> renders it as a whitespace-only line. After one <c>end</c> has produced
    /// news, a real <c>news</c> command's output really contains such a line — and the label takes the
    /// whole visible log (the command has no <c>News:</c> section), so the spacer must be dropped rather
    /// than selected. Round 1's <c>line.Length &gt; 0</c> test treated it as a real line.
    /// </summary>
    [Fact]
    public void A_whitespace_only_spacer_line_never_reaches_the_label()
    {
        var session = RomeSession();
        session.Submit("end");
        var output = session.Submit("news");

        Assert.Contains(output.Lines, line => line.Length > 0 && string.IsNullOrWhiteSpace(line));

        var labelLines = CommandOutcomeText.OutcomeBlock(output.Lines).Split('\n');

        Assert.DoesNotContain(labelLines, string.IsNullOrWhiteSpace);
        Assert.DoesNotContain(labelLines, line => !string.Equals(line, line.Trim(), StringComparison.Ordinal));

        // The label never repeats Submit's own "> news" echo line, and a "news" command has no News:
        // header, so the block is the whole log, each line trimmed and no spacer line anywhere.
        Assert.StartsWith("News log:", labelLines[0], StringComparison.Ordinal);
        var expected = string.Join(
            '\n',
            output.Lines
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("> ", StringComparison.Ordinal))
                .Select(line => line.Trim()));
        Assert.Equal(expected, string.Join('\n', labelLines));
        Assert.NotEqual(output.Lines[^1], string.Join('\n', labelLines));
    }

    /// <summary>
    /// T96 DoD 4: a plain rejected order shows its one line. The first siege composes the declaration of
    /// war ahead of its refusal (the case above); once Rome is at war, the identical second siege has no
    /// declaration left to compose, so its label is exactly the refusal line.
    /// </summary>
    [Fact]
    public void A_plain_rejected_order_shows_its_one_line()
    {
        var session = RomeSession();
        session.Submit("besiege-city army-0 misurata");
        var output = session.Submit("besiege-city army-0 misurata");

        Assert.DoesNotContain(
            output.Lines, line => line.Contains("diplomacy.declare-war", StringComparison.Ordinal));

        var labelLines = CommandOutcomeText.OutcomeBlock(output.Lines).Split('\n');

        Assert.Single(labelLines);
        Assert.StartsWith(
            "battle.besiege-city rejected (battle.siege-not-adjacent): ", labelLines[0], StringComparison.Ordinal);
        Assert.Contains("is not adjacent to 'misurata'", labelLines[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// T96 DoD 3 (PR #521's re-review R1-B2): <c>GameSession.Submit</c> prepends the AI seats played
    /// before a non-first human seat's first turn (<c>_pendingPrelude</c>) <em>above</em> the
    /// <c>&gt; &lt;command&gt;</c> echo, and nothing in the GUI consumes it first. A block that scanned
    /// from the top therefore made Thracia's first order 16 lines long, with the acceptance hidden under
    /// the AI-turn lines by the label's three-line window. The prelude is not part of the command's own
    /// outcome: the block starts after the echo.
    /// </summary>
    [Fact]
    public void A_non_first_seats_first_order_shows_only_its_own_outcome_not_the_ai_prelude()
    {
        var classical = Classical();
        var session = new GameSession(
            classical.World,
            classical.Ruleset,
            classical.Scenario,
            seedOverride: 1,
            humanSeatNationId: "thracia");

        var output = session.Submit("recruit-standing byzantium light_infantry 15000");

        // The real output really carries a non-empty prelude above the echo, and the order is really
        // accepted, so this case is not vacuous.
        var echoIndex = -1;
        for (var i = 0; i < output.Lines.Count; i++)
        {
            if (output.Lines[i].StartsWith("> ", StringComparison.Ordinal))
            {
                echoIndex = i;
                break;
            }
        }

        Assert.True(echoIndex > 0, "the non-first seat's first output carries the AI prelude above the echo");
        var preludeLines = output.Lines
            .Take(echoIndex)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToList();
        Assert.NotEmpty(preludeLines);
        Assert.Contains("recruitment.recruit-standing-unit accepted.", output.Lines, StringComparer.Ordinal);

        var label = CommandOutcomeText.OutcomeBlock(output.Lines);

        Assert.Equal("recruitment.recruit-standing-unit accepted.", label);
        Assert.DoesNotContain(preludeLines, line => label.Contains(line, StringComparison.Ordinal));
        Assert.DoesNotContain("takes its turn", label, StringComparison.Ordinal);
    }

    /// <summary>
    /// T147 (bug #781 point 3): an order that raises a post-battle peace offer carries the offer's own
    /// dialog lines and the CLI's <c>"Type 'peace-yes'…"</c> prompt in its rendered output. When the app
    /// holds that offer (the window is the way to answer), the block drops both and keeps the order's own
    /// line; with no pending offer nothing is dropped. The offer's lines come from the engine's own
    /// builder, <see cref="GameSession.PeaceTreatyOfferDialogLines"/>, and the filter finds the prompt by
    /// its place after them, never by a copied string.
    /// </summary>
    [Fact]
    public void A_pending_offers_dialog_lines_and_the_prompt_that_follows_them_are_dropped()
    {
        var session = RomeSession();
        var winner = session.State.NationById(RomeId)!;
        var loser = session.State.NationById("carthage")!;
        var offer = GameSession.PeaceTreatyOfferDialogLines(winner, loser);

        // The prompt is the engine's inline literal (GameSession.CapturePeaceTreatyOfferIfAny); a test may
        // spell it out to build the input, the filter never does.
        const string prompt = "Type 'peace-yes' to accept or 'peace-no' to decline.";
        var sample = new List<string>
        {
            "> attack-army army-0 army-2",
            "battle.attack-army accepted.",
            string.Empty,
            offer[0],
            offer[1],
            offer[2],
            offer[3],
            prompt,
            string.Empty,
        };

        var filtered = CommandOutcomeText.OutcomeBlock(sample, new[] { offer });
        Assert.Equal("battle.attack-army accepted.", filtered);

        var unfiltered = CommandOutcomeText.OutcomeBlock(sample, null);
        Assert.Equal(
            string.Join('\n', new[] { "battle.attack-army accepted." }.Concat(offer).Append(prompt)),
            unfiltered);
        Assert.Contains(prompt, unfiltered, StringComparison.Ordinal);
        Assert.Contains(offer[0], unfiltered, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sol's review R1: the news log is a ring buffer, so a count-based "the entries after the previous
    /// count" range finds <em>nothing</em> once the buffer is full — the exact ordinary-long-game case
    /// that hid an accepted order's news line. <see cref="CommandOutcomeText.NewsAddedSince"/> compares
    /// by reference, so the order's own appended entry is found whether the log has room or is full.
    /// </summary>
    [Fact]
    public void A_full_news_log_still_reports_an_orders_own_added_entry()
    {
        var rules = Classical().Ruleset.NewsLog;
        var full = NewsLog.Empty;
        for (var i = 0; i < rules.RingBufferSlots; i++)
        {
            full = full.Append(new NewsEntry($"Retained {i}"), rules);
        }

        Assert.Equal(rules.RingBufferSlots, full.Slots.Count);

        const string addedText = "Rome and Carthage have agreed to end their war.";
        var after = full.Append(new NewsEntry(addedText), rules);

        // The ring buffer really stayed full, so a count-based range would be empty.
        Assert.Equal(rules.RingBufferSlots, after.Slots.Count);
        Assert.Equal(new[] { addedText }, CommandOutcomeText.NewsAddedSince(full.Slots, after.Slots));
    }

    /// <summary>
    /// Sol's review R1, the other direction text equality would get wrong: an appended line that repeats
    /// a retained one is still this command's own addition, so it must be reported once.
    /// </summary>
    [Fact]
    public void An_appended_news_entry_that_repeats_a_retained_line_is_still_reported()
    {
        var rules = Classical().Ruleset.NewsLog;
        var previous = NewsLog.Empty.Append(new NewsEntry("same text"), rules);
        var after = previous.Append(new NewsEntry("same text"), rules);

        Assert.Equal(new[] { "same text" }, CommandOutcomeText.NewsAddedSince(previous.Slots, after.Slots));
    }

    private static int FindNewsHeader(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (string.Equals(lines[i].Trim(), "News:", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
