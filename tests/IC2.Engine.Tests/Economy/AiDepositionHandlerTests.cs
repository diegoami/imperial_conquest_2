using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T39 Quarterly upkeep: who pays, mercenary desertion, and deposition
/// for debt", Done-when 6: "AI deposition fires only when in debt and <c>Random(9) == 0</c>, using a
/// stub RNG."
/// </summary>
public sealed class AiDepositionHandlerTests
{
    private static QuarterBoundaryContext Context(GameState state, IRng rng, RecordingEventSink? sink = null) =>
        new(state, EconomyTestbed.Ruleset, EconomyTestbed.Toy.World, EndingSeasonIndex: 0, rng, sink ?? new RecordingEventSink());

    private static GameState WithSouth(int treasury, int unity, int wealth)
    {
        var state = EconomyTestbed.InitialState();
        var nations = state.Nations.Select(n => n.Id == "south" ? n with { Treasury = treasury, Unity = unity, Wealth = wealth } : n);
        return state with { Nations = ValueList.From(nations) };
    }

    [Fact]
    public void OnQuarterBoundary_InDebtAndTheDrawHits_DeposesAndPublishesTheNewsEvent()
    {
        var state = WithSouth(treasury: -21_000, unity: 470, wealth: 0); // in debt on the flat -20,000 arm.
        var rng = new ScriptedRng(nextChanceDraws: new[] { true }, expectedNextChanceOdds: new[] { (1, 9) });
        var sink = new RecordingEventSink();

        var after = new AiDepositionHandler().OnQuarterBoundary(Context(state, rng, sink));
        var south = after.NationById("south")!;

        Assert.Equal(0, south.Treasury); // negative -> floored to zero.
        Assert.Equal(550, south.Unity); // min(550, 470 + 150).

        var published = Assert.Single(sink.Events.OfType<AiLeaderDeposed>());
        Assert.Equal(south.Name, published.Nation);
        Assert.Equal(south.LeaderName, published.LeaderName);
    }

    [Fact]
    public void OnQuarterBoundary_InDebtButTheDrawMisses_LeavesTheNationUntouched()
    {
        var state = WithSouth(treasury: -21_000, unity: 470, wealth: 0);
        var before = state.NationById("south")!;
        var rng = new ScriptedRng(nextChanceDraws: new[] { false });
        var sink = new RecordingEventSink();

        var after = new AiDepositionHandler().OnQuarterBoundary(Context(state, rng, sink));

        Assert.Equal(before, after.NationById("south"));
        Assert.Empty(sink.Events.OfType<AiLeaderDeposed>());
    }

    [Fact]
    public void OnQuarterBoundary_NotInDebt_DrawsNothingAtAll()
    {
        var state = WithSouth(treasury: 1_000, unity: 990, wealth: 0);
        var before = state.NationById("south")!;

        // No draw scripted: this proves the roll is made only for an at-risk nation, not unconditionally.
        var rng = new ScriptedRng();

        var after = new AiDepositionHandler().OnQuarterBoundary(Context(state, rng));

        Assert.Equal(before, after.NationById("south"));
    }

    [Fact]
    public void OnQuarterBoundary_AHumanControlledNation_IsNeverConsideredHere()
    {
        // north is the toy scenario's human seat: even deeply in debt, this handler must leave it alone
        // -- HumanDepositionSystem is its own, separate path.
        var state = EconomyTestbed.InitialState();
        var nations = state.Nations.Select(n => n.Id == "north" ? n with { Treasury = -50_000, Unity = 100 } : n);
        state = state with { Nations = ValueList.From(nations) };
        var before = state.NationById("north")!;

        var rng = new ScriptedRng(); // no draw expected for a human seat.
        var after = new AiDepositionHandler().OnQuarterBoundary(Context(state, rng));

        Assert.Equal(before, after.NationById("north"));
    }

    [Fact]
    public void OnQuarterBoundary_ANationAtUnityZero_IsSkipped_MatchingTheNationLoopsOwnGuard()
    {
        var state = WithSouth(treasury: -50_000, unity: 0, wealth: 0);
        var before = state.NationById("south")!;

        var rng = new ScriptedRng(); // the whole nation-loop body is gated on unity > 0.
        var after = new AiDepositionHandler().OnQuarterBoundary(Context(state, rng));

        Assert.Equal(before, after.NationById("south"));
    }

    [Fact]
    public void OnQuarterBoundary_ResetsOnlyTheDeposedNationsCloseCooldowns()
    {
        var state = WithSouth(treasury: -21_000, unity: 470, wealth: 0);
        state = state with { Relations = state.Relations.WithRelation("north", "south", -3) };
        var rng = new ScriptedRng(nextChanceDraws: new[] { true });

        var after = new AiDepositionHandler().OnQuarterBoundary(Context(state, rng));

        Assert.Equal(0, after.Relations.Get("north", "south"));
    }
}
