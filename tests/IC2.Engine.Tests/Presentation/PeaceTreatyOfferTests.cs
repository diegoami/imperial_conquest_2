using System.Reflection;
using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// T88, DoD 3, Hazard 1: the CLI's own side of the human-consent post-battle treaty — showing a pending
/// <see cref="Battle.PeaceTreatyOffered"/> offer and answering it with <c>peace-yes</c>/<c>peace-no</c>.
/// </summary>
/// <remarks>
/// <strong>Why the fixture overrides the ruleset and adds a reserve army.</strong> The shipped toy world
/// has only three cities total, so no nation there can ever clear the human-consent gate's
/// <c>cities(loser) &gt; 7</c> test, and its one army per side means the losing side's own post-battle
/// <c>armies(loser)</c> is always 0 once its only army is destroyed — a losing side can never come out
/// stronger than the winner without a second, untouched army. <see cref="OfferFixture"/> keeps the
/// shipped world, scenario and both armies exactly as shipped (so the battle itself is the real
/// <c>north-army-1</c> vs <c>south-army-1</c> fight <see cref="FieldBattleTests"/>' own fixture is not),
/// and adds only: a ruleset override that relaxes the two threshold gates to whatever the toy nations
/// already have and makes the <c>Random(5)</c> draw always favourable (both are ordinary ruleset data,
/// the same technique <c>Season_names_are_read_from_the_rulesets_own_news_log_table</c> already uses),
/// and one extra, untouched reserve army for the side that loses this fight, so
/// <c>armies(winner) &lt; armies(loser)</c> has somewhere to be true. Which side wins is not stipulated
/// by this task: <c>south-army-1</c>'s own power (heavy infantry, high morale) already exceeds
/// <c>north-army-1</c>'s (light infantry and archers) on the shipped fixture, so <c>south</c> (AI) wins
/// and <c>north</c> (the toy scenario's own human seat) is the one offered the treaty, exactly the
/// <c>TBattlePols_InitializeForm</c> wording branch for "after defeating you in battle."
/// </remarks>
public sealed class PeaceTreatyOfferTests
{
    private static GameSession OfferFixture()
    {
        var toy = CoreTestbed.Toy;
        var customRuleset = toy.Ruleset with
        {
            Combat = toy.Ruleset.Combat with
            {
                // Always favourable, and both threshold gates relaxed to whatever the toy fixture already
                // has -- ordinary ruleset data, not a change to InstantBattleResolver's own gate order.
                AutoPeaceChanceNumerator = toy.Ruleset.Combat.AutoPeaceChanceDenominator,
                AutoPeaceLoserUnityThreshold = -1,
                AutoPeaceLoserCityThreshold = 0,
            },
        };

        var reserve = new StartingArmy(
            "north-reserve", "north", X: 2, Y: 1, Morale: 1, Money: 0, SupplyTons: 0, Moves: 5,
            Units: ValueList.Of(new UnitSlot(MercenaryLabel: 0, "heavy_infantry", Troops: 480000, Quality: 5, Name: "Reserve")));

        // south-army-1's shipped position (4,4) is not adjacent to north-army-1's (3,2) -- moved to (4,2),
        // one tile from north-army-1, so attack-army's own adjacency gate passes without an extra move.
        var southArmy = toy.World.StartingArmies.Single(a => a.Id == "south-army-1") with { X = 4, Y = 2 };
        var customWorld = toy.World with
        {
            StartingArmies = ValueList.From(
                toy.World.StartingArmies.Select(a => a.Id == "south-army-1" ? southArmy : a).Append(reserve)),
        };

        return new GameSession(customWorld, customRuleset, toy.Scenario);
    }

