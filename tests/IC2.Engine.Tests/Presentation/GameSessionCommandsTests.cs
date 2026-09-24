using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

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

    // ---- #256: HandleEnd's dash-wrapped-elimination sub-case (T23 Done-when 3's remaining sub-case) ----

    /// <summary>
    /// <c>docs/task-catalogue.md</c> T23 Done-when 3's remaining sub-case, follow-up
    /// <see href="https://github.com/diegoami/imperial_conquest_2/issues/256">#256</see>: a round whose
    /// entries include a <em>dash-wrapped</em> elimination (<c>NewsMessageCatalog.IsWrappedInDashLines</c>
    /// only wraps <c>nation.conquered</c>, not an ordinary conquest). Reaching it needs an AI seat to
    /// besiege, capture and eliminate a nation within its own turn: a <em>human</em>-issued besiege win is
    /// flushed by <c>IssueCommand</c>'s own <c>NewsLogWriter.Append</c> call before <c>HandleEnd</c>'s
    /// <c>newsBefore</c> line ever runs, so it can never land inside a round's own news window.
    /// </summary>
    /// <remarks>
    /// A scripted world, not the shipped toy scenario as-is: the toy world's own armies cannot besiege its
    /// own cities at all (issue #267 -- their populations are far above what either nation's economy can
    /// field). So <c>north</c> here keeps its cities and its nation record, but its one starting army is
    /// replaced by a single overwhelming archer force, already adjacent to <c>south</c>'s only city
    /// (<c>meridia</c>, at (3, 4)) -- the same 400,000-archer count
    /// <c>AiSiegeGateTallyTests.An_adjacent_army_strong_enough_is_recorded_as_a_proposal</c> already proves
    /// clears the ratio gate against an equally fortified, equally or more populous city. <c>south</c>
    /// plays first, as the human seat, and does nothing but end its turn; the capture and elimination
    /// happen entirely on <c>north</c>'s own AI turn -- the besiege candidate's base score of 6,000 so far
    /// exceeds anything else on offer that no tie-breaking is in play -- inside the one
    /// <c>TurnCoordinator.RunTurn</c> call <c>HandleEnd</c>'s own <c>while</c> loop makes for it, which is
    /// what lands the dash-wrapped lines inside this round's <c>newsBefore</c>/<c>newsAfter</c> window.
    /// </remarks>
    [Fact]
    public void HandleEnd_prints_the_dash_wrapped_elimination_from_an_ai_seats_own_turn()
    {
        var toy = CoreTestbed.Toy;
        var world = toy.World with
        {
            StartingArmies = ValueList.Of(
                new StartingArmy(
                    "north-overwhelming-army", "north", X: 3, Y: 3, Morale: 60, Money: 0, SupplyTons: 0,
                    Moves: 5, Units: ValueList.Of(CaptureFixtures.Unit("archers", 400_000)))),

            // south goes first (human, does nothing but end its turn) so the capture and elimination
            // happen on north's own AI turn -- inside HandleEnd's while loop, not the unconditional first
            // RunTurn call, and not a human-issued command flushed before newsBefore is read.
            TurnOrder = ValueList.Of("south", "north"),
        };
        var scenario = toy.Scenario with
        {
            Seats = ValueList.Of(
                new Seat("south", SeatControl.Human),
                new Seat(
                    "north", SeatControl.Ai,
                    new AiPersonality(Aggression: 0.5, ExpansionDrive: 0.5, LoyaltyToAlliances: 0.5))),
        };
        var session = new GameSession(world, toy.Ruleset, scenario);
        Assert.Empty(session.State.NewsLog.Slots);

        var output = session.Submit("end");

        Assert.True(
            session.State.NationById("south")!.Eliminated,
            "expected south's only city to fall this round and eliminate it");

        var newsIndex = output.Lines.ToList().IndexOf("News:");
        Assert.True(newsIndex >= 0, "Expected a \"News:\" section after the round that eliminates south.");

        var newsLines = output.Lines.Skip(newsIndex + 1).ToList();
        newsLines.RemoveAt(newsLines.Count - 1); // Submit()'s own trailing blank separator, not a news entry.

        // The DoD's own claim: every entry the round appended is printed, not a fixed trailing count --
        // proven here against a round that actually contains the dash-wrapped sub-case, not just the
        // header plus one ordinary line the #98 test above already covers.
        Assert.Equal(session.State.NewsLog.Slots.Count, newsLines.Count);

        // Rendered lines carry the session's own indentation, so the dash marker is matched by its
        // trimmed content -- the same marker NewsMessageCatalog.DashLineKind's template defines.
        const string dashLine = "-----------------------------------------------------------";
        var dashAt = newsLines.FindIndex(line => string.Equals(line.Trim(), dashLine, StringComparison.Ordinal));
        Assert.True(
            dashAt >= 0, $"Expected a dash-wrapped elimination line among: {string.Join(" | ", newsLines)}");
        Assert.True(
            dashAt + 2 < newsLines.Count, "Expected a message line and a closing dash after the opening dash.");
        Assert.Contains("conquers", newsLines[dashAt + 1], StringComparison.Ordinal);
        Assert.Equal(dashLine, newsLines[dashAt + 2].Trim());

        // And an ordinary line too, so this pins the full header + dash-wrapped-elimination + ordinary
        // line combination the catalogue's Done-when 3 names, not the dash-wrapped case in isolation.
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
