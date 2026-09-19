using IC2.Engine.Battle;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;
using static IC2.Engine.Tests.Battle.Commands.BattleCommandTestbed;

namespace IC2.Engine.Tests.Battle.Commands;

/// <summary>
/// <c>docs/task-catalogue.md</c> T54, Done-when 1 and Done-when 5: <see cref="AttackArmyCommand"/>
/// dispatches to <see cref="InstantBattleResolver.ResolveField"/> and returns its outcome through the
/// command layer, with every gate decided <em>before</em> the resolver runs.
/// </summary>
/// <remarks>
/// Nothing here asserts a combat number. T16 owns those and pins them in
/// <c>tests/IC2.Engine.Tests/Battle/FieldBattleTests.cs</c>; what these tests pin is that the command
/// reaches that resolver with the arguments the dispatcher gives it and hands back exactly what it
/// produced — <see cref="DoD01_TheCommandReturnsTheMergedResolversOwnOutcome"/> compares the whole state
/// against a hand-run of the resolver on the dispatcher's own stream, so a reimplemented rule of any kind
/// would fail it.
/// </remarks>
public sealed class AttackArmyCommandTests
{
    private const string AttackerId = "north-attacker";
    private const string DefenderId = "south-defender";

    /// <summary>
    /// Two armies on adjoining tiles: north's is much the stronger, so the attacker wins and the
    /// outcome is visible in the state. At peace until a test says otherwise.
    /// </summary>
    private static GameState AtPeaceFixture()
    {
        var attacker = BattleTestbed.Army(
            AttackerId, NorthNationId, 2, 3, morale: 68, money: 40, supplyTons: 10,
            BattleTestbed.Unit("heavy_infantry", 9_000, 6, "1st Guards Battalion"));
        var defender = BattleTestbed.Army(
            DefenderId, SouthNationId, 3, 3, morale: 50, money: 25, supplyTons: 8,
            BattleTestbed.Unit("light_infantry", 3_000, 6, "2nd Foot Battalion"));

        return BattleTestbed.StateWith(armies: new[] { attacker, defender });
    }

    private static GameState Fixture() => AtWar(AtPeaceFixture(), NorthNationId, SouthNationId);

    private static AttackArmyCommand Attack(string attacker = AttackerId, string target = DefenderId) =>
        new(NorthNationId, attacker, target);

    /// <summary>
    /// Done-when 1: the command's accepted state is, field for field, the state
    /// <see cref="InstantBattleResolver.ResolveField"/> produces when run by hand on the very stream the
    /// dispatcher hands the handler. The only permitted difference is
    /// <see cref="GameState.RandomSeed"/>, which the dispatch seam advances for every accepted command.
    /// </summary>
    [Fact]
    public void DoD01_TheCommandReturnsTheMergedResolversOwnOutcome()
    {
        var state = Fixture();
        var command = Attack();

        var direct = InstantBattleResolver.ResolveField(
            state, AttackerId, DefenderId, ToyRuleset, ToyWorld, RngFor(state, command.Kind), NullEventSink.Instance);

        var sink = new RecordingEventSink();
        var result = Dispatcher(sink).Dispatch(state, command);

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(
            GameJson.Serialize(direct.State),
            GameJson.Serialize(result.State with { RandomSeed = direct.State.RandomSeed }));

        // And the resolver's own result reached the caller, rather than being computed and dropped.
        var resolved = Assert.Single(sink.Events.OfType<BattleResolved>());
        Assert.Equal(direct.Result, resolved.Result);
        Assert.Equal(BattleKind.Field, resolved.Result.Kind);

        // The loser is gone from the state the command returned -- the outcome, not just the event.
        Assert.Null(result.State.ArmyById(DefenderId));
        Assert.Equal(0, result.State.ArmyById(AttackerId)!.Moves);
    }

    /// <summary>Done-when 1's adjacency gate: one tile further out and the attack is refused.</summary>
    [Fact]
    public void DoD01_AnAttackOnANonAdjacentArmyIsRefused()
    {
        var state = Fixture();
        state = state with
        {
            Armies = ValueList.From(state.Armies.Select(a =>
                a.Id == DefenderId ? a with { X = 5, Y = 3 } : a)),
        };

        AssertRefused(state, Attack(), AttackArmyRejections.NotAdjacent);
    }