    /// <summary>
    /// Rework round 1, B3(b)'s own fixture: <see cref="OfferFixture"/> plus a second, independent
    /// north-vs-south pair -- <c>north-army-2</c> (weak, the same shape as the shipped <c>north-army-1</c>)
    /// at (0,0) attacking <c>south-army-2</c> (strong, the same shape as the shipped <c>south-army-1</c>)
    /// at (1,0) -- placed away from every occupied tile the base fixture already uses (the shipped armies'
    /// own tiles, the reserve at (2,1), and all three toy cities). <c>north-reserve</c> stays untouched by
    /// the first battle, so it alone keeps <c>armies(loser=north) &gt; armies(winner=south)</c> for the
    /// second battle too, with no need for a second reserve -- but its own morale is raised to 40 (not
    /// <see cref="OfferFixture"/>'s 1): <c>ArmyPower.Compute</c> multiplies by morale, and at morale 1 the
    /// reserve's power (measured directly: ~8,500) barely clears a single south army's (~7,500), too thin
    /// a margin once the SECOND battle's own gate sums <em>both</em> of south's surviving armies
    /// (~6,700 combined) against the reserve alone -- moving the reserve's margin firmly ahead of both
    /// battles combined rather than leaving it accidentally tuned to only the first. <c>south-army-2</c>'s
    /// own <c>Moves: 0</c> is deliberate: once the first battle declares war, the AI's own
    /// <c>AiMilitaryPhase.ProposeArmyAttacks</c> would otherwise attack any adjacent enemy army during the
    /// "end" call in between the two battles regardless of the odds (<c>AttackLegality</c> only gates the
    /// attacker's own moves, never the defender's), destroying <c>north-army-2</c> before this test's own
    /// second <c>attack-army</c> ever runs it -- zero moves takes <c>south-army-2</c> out of contention as
    /// an attacker without affecting its being attacked later.
    /// </summary>
    private static GameSession TwoBattleOfferFixture()
    {
        var toy = CoreTestbed.Toy;
        var customRuleset = toy.Ruleset with
        {
            Combat = toy.Ruleset.Combat with
            {
                AutoPeaceChanceNumerator = toy.Ruleset.Combat.AutoPeaceChanceDenominator,
                AutoPeaceLoserUnityThreshold = -1,
                AutoPeaceLoserCityThreshold = 0,
            },
        };

        var reserve = new StartingArmy(
            "north-reserve", "north", X: 2, Y: 1, Morale: 40, Money: 0, SupplyTons: 0, Moves: 5,
            Units: ValueList.Of(new UnitSlot(MercenaryLabel: 0, "heavy_infantry", Troops: 480000, Quality: 5, Name: "Reserve")));

        var northArmy2 = new StartingArmy(
            "north-army-2", "north", X: 0, Y: 0, Morale: 68, Money: 0, SupplyTons: 0, Moves: 5,
            Units: ValueList.Of(new UnitSlot(MercenaryLabel: 0, "light_infantry", Troops: 15000, Quality: 6, Name: "2nd Battalion")));

        var southArmy2 = new StartingArmy(
            "south-army-2", "south", X: 1, Y: 0, Morale: 59, Money: 0, SupplyTons: 0, Moves: 0,
            Units: ValueList.Of(new UnitSlot(MercenaryLabel: 0, "heavy_infantry", Troops: 6000, Quality: 6, Name: "2nd Guards Battalion")));

        var southArmy = toy.World.StartingArmies.Single(a => a.Id == "south-army-1") with { X = 4, Y = 2 };
        var customWorld = toy.World with
        {
            StartingArmies = ValueList.From(
                toy.World.StartingArmies.Select(a => a.Id == "south-army-1" ? southArmy : a)
                    .Append(reserve).Append(northArmy2).Append(southArmy2)),
        };

        return new GameSession(customWorld, customRuleset, toy.Scenario);
    }

