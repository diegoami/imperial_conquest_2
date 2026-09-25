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
