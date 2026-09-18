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

    /// <summary>
    /// Review round 1, N4: renamed from "...ResetsOnlyThe..." -- the toy world has only two nations, so
    /// there is no third nation's row to assert is left untouched at this end-to-end level. The "only"
    /// half of the claim (values outside <c>[-5, -1]</c> are never reset) is pinned at the unit level by
    /// <see cref="DepositionTests.ResetRelations_OutsideTheRange_IsLeftUnchanged"/>; this test covers the
    /// in-range half wired through the real handler.
    /// </summary>
    [Fact]
    public void OnQuarterBoundary_ResetsTheDeposedNationsCloseCooldown()
    {
        var state = WithSouth(treasury: -21_000, unity: 470, wealth: 0);
        state = state with { Relations = state.Relations.WithRelation("north", "south", -3) };
        var rng = new ScriptedRng(nextChanceDraws: new[] { true });

        var after = new AiDepositionHandler().OnQuarterBoundary(Context(state, rng));

        Assert.Equal(0, after.Relations.Get("north", "south"));
    }

    /// <summary>
    /// Review round 1, N5: makes the registration-order assertion
    /// (<see cref="EconomySystemRegistrationTests.AiDepositionHandler_RegistersAfterTheNationTick"/>)
    /// behavioural rather than structural, wired through the real
    /// <see cref="Core.TurnCoordinator.FireQuarterBoundary"/> with billing, the city tick, the nation tick
    /// and this handler all registered together. South starts already in debt on its own stored figures
    /// (treasury -830, its pre-rebuild wealth 360) -- but this quarter's tax-base/wealth rebuild
    /// (<see cref="QuarterlyCityEconomySystem"/>, which moves the debt line itself: wealth rebuilds to
    /// 462,000, so the line moves from 0 to -924) and its income credit
    /// (<see cref="QuarterlyNationEconomySystem"/>, +4 net of this quarter's own -96 upkeep) land it at
    /// treasury -922, one talent inside the now-much-wider line. Because <see cref="AiDepositionHandler"/>
    /// runs last (order 300), it reads the settled figures and correctly finds the nation clear -- if it
    /// ran any earlier in the chain, per-Owns files aside, it would have found the nation still in debt.
    /// Review round 2, B2: the state's <see cref="Model.GameState.RandomSeed"/> is pinned to 13 -- see the
    /// in-body comment for why a seed choice, not just the order, decides whether this test can catch a
    /// mis-ordering at all.
    /// </summary>
    [Fact]
    public void OnQuarterBoundary_ReadsThisQuartersSettledFigures_NotTheStaleOnesFromBeforeItRan()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var state = EconomyTestbed.InitialState();
        var nations = state.Nations.Select(n => n.Id == "south" ? n with { Treasury = -830, Unity = 990 } : n);

        // Review round 2, B2: pinned to a seed where the 1-in-9 deposition draw actually hits. At the
        // correct order (this handler last, after the nation tick's credit) this nation is never in debt
        // by the time it is checked, so no draw is ever attempted and the seed does not matter to the
        // passing case. But the whole point of this test is to fail if the handler ran *before* the
        // credit instead -- and mis-ordered, this nation *would* be in debt when checked, so whether the
        // mutation is actually caught depends on whether that draw hits. The default seed happened not to
        // (verified: a mis-ordered run at the default seed still lands on -922, indistinguishable from
        // correct); seed 13 hits it, so a mis-ordered run instead deposes the nation mid-pipeline and
        // resets its treasury to 0 before the nation tick's own +4 credit lands on top of that (giving 4,
        // not -922) -- the divergence this test exists to catch. Do not tidy this away: without a seed
        // chosen for this reason, the test is inert at roughly eight seeds in nine.
        state = state with { Nations = ValueList.From(nations), RandomSeed = 13 };

        // On this nation's own stored (pre-quarter) figures, it already reads as in debt -- the treasury
        // is deeply negative against a wealth-based line that has not yet been rebuilt this quarter.
        Assert.True(Deposition.InDebt(state.NationById("south")!, ruleset));

        var sink = new RecordingEventSink();
        var coordinator = EconomyTestbed.CoordinatorOnly(
            sink,
            typeof(QuarterlyEconomySystem),
            typeof(QuarterlyCityEconomySystem),
            typeof(QuarterlyNationEconomySystem),
            typeof(AiDepositionHandler));

        var after = coordinator.FireQuarterBoundary(state, endingSeasonIndex: 0);
        var southAfter = after.NationById("south")!;

        // This quarter's own rebuild and credit clear it before the deposition check ever runs.
        Assert.False(Deposition.InDebt(southAfter, ruleset));
        Assert.Equal(-922, southAfter.Treasury);
        Assert.Equal(462_000, southAfter.Wealth); // the rebuild that moves the debt line itself.
        Assert.Empty(sink.Events.OfType<AiLeaderDeposed>());
    }
}