    /// <summary>
    /// Rework round 1 (B4)'s own fixture: the mirror image of <see cref="OfferFixture"/>, so the human is
    /// the treaty's <em>winner</em> instead of its loser -- <c>PeaceTreatyOfferDialogText</c>'s "After
    /// losing to you in battle, ... willing to end ..." branch (GameSession.cs, review round 1's own
    /// wording line) is otherwise never exercised by any test in this file. <c>north-army-1</c> is given
    /// <c>south-army-1</c>'s own shipped composition (strong) and vice versa, and the untouched reserve
    /// moves to <c>south</c> instead of <c>north</c>, so <c>armies(loser=south) &gt; armies(winner=north)</c>
    /// once <c>south</c>'s own attacking army is destroyed.
    /// </summary>
    private static GameSession HumanWinsOfferFixture()
    {
        var toy = CoreTestbed.Toy;
        var customRuleset = toy.Ruleset with
        {
            Combat = toy.Ruleset.Combat with
            {
                AutoPeaceChanceNumerator = toy.Ruleset.Combat.AutoPeaceChanceDenominator,
                AutoPeaceLoserUnityThreshold = -1,
                AutoPeaceLoserCityThreshold = 0,
            },
        };

        var southReserve = new StartingArmy(
            "south-reserve", "south", X: 0, Y: 5, Morale: 1, Money: 0, SupplyTons: 0, Moves: 5,
            Units: ValueList.Of(new UnitSlot(MercenaryLabel: 0, "heavy_infantry", Troops: 480000, Quality: 5, Name: "Reserve")));

        // north-army-1 gets south-army-1's own shipped (strong) composition; south-army-1 gets
        // north-army-1's own shipped (weak) composition -- swapped in place, so north wins this fight.
        var strongNorthArmy = toy.World.StartingArmies.Single(a => a.Id == "north-army-1") with
        {
            Units = ValueList.Of(new UnitSlot(MercenaryLabel: 0, "heavy_infantry", Troops: 6000, Quality: 6, Name: "1st Guards Battalion")),
        };
        var weakSouthArmy = toy.World.StartingArmies.Single(a => a.Id == "south-army-1") with
        {
            X = 4,
            Y = 2,
            Units = ValueList.Of(new UnitSlot(MercenaryLabel: 0, "light_infantry", Troops: 15000, Quality: 6, Name: "2nd Foot Battalion")),
        };
        var customWorld = toy.World with
        {
            StartingArmies = ValueList.From(
                toy.World.StartingArmies
                    .Select(a => a.Id == "north-army-1" ? strongNorthArmy : a.Id == "south-army-1" ? weakSouthArmy : a)
                    .Append(southReserve)),
        };

        return new GameSession(customWorld, customRuleset, toy.Scenario);
    }

    /// <summary>
    /// Rework round 1 (B4): the human-winner wording branch (GameSession.cs, review round 1's own line;
    /// mutation M4 replaced its text and left every test green). This is the only test in this file where
    /// the human attacks and wins, so it is the only one that can catch a change to that branch's own text.
    /// </summary>
    [Fact]
    public void Attack_army_HumanWins_UsesTheLosingToYouWording()
    {
        var session = HumanWinsOfferFixture();

        var output = session.Submit("attack-army north-army-1 south-army-1");

        Assert.Contains(
            output.Lines,
            l => l.Contains("After losing to you in battle", StringComparison.Ordinal)
                 && l.Contains("Southern League", StringComparison.Ordinal)
                 && l.Contains("willing to end the war", StringComparison.Ordinal));
        Assert.DoesNotContain(output.Lines, l => l.Contains("After defeating you in battle", StringComparison.Ordinal));
    }

