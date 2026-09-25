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
    /// A scripted world, not the shipped toy scenario as-is (T82, #359/#357, Owns amendment PR #378):
    /// before this task, the toy scenario's own first round reliably published a news-worthy event
    /// through the AI's old, buggy direct alliance write to a human seat -- bug #357's own root cause,
    /// the ~38-alliance cascade the user observed. Removing that write is this task's whole point, and
    /// it correctly leaves a lone AI with nothing else to propose against the shipped world's wealth:
    /// both nations' <c>P(n)</c> truncate to 0 under <see cref="Diplomacy.AiOwnDiplomacyRule.Power"/>'s
    /// own 20,000 divisor, so no war target clears the ratio gate either. The Southern League's wealth is
    /// raised here instead, to clear that gate legitimately through <c>FUN_0044FB7C</c>'s own war-target
    /// search (<c>P(south) = (40000/20000)*(520/100) = 10</c>; <c>P(north) = (400/20000)*(600/100) =
    /// 0</c>; ratio <c>= 8*10/max(1,0) = 80</c>, over the base of 10) -- <c>north</c>'s own wealth is
    /// left at its shipped value. The round still ends with its mandatory blank-line-and-header pair
    /// (news-log-format-and-messages.md Q3: "a quiet round costs 2 of the 40 slots" even with no event at
    /// all). Before the original #98 fix, <c>HandleEnd</c> inferred how many lines to print by counting
    /// news-worthy <em>events</em> — a number the header's own two entries are never backed by, and which
    /// a dash-wrapped event (three log entries for one event) can also disagree with — instead of asking
    /// <see cref="Model.NewsLog"/> how many entries it actually grew by. This asserts the two can never
    /// drift apart again: every entry the log gained this round is printed, and it is provably more than
    /// the header alone.
    /// </summary>
    [Fact]
    public void HandleEnd_prints_every_entry_the_round_actually_appended_not_just_the_header()
    {
        var toy = CoreTestbed.Toy;
        var world = toy.World with
        {
            Nations = ValueList.Of(
                toy.World.NationById("north")!,
                toy.World.NationById("south")! with { Wealth = 40_000 }),
        };

        // The ratio gate is deterministic given the wealth above, but AiMilitaryPhase.ProposeOwnWarDeclaration's
        // own Random(10) roll (report §1a) still has to hit on south's own turn within this fixture's
        // one-round budget -- searched the same way #256's fixture below does for its own budget.
        GameSession? session = null;
        SessionOutput? output = null;
        for (var seed = 1UL; seed <= 2000; seed++)
        {
            var candidate = new GameSession(world, toy.Ruleset, toy.Scenario, seed);
            Assert.Empty(candidate.State.NewsLog.Slots);

            var candidateOutput = candidate.Submit("end");
            if (candidate.State.NewsLog.Slots.Count <= 2)
            {
                // This seed's own Random(10) war-declaration roll did not hit within one round; try the next.
                continue;
            }

            session = candidate;
            output = candidateOutput;
            break;
        }

        Assert.NotNull(session);
        Assert.NotNull(output);

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
    /// <para>
    /// A scripted world, not the shipped toy scenario as-is: the toy world's own armies cannot besiege its
    /// own cities at all (issue #267 -- their populations are far above what either nation's economy can
    /// field). So <c>north</c> here keeps its cities and its nation record (treasury zeroed, so economy
    /// proposes nothing -- only military and diplomacy compete for each pass), but its one starting army
    /// is replaced by a single overwhelming archer force -- the same 400,000-archer count
    /// <c>AiSiegeGateTallyTests.An_adjacent_army_strong_enough_is_recorded_as_a_proposal</c> already proves
    /// clears the ratio gate against an equally fortified, equally or more populous city.
    /// </para>
    /// <para>
    /// <strong>Two rounds, not one (review round 1, B1).</strong> The army starts two tiles from
    /// <c>south</c>'s only city (<c>meridia</c>, at (3, 4)) with exactly one move point, so round 1's
    /// <c>end</c> cannot reach the siege: with north and south still at peace and no economy candidate on
    /// offer, the army marches -- <c>MoveArmyCommandHandler</c> stops it at the bordering tile, and the
    /// spent move point then makes <c>AiMilitaryPhase.Propose</c>'s own <c>army.Moves &lt;= 0</c> gate skip
    /// it for the rest of that turn, so no siege is proposed yet. Only round 2's <c>end</c> -- a fresh
    /// turn, moves replenished, now adjacent -- besieges, captures and eliminates <c>south</c>.
    /// <see href="https://github.com/diegoami/imperial_conquest_2/issues/334">#334</see> N5: an earlier
    /// revision of this paragraph said round 1 also proposed and formed an alliance ahead of the march --
    /// true under T65-era AI diplomacy, but T82 (#377, "AI diplomacy as the original has it") removed the
    /// buggy direct alliance write this relied on, and round 1's AI turn now has no diplomacy candidate to
    /// propose here either (bug #357's own root cause); the army's approach march is genuinely
    /// <c>HandleEnd</c>'s only production this round. This is what makes "every entry the round appended" a
    /// real claim rather than "the whole log": round 1 leaves a non-empty log behind regardless --
    /// <see href="https://github.com/diegoami/imperial_conquest_2/issues/334">#334</see> N4's starting news
    /// seed, plus every round's own mandatory blank-line-and-header pair even with no event at all
    /// (news-log-format-and-messages.md Q3: "a quiet round costs 2 of the 40 slots") --
    /// <c>countBeforeRound2</c> pins it, and round 2's assertions check growth from that mark, not from
    /// empty. <c>south</c> plays first each round, as the human seat, and does nothing but end its turn.
    /// </para>
    /// </remarks>
    [Fact]
    public void HandleEnd_prints_the_dash_wrapped_elimination_from_an_ai_seats_own_turn()
    {
        var toy = CoreTestbed.Toy;
        var world = toy.World with
        {
            // T82 (#359, bug #357) Owns amendment (PR #378): a siege no longer declares war on its own
            // (AiMilitaryPhase now requires the two already at war before it ever proposes one), which
            // this fixture reaches through north's own war-target search instead
            // (AiMilitaryPhase.ProposeOwnWarDeclaration, FUN_0044FB7C §1a) -- so the "DECLARES WAR" news
            // line below still comes from the AI's own declaration, not a pre-set relation. The toy
            // nations' shipped wealth (400/360) is far below AiOwnDiplomacyRule.Power's own 20,000
            // divisor, so north's wealth is raised here to clear the ratio gate
            // (P(north) = (40000/20000)*(600/100) = 12; P(south) = (360/20000)*(520/100) = 0; ratio =
            // 8*12/max(1,0) = 96, comfortably over the base of 10); south's own wealth is left untouched.
            Nations = ValueList.Of(
                toy.World.NationById("north")! with { Treasury = 0, Wealth = 40_000 },
                toy.World.NationById("south")! with { Treasury = 0 }),
            StartingArmies = ValueList.Of(
                new StartingArmy(
                    "north-overwhelming-army", "north", X: 3, Y: 2, Morale: 60, Money: 0, SupplyTons: 0,
                    Moves: 1, Units: ValueList.Of(CaptureFixtures.Unit("archers", 400_000)))),

            // south goes first (human, does nothing but end its turn) so every AI decision happens on
            // north's own turn -- inside HandleEnd's while loop, not the unconditional first RunTurn
            // call, and not a human-issued command flushed before newsBefore is read.
            TurnOrder = ValueList.Of("south", "north"),

            // #334 N4: a news-producing step before round 1. Without this, the log starts empty, so
            // round 1's own printed News section is indistinguishable from "the whole log" -- a fixed
            // "print the last N entries" mutation could still pass simply because before == []. With a
            // starting entry already in the log, HandleEnd's own newsBefore/newsAfter slicing has
            // something to slice away, and a fixed-N mutation that ignores it prints this seed line back
            // out inside round 1's own section.
            StartingNews = new NewsLog(MostRecentSlot: 0, ValueList.Of(new NewsEntry("The chronicle opens."))),
        };
        var scenario = toy.Scenario with
        {
            Seats = ValueList.Of(
                new Seat("south", SeatControl.Human),
                new Seat(
                    "north", SeatControl.Ai,
                    new AiPersonality(Aggression: 0.5, ExpansionDrive: 0.5, LoyaltyToAlliances: 0.5))),
        };

        // The ratio gate is deterministic given the wealth above, but AiMilitaryPhase.ProposeOwnWarDeclaration's
        // own Random(10) roll (report §1a) still has to hit on north's own turn. Rework round 1 (B6): a
        // seed-search loop used to stand in here, with both assertions below turned into a `continue` on
        // the seed that didn't fit -- but the amendment's own words are "both tests' assertions stay as
        // they are", and a skipped assertion is not the same claim as a checked one. Seed 2 is the first
        // of 1..2000 whose Random(10) roll hits within this fixture's two-round budget (found once, by
        // the same search, then hard-coded here) -- it is not a "designed" or otherwise special seed, just
        // the first one that works, so both original assertions can be checked directly instead of
        // filtered around.
        const ulong Seed = 2;
        var session = new GameSession(world, toy.Ruleset, scenario, Seed);
        Assert.Single(session.State.NewsLog.Slots); // #334 N4: the starting news seed, before any round.

        // Round 1: the approach march only (#334 N5: confirmed still accurate -- see the remarks above;
        // an earlier revision of this comment was wrong about an alliance forming here, but that relied
        // on AI diplomacy behaviour T82 has since removed). #334 N4: capturing round 1's own News section
        // -- not just the raw state count -- proves HandleEnd slices *this round's own* appended entries
        // out of a log that already had the starting seed in it. A fixed "print the last N entries"
        // mutation must now match round 1's true count as well as round 2's, and the two differ (asserted
        // below), so no single N satisfies both -- closing the gap the old, empty-at-start fixture left
        // open for whichever N equalled round 2's own count.
        var round1Output = session.Submit("end");
        Assert.False(
            session.State.NationById("south")!.Eliminated,
            "round 1 must only march the army into place and form the alliance, not eliminate south");
        var round1NewsLines = NewsSection(round1Output.Lines.ToList());
        Assert.DoesNotContain(round1NewsLines, line => string.Equals(line, "The chronicle opens.", StringComparison.Ordinal));
        var round1Lines = session.State.NewsLog.Slots.Select(s => s.Text).ToList();
        var countBeforeRound2 = session.State.NewsLog.Slots.Count;
        Assert.True(
            countBeforeRound2 > 1,
            "round 1 must append at least the alliance to the seeded log, or round 2 proves nothing");

        // Round 2: adjacent now, moves replenished -- the siege, the capture and the elimination.
        var output = session.Submit("end");

        Assert.True(
            session.State.NationById("south")!.Eliminated,
            "expected south's only city to fall this round and eliminate it");

        var trimmed = NewsSection(output.Lines.ToList());

        // #334 N4: round 1's own News section has a different line count than round 2's (asserted with
        // its exact value below) -- the two rounds cannot both satisfy a single fixed-N mutation.
        Assert.NotEqual(round1NewsLines.Count, trimmed.Count);

        // The DoD's own claim, made against a log that already has round 1's content in it: every entry
        // THIS round appended is printed -- not the header alone, and not the whole log either. Because
        // round 1 is non-empty, a fixed trailing count that overshoots round 2's own growth pulls in
        // round 1's tail and fails both this count and the exact-sequence check below (shown in the PR
        // for k = 10 and k = 50; k = 7 -- reduced from the original 9, see the T83 rework note below --
        // happens to equal this round's own true count and is expected to still pass).
        var appendedCount = session.State.NewsLog.Slots.Count - countBeforeRound2;
        Assert.Equal(appendedCount, trimmed.Count);

        // Round 1's own (non-blank) content must not reappear in round 2's printed lines. Blank
        // separators are excluded: every round's header includes one, so blank-to-blank equality is
        // meaningless as a leak check.
        foreach (var round1Line in round1Lines)
        {
            var round1Trimmed = round1Line.Trim();
            if (round1Trimmed.Length == 0)
            {
                continue;
            }

            Assert.DoesNotContain(
                trimmed, line => string.Equals(line, round1Trimmed, StringComparison.Ordinal));
        }

        // The exact appended sequence, in order: the war declaration, an ordinary city-capture line, the
        // dash-wrapped elimination, then this round's own single week header -- not merely "contains a
        // dash line somewhere", which a wrong trailing count could still satisfy by accident.
        //
        // T83 rework round 1 (review finding N2): this used to assert 9 lines, with a second empty-plus-
        // "Week" pair at the end. That second pair came from the pre-rework HandleEnd loop replaying north
        // a second time within this same end -- south is eliminated by the end of north's first play here,
        // leaving north the sole surviving seat, and SeatRotationSystem's search then lands back on north
        // itself (nothing else left to rotate to); the old loop's only bound was a raw TurnOrder.Count
        // guard, which never noticed north had already played and called RunTurn on it again, producing a
        // second, superfluous turn -- and since a 2-seat turn order's rotation search passes through index
        // 0 on every single call, that spurious second play produced its own round-tick, hence its own
        // "Week" header. GameSession.cs's PlayUntilOneFullLapOrRepeat now stops the instant a seat would
        // repeat, so north plays exactly once here, as Done-when 2's "no seat played twice" already
        // required elsewhere.
        const string dashLine = "-----------------------------------------------------------";
        Assert.Equal(7, trimmed.Count);
        Assert.Contains("DECLARES WAR", trimmed[0], StringComparison.Ordinal);
        Assert.Contains("falls to", trimmed[1], StringComparison.Ordinal);
        Assert.Equal(dashLine, trimmed[2]);
        Assert.Contains("conquers", trimmed[3], StringComparison.Ordinal);
        Assert.Equal(dashLine, trimmed[4]);
        Assert.Equal(string.Empty, trimmed[5]);
        Assert.Contains("Week", trimmed[6], StringComparison.Ordinal);

        // #334 N4: round 1's own exact News section, pinned the same way round 2's is above -- no event
        // this round (#334 N5, confirmed: no alliance forms), so only the mandatory blank-line-and-header
        // pair every round closes with. Its count (2) is deliberately different from round 2's (7): the
        // DoD's own worry was a fixed "print the last N entries" mutation that happens to equal one
        // round's true count; with both counts pinned and unequal, no single N can satisfy both.
        Assert.Equal(2, round1NewsLines.Count);
        Assert.Equal(string.Empty, round1NewsLines[0]);
        Assert.Contains("Week", round1NewsLines[1], StringComparison.Ordinal);
    }

    /// <summary>Extracts and trims a <c>Submit()</c> output's "News:" section, dropping the trailing
    /// blank separator <c>Submit()</c> always appends after it (#334 N4: shared between round 1 and round
    /// 2's own extraction in <see cref="HandleEnd_prints_the_dash_wrapped_elimination_from_an_ai_seats_own_turn"/>,
    /// so both rounds are read the same way).</summary>
    private static List<string> NewsSection(IReadOnlyList<string> lines)
    {
        var newsIndex = lines.ToList().IndexOf("News:");
        Assert.True(newsIndex >= 0, "Expected a \"News:\" section in the round's output.");

        var newsLines = lines.Skip(newsIndex + 1).ToList();
        newsLines.RemoveAt(newsLines.Count - 1); // Submit()'s own trailing blank separator, not a news entry.
        return newsLines.Select(l => l.Trim()).ToList();
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
