using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy.Commands;

/// <summary>
/// T88, DoD 3: the human's Yes to a pending post-battle treaty offer — always honourable, never
/// reparations, and always through the same ally-peace cascade the AI-vs-AI branch shares.
/// </summary>
public sealed class AcceptPeaceTreatyCommandTests
{
    private const string Winner = "winner-nation";
    private const string Loser = "loser-nation";
    private const string ThirdParty = "third-party";

    private static GameState WarState(SeatControl winnerControl, SeatControl loserControl)
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Winner, "Winner", control: winnerControl, unity: 900, wealth: 9000),
            DiplomacyTestbed.Nation(Loser, "Loser", control: loserControl, unity: 900, wealth: 9000),
            DiplomacyTestbed.Nation(ThirdParty, "ThirdParty"));

        return state with
        {
            Relations = state.Relations.WithRelation(Winner, Loser, ruleset.Diplomacy.StateCodes.War),
        };
    }

    [Fact]
    public void Yes_AlwaysWritesTheHonourableBranch_NeverReparations()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = WarState(SeatControl.Human, SeatControl.Ai);
        var winnerTreasuryBefore = state.NationById(Winner)!.Treasury;
        var loserTreasuryBefore = state.NationById(Loser)!.Treasury;

        var command = new AcceptPeaceTreatyCommand(Winner, Winner, Loser);
        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, command);

        Assert.True(result.IsAccepted);
        Assert.Equal(ruleset.Diplomacy.CooldownAfterEndedWar, result.State.Relations.Get(Winner, Loser));
        Assert.Equal(winnerTreasuryBefore, result.State.NationById(Winner)!.Treasury);
        Assert.Equal(loserTreasuryBefore, result.State.NationById(Loser)!.Treasury);

        var lines = result.State.NewsLog.Slots.Select(e => e.Text).ToList();
        Assert.Contains("Winner and Loser have agreed to end their war.", lines);
        Assert.DoesNotContain(lines, l => l.Contains("pays reparations", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("sues", StringComparison.Ordinal));
    }

    /// <summary>Issued by whichever side is human -- here the loser answers its own winner's offer.</summary>
    [Fact]
    public void Yes_CanBeIssuedByTheLoserSide_WhenTheLoserIsTheHumanSeat()
    {
        // ActiveSeatIndex 1: the loser (the human side here) is the one "at the prompt" to answer.
        var state = WarState(SeatControl.Ai, SeatControl.Human) with { ActiveSeatIndex = 1 };
        var command = new AcceptPeaceTreatyCommand(Loser, Winner, Loser);
        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, command);

        Assert.True(result.IsAccepted);
        Assert.Equal(DiplomacyTestbed.Ruleset.Diplomacy.CooldownAfterEndedWar, result.State.Relations.Get(Winner, Loser));
    }

    [Fact]
    public void Yes_UnknownNation_IsRejected()
    {
        var state = WarState(SeatControl.Human, SeatControl.Ai);
        var command = new AcceptPeaceTreatyCommand(Winner, Winner, "nobody");
        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, command);

        Assert.True(result.IsRejected);
        Assert.Equal(AcceptPeaceTreatyRejections.UnknownNation, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Yes_WhenNoLongerAtWar_IsRejected()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = WarState(SeatControl.Human, SeatControl.Ai) with
        {
            Relations = WarState(SeatControl.Human, SeatControl.Ai).Relations
                .WithRelation(Winner, Loser, ruleset.Diplomacy.StateCodes.Peace),
        };

        var command = new AcceptPeaceTreatyCommand(Winner, Winner, Loser);
        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, command);

        Assert.True(result.IsRejected);
        Assert.Equal(AcceptPeaceTreatyRejections.NotAtWar, result.Code);
    }

    /// <summary>The ally-peace cascade fires exactly as the AI-vs-AI branch's own does.</summary>
    [Fact]
    public void Yes_AlsoRunsTheAllyPeaceCascade()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = WarState(SeatControl.Human, SeatControl.Ai);
        const string LoserAlly = "loser-ally";
        state = state with
        {
            Nations = ValueList.From(state.Nations.Append(DiplomacyTestbed.Nation(LoserAlly, "LoserAlly"))),
            Relations = DiplomaticRelations.Uniform(
                    ValueList.From(state.Nations.Select(n => n.Id).Append(LoserAlly)), codes.Peace)
                .WithRelation(Winner, Loser, codes.War)
                .WithRelation(LoserAlly, Loser, codes.Alliance)
                .WithRelation(LoserAlly, Winner, codes.War),
        };

        var command = new AcceptPeaceTreatyCommand(Winner, Winner, Loser);
        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, command);

        Assert.True(result.IsAccepted);
        Assert.Equal(ruleset.Diplomacy.CooldownAfterAllyPeace, result.State.Relations.Get(LoserAlly, Loser));
        Assert.Equal(ruleset.Diplomacy.CooldownAfterAllyPeace, result.State.Relations.Get(LoserAlly, Winner));
    }
}