    /// <summary>
    /// Rework round 1 (B4): the offer's other capture site — <c>PlayUntilOneFullLapOrRepeat</c>'s own call
    /// to <c>CapturePeaceTreatyOfferIfAny</c> (GameSession.cs:644), reached only when a battle resolves
    /// inside an AI seat's own turn rather than from a human-issued command. Before this, deleting that
    /// call left every test green (mutation M3): the only battle any test raised went through
    /// <c>IssueCommand</c>'s own site instead. Here the human only declares war; <c>south</c>'s own AI
    /// turn (on <c>end</c>) then attacks the adjacent, weaker <c>north-army-1</c> on its own initiative and
    /// wins, so the offer this test observes could only have come from the AI-turn capture site.
    /// </summary>
    [Fact]
    public void ABattleInsideAnAiSeatsOwnTurn_AlsoCapturesTheOffer()
    {
        var session = OfferFixture();
        session.Submit("declare-war south");
        Assert.Equal(session.Ruleset.Diplomacy.StateCodes.War, session.State.Relations.Get("north", "south"));

        var afterAiTurn = session.Submit("end");

        Assert.Contains(
            afterAiTurn.Lines,
            l => l.Contains("After defeating you in battle", StringComparison.Ordinal)
                 && l.Contains("willing to end the war", StringComparison.Ordinal));
        Assert.Contains(
            afterAiTurn.Lines,
            l => l.Contains("peace-yes", StringComparison.Ordinal) && l.Contains("peace-no", StringComparison.Ordinal));
    }

    [Fact]
    public void Attack_army_raises_the_offer_and_names_how_to_answer_it()
    {
        var session = OfferFixture();

        var output = session.Submit("attack-army north-army-1 south-army-1");

        Assert.Contains(
            output.Lines,
            l => l.Contains("After defeating you in battle", StringComparison.Ordinal)
                 && l.Contains("Southern League", StringComparison.Ordinal)
                 && l.Contains("willing to end the war", StringComparison.Ordinal));
        Assert.Contains(
            output.Lines, l => l.Contains("peace-yes", StringComparison.Ordinal) && l.Contains("peace-no", StringComparison.Ordinal));

        // Still at war -- the offer alone writes nothing.
        Assert.Equal(session.Ruleset.Diplomacy.StateCodes.War, session.State.Relations.Get("north", "south"));
    }

    [Fact]
    public void Peace_yes_writes_the_honourable_peace_and_its_news_with_no_reparations()
    {
        var session = OfferFixture();
        session.Submit("attack-army north-army-1 south-army-1");

        var output = session.Submit("peace-yes");

        Assert.Contains(
            output.Lines, l => l.Contains("diplomacy.accept-peace-treaty accepted", StringComparison.Ordinal));
        Assert.Equal(
            session.Ruleset.Diplomacy.CooldownAfterEndedWar, session.State.Relations.Get("north", "south"));

        var news = session.Submit("news");

        // Not the full "...have agreed to end their war." -- these nation display names ("Southern
        // League"/"Northern League") push the rendered line past the toy ruleset's own MessageByteLength
        // (61 bytes), so the news log's own faithful fixed-length truncation (NewsLogWriter, matching the
        // original's own news buffer) cuts it off before "war." lands. The untruncated prefix is enough
        // to prove the honourable line, not a reparations one, was written.
        Assert.Contains(
            news.Lines,
            l => l.Contains("have agreed to end their", StringComparison.Ordinal)
                 && l.Contains("Southern League", StringComparison.Ordinal)
                 && l.Contains("Northern League", StringComparison.Ordinal));
        Assert.DoesNotContain(news.Lines, l => l.Contains("pays reparations", StringComparison.Ordinal));
        Assert.DoesNotContain(news.Lines, l => l.Contains("sues", StringComparison.Ordinal));
    }

    /// <summary>Answering twice: the offer is consumed by the first answer, whichever it is.</summary>
    [Fact]
    public void Peace_yes_ThenAskedAgain_ReportsNoPendingOffer()
    {
        var session = OfferFixture();
        session.Submit("attack-army north-army-1 south-army-1");
        session.Submit("peace-yes");

        var again = session.Submit("peace-yes");

        Assert.Contains(
            again.Lines, l => l.Contains("There is no pending peace treaty offer.", StringComparison.Ordinal));
    }

