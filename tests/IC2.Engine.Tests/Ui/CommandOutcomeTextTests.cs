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
/// <see cref="CommandOutcomeText.LastNonEmptyLine"/> — the exact expression
/// <c>MainGameScreen.OnCommandIssued</c> now assigns to the label.
/// </summary>
/// <remarks>
/// The rule is "the last non-empty line", chosen over "the whole outcome block" because it is the same
/// rule T95's Save confirmation label already used (one shared rule, not two) and because the label is
/// a single non-autowrapping <c>Label</c> under the bottom toolbar, where an <c>end</c>'s whole block
/// would overflow or balloon the screen. <see cref="CommandOutcomeText"/>'s own remarks carry the full
/// reasoning. Nothing here is a hand-written output line: each expected text is either asserted against
/// the real <c>Submit</c> output or copied from the engine's own constants (the rejection code).
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

        var label = CommandOutcomeText.LastNonEmptyLine(output.Lines);

        Assert.Equal("recruitment.recruit-standing-unit accepted.", label);
        Assert.Empty(output.Lines[^1]);
        Assert.NotEqual(output.Lines[^1], label);
    }

    /// <summary>
    /// The bug, rejected half: the label must show the rejection's own reason text. Rome's army-0 starts
    /// far from Carthage's <c>misurata</c> (114, 96), and <c>AttackLegality</c> checks adjacency before
    /// the war gate, so this siege is deterministically refused with <c>battle.siege-not-adjacent</c>.
    /// </summary>
    [Fact]
    public void A_rejected_order_shows_the_rejections_own_reason_text()
    {
        var session = RomeSession();
        var output = session.Submit("besiege-city army-0 misurata");

        var label = CommandOutcomeText.LastNonEmptyLine(output.Lines);

        Assert.StartsWith("battle.besiege-city rejected (battle.siege-not-adjacent): ", label, StringComparison.Ordinal);
        Assert.Contains("is not adjacent to 'misurata'", label, StringComparison.Ordinal);
        Assert.NotEqual(output.Lines[^1], label);
    }

    /// <summary>
    /// The bug, <c>end</c> half: the label must show something meaningful. The round always closes with
    /// the footer's <c>"Now: Week …"</c> line and, when the round produced news, that news' own entries
    /// after it — so the last non-empty line is the footer or the newest news entry, never blank.
    /// </summary>
    [Fact]
    public void An_end_turn_shows_the_rounds_closing_summary_not_blank()
    {
        var session = RomeSession();
        var output = session.Submit("end");

        var label = CommandOutcomeText.LastNonEmptyLine(output.Lines);

        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.True(
            label.StartsWith("Now: Week ", StringComparison.Ordinal)
            || label.StartsWith("  ", StringComparison.Ordinal),
            $"the end's own last non-empty line is the round footer or a news entry; got '{label}'");
        Assert.NotEqual(output.Lines[^1], label);
    }
}
