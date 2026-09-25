using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// <c>docs/task-catalogue.md</c> T19 DoD 7 and DoD 8: reacting to T16's <see cref="PeaceTreatyTriggered"/>
/// with the reparations sequence or the honourable-peace line, and the ally-peace cascade both branches
/// share.
/// </summary>
public sealed class PeaceTreatySystemTests
{
    private const string Winner = "winner-nation";
    private const string Loser = "loser-nation";
    private const string ThirdParty = "third-party"; // survives untouched
    private const string LoserAlly = "loser-ally"; // allied to the loser, at war with the winner

    private static GameState BaseState(int winnerWealth, int winnerUnity, int loserWealth, int loserUnity, int loserTaxBase)
    {
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Winner, "Winner", unity: winnerUnity, wealth: winnerWealth),
            DiplomacyTestbed.Nation(Loser, "Loser", unity: loserUnity, wealth: loserWealth, taxBase: loserTaxBase),
            DiplomacyTestbed.Nation(ThirdParty, "ThirdParty"),
            DiplomacyTestbed.Nation(LoserAlly, "LoserAlly"));

        var army = DiplomacyTestbed.Unit("light_infantry", 5000, 5);
        state = state with
        {
            Armies = ValueList.Of(
                DiplomacyTestbed.Army("winner-army", Winner, 60, army),
                DiplomacyTestbed.Army("loser-army", Loser, 60, army)),
        };

        // Every treaty this build produces fires after a battle set the pair to war (DoD 5).
        return state with
        {
            Relations = state.Relations.WithRelation(Winner, Loser, DiplomacyTestbed.Ruleset.Diplomacy.StateCodes.War),
        };
    }

    // ---- DoD 7: reparations branch ----

    [Fact]
    public void DoD07_ReparationsBranch_DebitsLoser_CreditsWinner_AndWritesTheConfirmedNewsSequence()
    {
        var ruleset = DiplomacyTestbed.Ruleset;

        // Winner stronger on both score and army -> not honourable -> reparations.
        var state = BaseState(winnerWealth: 9000, winnerUnity: 900, loserWealth: 100, loserUnity: 100, loserTaxBase: 6188);
        state = state with
        {
            Armies = ValueList.Of(
                DiplomacyTestbed.Army("winner-army", Winner, 60, DiplomacyTestbed.Unit("light_infantry", 90000, 5)),
                DiplomacyTestbed.Army("loser-army", Loser, 60, DiplomacyTestbed.Unit("light_infantry", 100, 5))),
        };

        var winnerTreasuryBefore = state.NationById(Winner)!.Treasury;
        var loserTreasuryBefore = state.NationById(Loser)!.Treasury;

        var treaty = new PeaceTreatyTriggered(Winner, Loser, LoserUnity: 100, LoserCityCount: 48);
        var after = PeaceTreatySystem.Apply(state, ruleset, DiplomacyTestbed.Toy.World, treaty, DiplomacyTestbed.Rng());

        var reparations = after.NationById(Winner)!.Treasury - winnerTreasuryBefore;
        Assert.True(reparations > 0);
        Assert.Equal(loserTreasuryBefore - reparations, after.NationById(Loser)!.Treasury);
        Assert.InRange(reparations, 2027, 3573); // the Ptolemaic-shaped range for W=6188, 48 cities

        Assert.Equal(ruleset.Diplomacy.CooldownAfterEndedWar, after.Relations.Get(Winner, Loser));

        var lines = after.NewsLog.Slots.Select(e => e.Text).ToList();
        Assert.Contains(lines, l => l.Contains("sues", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("ends all current trading agreements", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("ends all current alliances", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("pays reparations of", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains($"{reparations:N0}", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("have agreed to end their war", StringComparison.Ordinal));
    }

    /// <summary>The loser's other trade/alliance partners cool to -10; a third, unrelated nation is untouched.</summary>
    [Fact]
    public void DoD07_TheLosersOtherPartners_CoolToMinusTen_AndAnUnrelatedNationIsUntouched()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = BaseState(9000, 900, 100, 100, 6188) with
        {
            Armies = ValueList.Of(
                DiplomacyTestbed.Army("winner-army", Winner, 60, DiplomacyTestbed.Unit("light_infantry", 90000, 5)),
                DiplomacyTestbed.Army("loser-army", Loser, 60, DiplomacyTestbed.Unit("light_infantry", 100, 5))),
        };
        state = state with { Relations = state.Relations.WithRelation(Loser, ThirdParty, codes.Trade) };

        var treaty = new PeaceTreatyTriggered(Winner, Loser, 100, 48);
        var after = PeaceTreatySystem.Apply(state, ruleset, DiplomacyTestbed.Toy.World, treaty, DiplomacyTestbed.Rng());

        Assert.Equal(ruleset.Diplomacy.CooldownAfterPeaceTerms, after.Relations.Get(Loser, ThirdParty));
    }

    // ---- DoD 8: honourable branch ----

    [Fact]
    public void DoD08_HonourableBranch_PaysNothing_AndWritesOnlyTheHonourableLine()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = BaseState(winnerWealth: 100, winnerUnity: 100, loserWealth: 9000, loserUnity: 900, loserTaxBase: 6188);

        var winnerTreasuryBefore = state.NationById(Winner)!.Treasury;
        var loserTreasuryBefore = state.NationById(Loser)!.Treasury;

        var treaty = new PeaceTreatyTriggered(Winner, Loser, 900, 48);
        var after = PeaceTreatySystem.Apply(state, ruleset, DiplomacyTestbed.Toy.World, treaty, DiplomacyTestbed.Rng());

        Assert.Equal(winnerTreasuryBefore, after.NationById(Winner)!.Treasury);
        Assert.Equal(loserTreasuryBefore, after.NationById(Loser)!.Treasury);

        var lines = after.NewsLog.Slots.Select(e => e.Text).ToList();
        Assert.Contains(lines, l => l.Contains("have agreed to end their war", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("pays reparations", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("sues", StringComparison.Ordinal));
    }

    // ---- Ally-peace cascade, both branches ----

    [Fact]
    public void AllyPeaceCascade_AnAllyOfTheLoserStillAtWarWithTheWinner_AlsoMakesPeace()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = BaseState(100, 100, 9000, 900, 6188);
        state = state with
        {
            Relations = state.Relations
                .WithRelation(LoserAlly, Loser, codes.Alliance)
                .WithRelation(LoserAlly, Winner, codes.War),
        };

        var treaty = new PeaceTreatyTriggered(Winner, Loser, 900, 48);
        var after = PeaceTreatySystem.Apply(state, ruleset, DiplomacyTestbed.Toy.World, treaty, DiplomacyTestbed.Rng());

        Assert.Equal(ruleset.Diplomacy.CooldownAfterAllyPeace, after.Relations.Get(LoserAlly, Winner));

        var lines = after.NewsLog.Slots.Select(e => e.Text).ToList();
        Assert.Contains(lines, l => l.Contains("have agreed to end their war", StringComparison.Ordinal));

        // Two-entity probe: the third party, allied to nobody here, is untouched.
        Assert.Equal(codes.Peace, after.Relations.Get(ThirdParty, Winner));
        Assert.Equal(codes.Peace, after.Relations.Get(ThirdParty, Loser));
    }

    /// <summary>Mutation proof: an ally NOT at war with the other side is left alone -- no blanket sweep.</summary>
    [Fact]
    public void AllyPeaceCascade_AnAllyNotAtWarWithTheOtherSide_IsNotTouched()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = BaseState(100, 100, 9000, 900, 6188);
        state = state with { Relations = state.Relations.WithRelation(LoserAlly, Loser, codes.Alliance) };

        var treaty = new PeaceTreatyTriggered(Winner, Loser, 900, 48);
        var after = PeaceTreatySystem.Apply(state, ruleset, DiplomacyTestbed.Toy.World, treaty, DiplomacyTestbed.Rng());

        Assert.Equal(codes.Peace, after.Relations.Get(LoserAlly, Winner));
    }
}