    /// <summary>
    /// The boundary the gate actually draws: a diagonal neighbour is adjacent (Chebyshev, not Manhattan),
    /// and two tiles away is not. Without this the gate could be any of three metrics and still pass the
    /// test above.
    /// </summary>
    [Theory]
    [InlineData(3, 4, true)]   // diagonal neighbour
    [InlineData(2, 2, true)]   // orthogonal neighbour
    [InlineData(4, 5, false)]  // two tiles diagonally
    [InlineData(4, 3, false)]  // two tiles across
    public void DoD01_AdjacencyIsChebyshevDistanceOne(int defenderX, int defenderY, bool expectedLegal)
    {
        var state = Fixture();
        state = state with
        {
            Armies = ValueList.From(state.Armies.Select(a =>
                a.Id == DefenderId ? a with { X = defenderX, Y = defenderY } : a)),
        };

        Assert.Equal(expectedLegal, AttackLegality.IsLegal(state, ToyRuleset, Attack()));
        Assert.Equal(expectedLegal, Dispatcher().Dispatch(state, Attack()).IsAccepted);
    }

    /// <summary>Done-when 1's moves gate: an army that has already acted cannot attack.</summary>
    [Fact]
    public void DoD01_AnAttackerWithNoMovesLeftIsRefused()
    {
        var state = Fixture();
        state = state with
        {
            Armies = ValueList.From(state.Armies.Select(a =>
                a.Id == AttackerId ? a with { Moves = 0 } : a)),
        };

        AssertRefused(state, Attack(), AttackArmyRejections.NoMovesLeft);
    }

    /// <summary>
    /// Done-when 1's embarkation gate. <see cref="InstantBattleResolver.ResolveField"/> <em>throws</em>
    /// on an embarked army, so without this gate an ordinary illegal order would take the turn down
    /// instead of coming back as a refusal.
    /// </summary>
    [Fact]
    public void DoD01_AnEmbarkedAttackerIsRefused()
    {
        var state = Fixture();
        var carrier = BattleTestbed.Fleet("north-fleet", NorthNationId, 2, 3, ships: 30, conditionPercent: 90);
        state = state with
        {
            Fleets = ValueList.Of(carrier),
            Armies = ValueList.From(state.Armies.Select(a =>
                a.Id == AttackerId ? a with { AboardFleetId = "north-fleet", CoveredTileCode = null } : a)),
        };

        AssertRefused(state, Attack(), AttackArmyRejections.AttackerEmbarked);
    }

    /// <summary>The mirror of the gate above, for the same reason: the resolver throws on either side.</summary>
    [Fact]
    public void DoD01_AnEmbarkedTargetIsRefused()
    {
        var state = Fixture();
        var carrier = BattleTestbed.Fleet("south-fleet", SouthNationId, 3, 3, ships: 30, conditionPercent: 90);
        state = state with
        {
            Fleets = ValueList.Of(carrier),
            Armies = ValueList.From(state.Armies.Select(a =>
                a.Id == DefenderId ? a with { AboardFleetId = "south-fleet", CoveredTileCode = null } : a)),
        };

        AssertRefused(state, Attack(), AttackArmyRejections.TargetEmbarked);
    }

    /// <summary>Done-when 1's war gate — and the reason Done-when 5 is checkable at all.</summary>
    [Fact]
    public void DoD01_AnAttackOnANationNotAtWarIsRefused()
    {
        AssertRefused(AtPeaceFixture(), Attack(), AttackArmyRejections.NotAtWar);
    }

    /// <summary>
    /// The three structural refusals, each of which is also a condition
    /// <see cref="InstantBattleResolver.ResolveField"/> throws on.
    /// </summary>
    [Fact]
    public void DoD01_UnknownAndSameNationTargetsAreRefused()
    {
        var state = Fixture();

        AssertRefused(state, Attack(attacker: "no-such-army"), AttackArmyRejections.UnknownArmy);
        AssertRefused(state, Attack(target: "no-such-army"), AttackArmyRejections.UnknownTarget);
        AssertRefused(state, Attack(target: AttackerId), AttackArmyRejections.SameNation);
        AssertRefused(
            state, new AttackArmyCommand(NorthNationId, DefenderId, AttackerId), AttackArmyRejections.NotYourArmy);
    }