    [Fact]
    public void Peace_no_declines_and_writes_nothing_the_war_continues()
    {
        var session = OfferFixture();
        session.Submit("attack-army north-army-1 south-army-1");
        var before = session.State;

        var output = session.Submit("peace-no");

        Assert.Contains(
            output.Lines, l => l.Contains("Peace treaty declined. The war continues.", StringComparison.Ordinal));
        Assert.Equal(session.Ruleset.Diplomacy.StateCodes.War, session.State.Relations.Get("north", "south"));

        // Nothing in the news log changed either -- "No writes nothing" means nothing, not "no relation".
        Assert.Equal(before.NewsLog.Slots.Count, session.State.NewsLog.Slots.Count);

        var again = session.Submit("peace-no");
        Assert.Contains(
            again.Lines, l => l.Contains("There is no pending peace treaty offer.", StringComparison.Ordinal));
    }

    /// <summary>
    /// Rework round 1, B3(a)/(d): the reviewer's own repro -- raise the offer, then end the human's turn
    /// three times without answering. Before this fix, a much later <c>peace-yes</c> still took the
    /// always-honourable branch even though nothing survived of the original's own reasoning for it (the
    /// dialog being modal, so nothing could move between the gate and the human's answer). Ending clears
    /// the offer, so the later <c>peace-yes</c> reports nothing pending, and the war -- untouched the whole
    /// time -- is Hazard 1's "ignoring the offer leaves the war in place" (d).
    /// </summary>
    [Fact]
    public void UnansweredOffer_LapsesOnEnd_SoALaterYesFindsNothingPending_AndTheWarStandsUntouched()
    {
        var session = OfferFixture();
        session.Submit("attack-army north-army-1 south-army-1");
        Assert.Equal(session.Ruleset.Diplomacy.StateCodes.War, session.State.Relations.Get("north", "south"));

        var firstEnd = session.Submit("end");
        Assert.Contains(
            firstEnd.Lines, l => l.Contains("The peace treaty offer has lapsed.", StringComparison.Ordinal));

        session.Submit("end");
        session.Submit("end");

        var wayLater = session.Submit("peace-yes");
        Assert.Contains(
            wayLater.Lines, l => l.Contains("There is no pending peace treaty offer.", StringComparison.Ordinal));
        Assert.Equal(session.Ruleset.Diplomacy.StateCodes.War, session.State.Relations.Get("north", "south"));
    }

    /// <summary>
    /// Rework round 1, B3(b): a dropped offer must not silently suppress every later one for the rest of
    /// the game. Before this fix, <c>CapturePeaceTreatyOfferIfAny</c> stayed a no-op forever once
    /// <c>_pendingPeaceTreatyOffer</c> was first set, because nothing but an answer ever cleared it. The
    /// first battle's offer is left to lapse on <c>end</c>; a second, unrelated battle then still raises
    /// its own offer rather than being silently swallowed.
    /// </summary>
    [Fact]
    public void ExpiredOffer_DoesNotSuppressALaterOne()
    {
        var session = TwoBattleOfferFixture();

        var firstBattle = session.Submit("attack-army north-army-1 south-army-1");
        Assert.Contains(
            firstBattle.Lines, l => l.Contains("Type 'peace-yes'", StringComparison.Ordinal));

        var afterEnd = session.Submit("end");
        Assert.Contains(
            afterEnd.Lines, l => l.Contains("The peace treaty offer has lapsed.", StringComparison.Ordinal));

        var secondBattle = session.Submit("attack-army north-army-2 south-army-2");
        Assert.Contains(
            secondBattle.Lines, l => l.Contains("Type 'peace-yes'", StringComparison.Ordinal));
    }

