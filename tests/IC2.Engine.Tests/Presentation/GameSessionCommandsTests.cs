using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// <c>docs/task-catalogue.md</c> T23 Done-when 4's folded follow-ups, exercised directly through
/// <see cref="GameSession"/> rather than through the golden transcript, so each stays pinned even if the
/// demo script changes later.
/// </summary>
public sealed class GameSessionCommandsTests
{
    private static GameSession NewSession(ulong? seed = null) =>
        new(CoreTestbed.Toy.World, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.Scenario, seed);

    // ---- #221: the CLI composes DeclareWarCommand ahead of an attack, when the two nations are at peace ----

    [Fact]
    public void Attack_army_composes_declare_war_when_the_two_nations_are_at_peace()
    {
        var session = NewSession();
        Assert.NotEqual(
            session.Ruleset.Diplomacy.StateCodes.War, session.State.Relations.Get("north", "south"));

        var output = session.Submit("attack-army north-army-1 south-army-1");

        Assert.Contains(
            output.Lines, line => line.Contains("diplomacy.declare-war accepted", StringComparison.Ordinal));
        Assert.DoesNotContain(
            output.Lines, line => line.Contains("battle.not-at-war", StringComparison.Ordinal));
        Assert.Equal(
            session.Ruleset.Diplomacy.StateCodes.War, session.State.Relations.Get("north", "south"));
    }

    [Fact]
    public void Besiege_city_composes_declare_war_when_the_two_nations_are_at_peace()
    {
        var session = NewSession();

        var output = session.Submit("besiege-city north-army-1 meridia");

        Assert.Contains(
            output.Lines, line => line.Contains("diplomacy.declare-war accepted", StringComparison.Ordinal));
        Assert.DoesNotContain(
            output.Lines, line => line.Contains("battle.siege-not-at-war", StringComparison.Ordinal));
        Assert.Equal(
            session.Ruleset.Diplomacy.StateCodes.War, session.State.Relations.Get("north", "south"));
    }

    [Fact]
    public void Attack_fleet_composes_declare_war_when_the_two_nations_are_at_peace()
    {
        var session = NewSession();

        var output = session.Submit("attack-fleet north-fleet-1 south-fleet-1");

        Assert.Contains(
            output.Lines, line => line.Contains("diplomacy.declare-war accepted", StringComparison.Ordinal));
        Assert.DoesNotContain(
            output.Lines, line => line.Contains("battle.fleet-not-at-war", StringComparison.Ordinal));
        Assert.Equal(
            session.Ruleset.Diplomacy.StateCodes.War, session.State.Relations.Get("north", "south"));
    }

    /// <summary>
    /// The composition never redeclares once the two nations are already at war -- a second
    /// <c>diplomacy.declare-war</c> line would be a second, spurious order the scripter never asked for.
    /// </summary>
    [Fact]
    public void Attack_army_does_not_redeclare_war_when_already_at_war()
    {
        var session = NewSession();
        session.Submit("declare-war south");
        Assert.Equal(
            session.Ruleset.Diplomacy.StateCodes.War, session.State.Relations.Get("north", "south"));

        var output = session.Submit("attack-army north-army-1 south-army-1");

        Assert.DoesNotContain(
            output.Lines, line => line.Contains("diplomacy.declare-war", StringComparison.Ordinal));
    }

    /// <summary>
    /// The composition never fires for a self-target attack -- there is no "other nation" to declare war
    /// on, and the attack's own <c>battle.same-nation</c> gate is what should answer instead.
    /// </summary>
    [Fact]
    public void Attack_army_against_your_own_nation_does_not_attempt_to_declare_war()
    {
        var session = NewSession();

        var output = session.Submit("attack-army north-army-1 north-army-1");

        Assert.DoesNotContain(
            output.Lines, line => line.Contains("diplomacy.declare-war", StringComparison.Ordinal));
        Assert.Contains(output.Lines, line => line.Contains("battle.same-nation", StringComparison.Ordinal));
    }

    // ---- #98: HandleEnd counts what the news log actually appended, not news-worthy events ----