    /// <summary>
    /// <strong>Done-when 5, and the close of
    /// <see href="https://github.com/diegoami/imperial_conquest_2/issues/200">#200</see>'s N2.</strong>
    /// T19's Done-when 5 — "attacking sets the relation to war <em>before</em> the battle resolves" — has
    /// never had a runnable check, because nothing could attack; T19 composed <c>DeclareWar</c> with the
    /// resolver directly and said so. This asserts the ordering end to end through the command layer, in
    /// both directions: at peace <em>no battle resolves at all</em> (the state is untouched and the sink
    /// is empty), and the battle that does resolve resolves against a state that is already at war.
    /// </summary>
    [Fact]
    public void DoD05_NoBattleResolvesUntilTheRelationIsWar()
    {
        var war = ToyRuleset.Diplomacy.StateCodes.War;
        var peaceState = AtPeaceFixture();
        var sink = new RecordingEventSink();
        var dispatcher = Dispatcher(sink);

        var refused = dispatcher.Dispatch(peaceState, Attack());
        Assert.True(refused.IsRejected);
        Assert.Equal(AttackArmyRejections.NotAtWar, refused.Code);
        Assert.Same(peaceState, refused.State);
        Assert.Empty(sink.Events.OfType<BattleResolved>());
        Assert.NotNull(refused.State.ArmyById(DefenderId));

        // The original's own sequence, as two commands: declare, then attack.
        var declared = dispatcher.Dispatch(peaceState, new DeclareWarCommand(NorthNationId, SouthNationId));
        Assert.True(declared.IsAccepted, declared.ToString());
        Assert.Equal(war, declared.State.Relations.Get(NorthNationId, SouthNationId));

        var attacked = dispatcher.Dispatch(declared.State, Attack());
        Assert.True(attacked.IsAccepted, attacked.ToString());

        // The state the battle resolved against was already at war -- the relation did not arrive with
        // the battle, and the battle did not change it.
        Assert.Equal(war, attacked.State.Relations.Get(NorthNationId, SouthNationId));
        Assert.Single(sink.Events.OfType<BattleResolved>());
        Assert.Null(attacked.State.ArmyById(DefenderId));
    }

    /// <summary>
    /// The hazard T22 depends on: every refusal is answerable <em>before</em> the command is issued, and
    /// the advance answer is the answer the dispatcher gives — because both come from
    /// <see cref="AttackLegality"/>. Checked across every gate this class exercises, so the probe cannot
    /// drift from one of them unnoticed.
    /// </summary>
    [Fact]
    public void TheLegalityProbeAgreesWithTheDispatcherOnEveryGate()
    {
        var atWar = Fixture();
        var cases = new (GameState State, AttackArmyCommand Command)[]
        {
            (atWar, Attack()),
            (AtPeaceFixture(), Attack()),
            (atWar, Attack(attacker: "no-such-army")),
            (atWar, Attack(target: "no-such-army")),
            (atWar, Attack(target: AttackerId)),
            (atWar, new AttackArmyCommand(NorthNationId, DefenderId, AttackerId)),
            (WithAttacker(atWar, a => a with { Moves = 0 }), Attack()),
            (WithDefender(atWar, d => d with { X = 7, Y = 5 }), Attack()),
        };

        foreach (var (state, command) in cases)
        {
            var predicted = AttackLegality.Check(state, ToyRuleset, command);
            var actual = Dispatcher().Dispatch(state, command);

            Assert.Equal(predicted is null, actual.IsAccepted);
            Assert.Equal(predicted?.Code, actual.Code);
        }
    }

    private static GameState WithAttacker(GameState state, Func<ArmyState, ArmyState> change) =>
        state with
        {
            Armies = ValueList.From(state.Armies.Select(a => a.Id == AttackerId ? change(a) : a)),
        };

    private static GameState WithDefender(GameState state, Func<ArmyState, ArmyState> change) =>
        state with
        {
            Armies = ValueList.From(state.Armies.Select(a => a.Id == DefenderId ? change(a) : a)),
        };

    /// <summary>
    /// Every rejection assertion in one place: the code is the expected one, the state that comes back is
    /// <em>the very object</em> that went in (Done-when 1's <c>Assert.Same</c>), and nothing was
    /// published.
    /// </summary>
    private static void AssertRefused(GameState state, AttackArmyCommand command, RejectionCode expected)
    {
        var sink = new RecordingEventSink();
        var result = Dispatcher(sink).Dispatch(state, command);

        Assert.True(result.IsRejected, result.ToString());
        Assert.Equal(expected, result.Code);
        Assert.Same(state, result.State);
        Assert.Empty(sink.Events);
    }
}