    /// <summary>
    /// Rework round 1 (B4): "a second offer raised while one is already pending is dropped rather than
    /// replacing it" (<c>_pendingPeaceTreatyOffer</c>'s own remarks) had no test. Both battles run back to
    /// back with no <c>end</c> or answer in between, so the first offer is still pending when the second
    /// battle resolves; the second battle's own output must not show the dialog again.
    /// </summary>
    [Fact]
    public void SecondOfferWhileOnePending_IsDropped()
    {
        var session = TwoBattleOfferFixture();

        var firstBattle = session.Submit("attack-army north-army-1 south-army-1");
        Assert.Contains(firstBattle.Lines, l => l.Contains("Type 'peace-yes'", StringComparison.Ordinal));

        var secondBattle = session.Submit("attack-army north-army-2 south-army-2");
        Assert.DoesNotContain(secondBattle.Lines, l => l.Contains("Type 'peace-yes'", StringComparison.Ordinal));
        Assert.DoesNotContain(secondBattle.Lines, l => l.Contains("willing to end the war", StringComparison.Ordinal));

        // The still-pending offer is the first battle's -- answering it now still works.
        var answer = session.Submit("peace-yes");
        Assert.Contains(
            answer.Lines, l => l.Contains("diplomacy.accept-peace-treaty accepted", StringComparison.Ordinal));
        Assert.Equal(
            session.Ruleset.Diplomacy.CooldownAfterEndedWar, session.State.Relations.Get("north", "south"));
    }

    /// <summary>
    /// Rework round 1 (B4): the session's own re-check that one side is human —
    /// <see cref="CapturePeaceTreatyOfferIfAny"/>'s own remarks: "<c>InstantBattleResolver</c>'s own gate
    /// already restricts it to exactly one human side, but this session checks again rather than trusting
    /// that invariant blindly" — had no test, because the real engine can never actually produce an
    /// all-AI <see cref="PeaceTreatyOffered"/> (the resolver's own <c>exactlyOneHuman</c> gate guarantees
    /// it). Invoked directly, since there is no other seam to reach a defensive check the engine itself
    /// cannot trigger: a fabricated event between two AI-controlled nations must add no dialog and leave
    /// no pending offer.
    /// </summary>
    [Fact]
    public void AllAiPeaceTreatyOffered_IsIgnoredByTheSessionsOwnRecheck()
    {
        var toy = CoreTestbed.Toy;
        var bothAi = toy.Scenario with
        {
            Seats = ValueList.From(toy.Scenario.Seats.Select(s => s with
            {
                Control = SeatControl.Ai,
                Personality = s.Personality ?? new AiPersonality(0.5, 0.5, 0.5),
            })),
        };
        var session = new GameSession(toy.World, toy.Ruleset, bothAi);

        var lines = new List<string>();
        var method = typeof(GameSession).GetMethod(
            "CapturePeaceTreatyOfferIfAny", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(session, new object[] { lines, new DomainEvent[] { new PeaceTreatyOffered("south", "north") } });

        Assert.Empty(lines);

        var answer = session.Submit("peace-yes");
        Assert.Contains(
            answer.Lines, l => l.Contains("There is no pending peace treaty offer.", StringComparison.Ordinal));
    }

    [Fact]
    public void Peace_yes_WithNoPendingOffer_ReportsIt_AndDoesNotDispatchAnything()
    {
        var session = CoreTestbedSession();

        var output = session.Submit("peace-yes");

        Assert.Contains(
            output.Lines, l => l.Contains("There is no pending peace treaty offer.", StringComparison.Ordinal));
    }

    [Fact]
    public void Peace_no_WithNoPendingOffer_ReportsIt()
    {
        var session = CoreTestbedSession();

        var output = session.Submit("peace-no");

        Assert.Contains(
            output.Lines, l => l.Contains("There is no pending peace treaty offer.", StringComparison.Ordinal));
    }

    [Fact]
    public void Help_lists_peace_yes_and_peace_no()
    {
        var session = CoreTestbedSession();

        var output = session.Submit("help");

        Assert.Contains(output.Lines, l => l.Contains("peace-yes", StringComparison.Ordinal));
        Assert.Contains(output.Lines, l => l.Contains("peace-no", StringComparison.Ordinal));
    }

    private static GameSession CoreTestbedSession() =>
        new(CoreTestbed.Toy.World, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.Scenario);
}
