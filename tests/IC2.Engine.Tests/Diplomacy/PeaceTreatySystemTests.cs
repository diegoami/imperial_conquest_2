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

    // ---- Ally-peace cascade (T88, DoD 4, report §3), both branches ----
    //
    // Every point of §3: the partner's own alliance with the ally is reset to -8 UNGATED (point 1); the
    // ally only joins the peace itself if it does not border the enemy and is not human (point 2); the
    // enemy is named first in the news line (point 3); the two halves interleave per ally, not two full
    // passes (point 4).

    private const string WinnerAlly = "winner-ally"; // allied to the winner, at war with the loser

    private static GameState WithAlly(GameState state, Ruleset ruleset, string allyId, string partnerId, string enemyId)
    {
        var codes = ruleset.Diplomacy.StateCodes;
        return state with
        {
            Relations = state.Relations
                .WithRelation(allyId, partnerId, codes.Alliance)
                .WithRelation(allyId, enemyId, codes.War),
        };
    }

    /// <summary>Adds one more nation to <paramref name="state"/>, at peace with everyone, rebuilding the relation matrix to include it while keeping every relation <paramref name="state"/> already carries.</summary>
    private static GameState WithAdditionalNation(GameState state, Ruleset ruleset, NationState nation)
    {
        var codes = ruleset.Diplomacy.StateCodes;
        var nations = ValueList.From(state.Nations.Append(nation));
        var ids = ValueList.From(nations.Select(n => n.Id));
        var relations = DiplomaticRelations.Uniform(ids, codes.Peace);
        foreach (var a in state.Relations.NationIds)
        {
            foreach (var b in state.Relations.NationIds)
            {
                if (string.CompareOrdinal(a, b) < 0)
                {
                    relations = relations.WithRelation(a, b, state.Relations.Get(a, b));
                }
            }
        }

        return state with { Nations = nations, Relations = relations };
    }

    [Fact]
    public void AllyPeaceCascade_AnAllyOfTheLoserStillAtWarWithTheWinner_AlsoMakesPeace()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = WithAlly(BaseState(100, 100, 9000, 900, 6188), ruleset, LoserAlly, Loser, Winner);

        var treaty = new PeaceTreatyTriggered(Winner, Loser, 900, 48);
        var after = PeaceTreatySystem.Apply(state, ruleset, DiplomacyTestbed.Toy.World, treaty, DiplomacyTestbed.Rng());

        // Point 1: the partner's (loser's) own alliance with the ally is reset too, to -8, not the -24 an
        // alliance break would normally map to -- the setter stores a non-zero argument as given.
        Assert.Equal(ruleset.Diplomacy.CooldownAfterAllyPeace, after.Relations.Get(LoserAlly, Loser));
        Assert.NotEqual(ruleset.Diplomacy.CooldownAfterBrokenAlliance, after.Relations.Get(LoserAlly, Loser));

        Assert.Equal(ruleset.Diplomacy.CooldownAfterAllyPeace, after.Relations.Get(LoserAlly, Winner));

        // Point 3: the enemy (the winner, from this ally's own perspective) is named first.
        var lines = after.NewsLog.Slots.Select(e => e.Text).ToList();
        Assert.Contains("Winner and LoserAlly have agreed to end their war.", lines);

        // Two-entity probe: the third party, allied to nobody here, is untouched.
        Assert.Equal(codes.Peace, after.Relations.Get(ThirdParty, Winner));
        Assert.Equal(codes.Peace, after.Relations.Get(ThirdParty, Loser));
    }

    /// <summary>The mirror half: an ally of the winner, still at war with the loser, also makes peace -- loser named first.</summary>
    [Fact]
    public void AllyPeaceCascade_AnAllyOfTheWinnerStillAtWarWithTheLoser_AlsoMakesPeace()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var withWinnerAlly = WithAdditionalNation(
            BaseState(100, 100, 9000, 900, 6188), ruleset, DiplomacyTestbed.Nation(WinnerAlly, "WinnerAlly"));
        var state = WithAlly(withWinnerAlly, ruleset, WinnerAlly, Winner, Loser);

        var treaty = new PeaceTreatyTriggered(Winner, Loser, 900, 48);
        var after = PeaceTreatySystem.Apply(state, ruleset, DiplomacyTestbed.Toy.World, treaty, DiplomacyTestbed.Rng());

        Assert.Equal(ruleset.Diplomacy.CooldownAfterAllyPeace, after.Relations.Get(WinnerAlly, Winner));
        Assert.Equal(ruleset.Diplomacy.CooldownAfterAllyPeace, after.Relations.Get(WinnerAlly, Loser));

        // Point 3: the enemy (the loser, from this ally's own perspective) is named first.
        var lines = after.NewsLog.Slots.Select(e => e.Text).ToList();
        Assert.Contains("Loser and WinnerAlly have agreed to end their war.", lines);
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

        // Not gated on war-with-the-other-side, though: the alliance itself is untouched here too, since
        // there was never a war to make peace out of on this ally's own half.
        Assert.Equal(codes.Alliance, after.Relations.Get(LoserAlly, Loser));
    }

    /// <summary>
    /// Point 2, the border half: an ally that borders the enemy keeps fighting -- but still loses its own
    /// alliance with its partner (point 1's ungated reset fires regardless). The border check is T85's
    /// exact DAT-mask query, <see cref="NeighbourGeography.AreNeighbours"/>.
    /// </summary>
    [Fact]
    public void AllyPeaceCascade_ABorderingAlly_KeepsFighting_ButStillLosesTheAlliance()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = WithAlly(BaseState(100, 100, 9000, 900, 6188), ruleset, LoserAlly, Loser, Winner);
        var world = DiplomacyTestbed.Toy.World with
        {
            StartingNeighbours = ValueList.Of(new NationNeighbours(LoserAlly, ValueList.Of(Winner))),
        };

        var treaty = new PeaceTreatyTriggered(Winner, Loser, 900, 48);
        var after = PeaceTreatySystem.Apply(state, ruleset, world, treaty, DiplomacyTestbed.Rng());

        // Point 1 still fires: the partner alliance is gone regardless of the border.
        Assert.Equal(ruleset.Diplomacy.CooldownAfterAllyPeace, after.Relations.Get(LoserAlly, Loser));

        // Point 2: the border keeps this ally at war, and no news line is written for it.
        Assert.Equal(ruleset.Diplomacy.StateCodes.War, after.Relations.Get(LoserAlly, Winner));
        var lines = after.NewsLog.Slots.Select(e => e.Text).ToList();
        Assert.DoesNotContain(lines, l => l.Contains("LoserAlly", StringComparison.Ordinal) && l.Contains("agreed", StringComparison.Ordinal));
    }

    /// <summary>Point 2, the human half: a human ally keeps fighting too, on the same ungated-reset-but-no-peace terms as a bordering one.</summary>
    [Fact]
    public void AllyPeaceCascade_AHumanAlly_KeepsFighting_ButStillLosesTheAlliance()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Winner, "Winner", unity: 100, wealth: 100),
            DiplomacyTestbed.Nation(Loser, "Loser", unity: 900, wealth: 9000, taxBase: 6188),
            DiplomacyTestbed.Nation(ThirdParty, "ThirdParty"),
            DiplomacyTestbed.Nation(LoserAlly, "LoserAlly", control: SeatControl.Human));
        var army = DiplomacyTestbed.Unit("light_infantry", 5000, 5);
        state = state with
        {
            Armies = ValueList.Of(
                DiplomacyTestbed.Army("winner-army", Winner, 60, army),
                DiplomacyTestbed.Army("loser-army", Loser, 60, army)),
            Relations = state.Relations.WithRelation(Winner, Loser, codes.War),
        };
        state = WithAlly(state, ruleset, LoserAlly, Loser, Winner);

        var treaty = new PeaceTreatyTriggered(Winner, Loser, 900, 48);
        var after = PeaceTreatySystem.Apply(state, ruleset, DiplomacyTestbed.Toy.World, treaty, DiplomacyTestbed.Rng());

        Assert.Equal(ruleset.Diplomacy.CooldownAfterAllyPeace, after.Relations.Get(LoserAlly, Loser));
        Assert.Equal(codes.War, after.Relations.Get(LoserAlly, Winner));
    }

    /// <summary>
    /// Point 4: the two halves interleave per ally, not two full passes -- a loser's ally sorting before a
    /// winner's ally (by <see cref="DiplomaticRelations.NationIds"/> order) has its own news line land
    /// first, even though the winner-side check runs first for any single ally.
    /// </summary>
    [Fact]
    public void AllyPeaceCascade_TheTwoHalvesInterleavePerAlly_NotAsTwoFullPasses()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        const string AAaLoserAlly = "aa-loser-ally"; // sorts before WinnerAlly in NationIds order
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Winner, "Winner", unity: 100, wealth: 100),
            DiplomacyTestbed.Nation(Loser, "Loser", unity: 900, wealth: 9000, taxBase: 6188),
            DiplomacyTestbed.Nation(AAaLoserAlly, "AaLoserAlly"),
            DiplomacyTestbed.Nation(WinnerAlly, "WinnerAlly"));
        var army = DiplomacyTestbed.Unit("light_infantry", 5000, 5);
        state = state with
        {
            Armies = ValueList.Of(
                DiplomacyTestbed.Army("winner-army", Winner, 60, army),
                DiplomacyTestbed.Army("loser-army", Loser, 60, army)),
            Relations = state.Relations.WithRelation(Winner, Loser, ruleset.Diplomacy.StateCodes.War),
        };
        state = WithAlly(state, ruleset, AAaLoserAlly, Loser, Winner);
        state = WithAlly(state, ruleset, WinnerAlly, Winner, Loser);

        var treaty = new PeaceTreatyTriggered(Winner, Loser, 900, 48);
        var after = PeaceTreatySystem.Apply(state, ruleset, DiplomacyTestbed.Toy.World, treaty, DiplomacyTestbed.Rng());

        var lines = after.NewsLog.Slots.Select(e => e.Text).ToList();
        var loserAllyLine = lines.IndexOf("Winner and AaLoserAlly have agreed to end their war.");
        var winnerAllyLine = lines.IndexOf("Loser and WinnerAlly have agreed to end their war.");

        Assert.True(loserAllyLine >= 0 && winnerAllyLine >= 0);
        // AaLoserAlly precedes WinnerAlly in NationIds' own declared order, and the single per-ally loop
        // walks that order -- a two-full-passes implementation would still put every winner-side line
        // first regardless of this ordering, since the treaty's own honourable/sues lines are emitted
        // before the cascade runs at all and do not interfere with this comparison.
        Assert.True(loserAllyLine < winnerAllyLine, "the loser's ally's line must land before the winner's ally's own");
    }
}