    /// <summary>
    /// The toy scenario's own first round: the Southern League's AI turn publishes at least one
    /// news-worthy event of its own, and the round still ends with its mandatory blank-line-and-header
    /// pair (news-log-format-and-messages.md Q3: "a quiet round costs 2 of the 40 slots" even with no
    /// event at all). Before this fix, <c>HandleEnd</c> inferred how many lines to print by counting
    /// news-worthy <em>events</em> — a number the header's own two entries are never backed by, and which
    /// a dash-wrapped event (three log entries for one event) can also disagree with — instead of asking
    /// <see cref="Model.NewsLog"/> how many entries it actually grew by. This asserts the two can never
    /// drift apart again: every entry the log gained this round is printed, and it is provably more than
    /// the header alone.
    /// </summary>
    [Fact]
    public void HandleEnd_prints_every_entry_the_round_actually_appended_not_just_the_header()
    {
        var session = NewSession();
        Assert.Empty(session.State.NewsLog.Slots);

        var output = session.Submit("end");

        var newsIndex = output.Lines.ToList().IndexOf("News:");
        Assert.True(newsIndex >= 0, "Expected a \"News:\" section after the first round.");

        var newsLines = output.Lines.Skip(newsIndex + 1).ToList();
        newsLines.RemoveAt(newsLines.Count - 1); // Submit()'s own trailing blank separator, not a news entry.

        // The log itself is the source of truth: every entry it holds after this one round must be
        // printed, not merely the tail the old event-count heuristic happened to compute.
        Assert.Equal(session.State.NewsLog.Slots.Count, newsLines.Count);

        // The round's own mandatory header alone is 2 entries (a blank line, then the week text,
        // news-log-format-and-messages.md Q3). More than that proves a real event's own line survived
        // too -- exactly what the old news-worthy-event count could silently drop.
        Assert.True(
            newsLines.Count > 2,
            $"Expected more than the round header alone (2 entries); got {newsLines.Count}: {string.Join(" | ", newsLines)}");
        Assert.Contains(newsLines, line => line.Contains("Week", StringComparison.Ordinal));
    }

    // ---- #100 item 2: season names come from the ruleset, not a hardcoded duplicate ----

    [Fact]
    public void Season_names_are_read_from_the_rulesets_own_news_log_table()
    {
        var toy = CoreTestbed.Toy;
        var customRuleset = toy.Ruleset with
        {
            NewsLog = toy.Ruleset.NewsLog with
            {
                SeasonNames = ValueList.Of("Firstseason", "Secondseason", "Thirdseason", "Fourthseason"),
            },
        };
        var session = new GameSession(toy.World, customRuleset, toy.Scenario);

        var output = session.Submit("status");

        Assert.Contains(output.Lines, line => line.Contains("Firstseason", StringComparison.Ordinal));
        Assert.DoesNotContain(output.Lines, line => line.Contains("Spring", StringComparison.Ordinal));
    }

    // ---- #232: the stale "Not yet implemented" banner is gone, not renamed ----

    [Fact]
    public void Help_no_longer_carries_a_hand_maintained_not_yet_implemented_banner()
    {
        var session = NewSession();

        var output = session.Submit("help");

        Assert.DoesNotContain(
            output.Lines, line => line.Contains("Not yet implemented", StringComparison.Ordinal));
    }

    // ---- Done-when 2: a human move ending against a non-hostile city resupplies automatically ----

    /// <summary>
    /// <c>docs/task-catalogue.md</c> T23 Done-when 2: <c>north-army-1</c> moved to <c>(4, 3)</c> and left
    /// to drain over twelve human-only turns is exactly <see cref="SupplyPurchaseDemoTests"/>'s own proven
    /// recipe for reaching zero supply (that suite's <c>Before.ArmySupply</c> is asserted <c>0</c>). From
    /// there, a move that ends beside Portus again -- with no explicit <c>buy</c> -- is Done-when 2's
    /// trigger: <see cref="Economy.AutomaticResupply.ForArmy"/> fires automatically and the army's supply
    /// rises. Every seat is human here, the same reason <see cref="SupplyPurchaseDemoTests"/> gives: the
    /// rule under test is the resupply seam, not the AI, which would otherwise destroy this very army on
    /// its first turn (as it does in the shipped demo script).
    /// </summary>
    [Fact]
    public void A_move_ending_beside_the_armys_own_city_resupplies_it_automatically_once_drained()
    {
        var toy = CoreTestbed.Toy;
        var session = new GameSession(toy.World, toy.Ruleset, AllSeatsHuman(toy.Scenario));

        Submit(session, "move north-army-1 4 3"); // adjoins portus (own) and meridia (foreign).
        for (var i = 0; i < 12; i++)
        {
            Submit(session, "end");
        }

        var drained = session.State.ArmyById("north-army-1")!;
        Assert.Equal(0, drained.SupplyTons);

        // A move that ends on the very tile it started from still resolves adjacency against Portus.
        Submit(session, "move north-army-1 4 3");

        var resupplied = session.State.ArmyById("north-army-1")!;
        Assert.True(
            resupplied.SupplyTons > 0,
            "Expected the move to trigger an automatic resupply from Portus without an explicit 'buy'.");
    }

    /// <summary>
    /// The same scenario <see cref="SupplyPurchaseDemoTests"/> builds, duplicated locally rather than
    /// exposed from that internal test class: every seat human, so the rule under test (the automatic
    /// resupply trigger, not the AI) is isolated.
    /// </summary>
    private static Scenario AllSeatsHuman(Scenario scenario) =>
        scenario with
        {
            Seats = ValueList.From(
                scenario.Seats.Select(seat => seat with { Control = SeatControl.Human, Personality = null })),
        };

    private static void Submit(GameSession session, string line) => session.Submit(line);
}
