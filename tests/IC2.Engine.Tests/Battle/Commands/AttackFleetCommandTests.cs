using IC2.Engine.Battle;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;
using static IC2.Engine.Tests.Battle.Commands.BattleCommandTestbed;

namespace IC2.Engine.Tests.Battle.Commands;

/// <summary>
/// <c>docs/task-catalogue.md</c> T54, Done-when 7: <see cref="AttackFleetCommand"/> dispatches to
/// <see cref="InstantBattleResolver.ResolveNaval"/>. Implemented rather than deferred — see the command's
/// own remarks.
/// </summary>
public sealed class AttackFleetCommandTests
{
    private const string AttackerId = "north-fleet";
    private const string TargetId = "south-fleet";

    /// <summary>
    /// Two fleets on adjoining tiles, well away from either nation's cities so the docking refusal does
    /// not fire by accident — the test below puts a city next to the target deliberately instead.
    /// </summary>
    private static GameState AtPeaceFixture()
    {
        var attacker = BattleTestbed.Fleet(AttackerId, NorthNationId, 6, 5, ships: 80, conditionPercent: 95);
        var target = BattleTestbed.Fleet(TargetId, SouthNationId, 7, 5, ships: 20, conditionPercent: 60);
        return BattleTestbed.StateWith(fleets: new[] { attacker, target });
    }

    private static GameState Fixture() => AtWar(AtPeaceFixture(), NorthNationId, SouthNationId);

    private static AttackFleetCommand Attack(string attacker = AttackerId, string target = TargetId) =>
        new(NorthNationId, attacker, target);

    /// <summary>Done-when 7: the command hands back exactly what the merged naval resolver produced.</summary>
    [Fact]
    public void DoD07_TheCommandReturnsTheMergedNavalResolversOwnOutcome()
    {
        var state = Fixture();
        var command = Attack();

        var direct = InstantBattleResolver.ResolveNaval(
            state, AttackerId, TargetId, ToyRuleset, ToyWorld, RngFor(state, command.Kind),
            ToyRuleset.ArcherUnitTypeId, NullEventSink.Instance);

        var sink = new RecordingEventSink();
        var result = Dispatcher(sink).Dispatch(state, command);

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(
            GameJson.Serialize(direct.State),
            GameJson.Serialize(result.State with { RandomSeed = direct.State.RandomSeed }));

        var resolved = Assert.Single(sink.Events.OfType<BattleResolved>());
        Assert.Equal(BattleKind.Naval, resolved.Result.Kind);
        Assert.Null(result.State.FleetById(TargetId));
    }

    /// <summary>
    /// The one refusal this command has that the army one does not, and it is confirmed text:
    /// <em>"You cannot attack a fleet docked at its own city !"</em>
    /// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>. The radius
    /// is <c>[derived]</c> — see <see cref="AttackLegality"/>.
    /// </summary>
    [Fact]
    public void DoD07_AFleetDockedAtItsOwnCityCannotBeAttacked()
    {
        var state = Fixture();
        var harbour = state.CityById("meridia")! with { X = 7, Y = 4 }; // adjoining the target at (7, 5).
        state = state with
        {
            Cities = ValueList.From(state.Cities.Select(c => c.Id == "meridia" ? harbour : c)),
        };

        AssertRefused(state, Attack(), AttackFleetRejections.TargetDockedAtItsOwnCity);

        // The same target one tile further from the harbour is attackable, so the refusal is the
        // docking rule and not simply "there is a city on this map".
        var awayFromHarbour = state with
        {
            Fleets = ValueList.From(state.Fleets.Select(f => f.Id == TargetId ? f with { Y = 6 } : f)),
            Cities = ValueList.From(state.Cities.Select(c => c.Id == "meridia" ? harbour with { Y = 2 } : c)),
        };
        Assert.True(AttackLegality.IsLegal(awayFromHarbour, ToyRuleset, Attack()));
    }

    /// <summary>
    /// Rework round 1, F1: <see cref="AttackFleetRejections.NoArcherUnitType"/> (<c>AttackLegality.cs</c>,
    /// the naval <c>Check</c> overload) had no test at all before this round -- gap predates this task,
    /// fix lives in the same file this task edits. The check runs unconditionally, before either fleet's
    /// strength is probed, so no carried army is needed to reach it: <c>"archers"</c> stays declared, and
    /// only <see cref="Ruleset.ArcherUnitTypeId"/> is moved to an undeclared id.
    /// </summary>
    [Fact]
    public void T66F1_NavalIsRefusedWhenThePointerNamesAnUndeclaredTypeEvenThoughArchersStillExists()
    {
        var state = Fixture();

        var pointerDangles = ToyRuleset with { ArcherUnitTypeId = "phantom_archers" };

        Assert.NotNull(pointerDangles.UnitTypeById("archers"));
        Assert.Null(pointerDangles.UnitTypeById("phantom_archers"));
        Assert.Equal(
            AttackFleetRejections.NoArcherUnitType,
            AttackLegality.Check(state, pointerDangles, Attack())!.Code);
    }

    /// <summary>
    /// T23 follow-up <see href="https://github.com/diegoami/imperial_conquest_2/issues/222">#222</see>:
    /// the boundary theory <see cref="AttackArmyCommandTests.DoD01_AdjacencyIsChebyshevDistanceOne"/> gives
    /// the army gate, now given to the naval gate too — a diagonal neighbour is adjacent (Chebyshev, not
    /// Manhattan), and two tiles away is not.
    /// </summary>
    [Theory]
    [InlineData(6, 4, true)]   // diagonal neighbour of the target at (7, 5).
    [InlineData(7, 4, true)]   // orthogonal neighbour.
    [InlineData(5, 5, false)]  // two tiles across.
    [InlineData(5, 3, false)]  // two tiles diagonally.
    public void DoD07_AdjacencyIsChebyshevDistanceOne(int attackerX, int attackerY, bool expectedLegal)
    {
        var state = Fixture();
        state = state with
        {
            Fleets = ValueList.From(state.Fleets.Select(f =>
                f.Id == AttackerId ? f with { X = attackerX, Y = attackerY } : f)),
        };

        Assert.Equal(expectedLegal, AttackLegality.IsLegal(state, ToyRuleset, Attack()));
        Assert.Equal(expectedLegal, Dispatcher().Dispatch(state, Attack()).IsAccepted);
    }

    /// <summary>Done-when 7's gates, on Done-when 1's discipline.</summary>
    [Fact]
    public void DoD07_EveryGateRefusesBeforeAnythingResolves()
    {
        var state = Fixture();

        AssertRefused(state, Attack(attacker: "no-such-fleet"), AttackFleetRejections.UnknownFleet);
        AssertRefused(state, Attack(target: "no-such-fleet"), AttackFleetRejections.UnknownTarget);
        AssertRefused(state, Attack(target: AttackerId), AttackFleetRejections.SameNation);
        AssertRefused(
            state, new AttackFleetCommand(NorthNationId, TargetId, AttackerId), AttackFleetRejections.NotYourFleet);
        AssertRefused(AtPeaceFixture(), Attack(), AttackFleetRejections.NotAtWar);

        // T66 DoD 3 (bug #222 N2): at Chebyshev distance 2 from the attacker (6, 5), not 4 -- so this
        // fixture itself fails the "distance <= 2" mutation, rather than only the boundary theory below.
        var apart = state with
        {
            Fleets = ValueList.From(state.Fleets.Select(f => f.Id == TargetId ? f with { X = 4, Y = 5 } : f)),
        };
        AssertRefused(apart, Attack(), AttackFleetRejections.NotAdjacent);

        var spent = state with
        {
            Fleets = ValueList.From(state.Fleets.Select(f => f.Id == AttackerId ? f with { Moves = 0 } : f)),
        };
        AssertRefused(spent, Attack(), AttackFleetRejections.NoMovesLeft);
    }

    /// <summary>
    /// T63 Decision 2: the original divides by zero when the naval battle's winner would have zero
    /// strength, which only happens when BOTH fleets' base strength floors to zero. Refused with the
    /// typed reason rather than reaching <see cref="InstantBattleResolver.ResolveNaval"/>'s own defensive
    /// backstop.
    /// </summary>
    [Fact]
    public void BothFleetsHaveNoStrength_IsRefusedWithTheTypedReason()
    {
        var state = Fixture();
        var powerless = state with
        {
            Fleets = ValueList.From(state.Fleets.Select(f => f with { Ships = 0 })),
        };

        AssertRefused(powerless, Attack(), AttackFleetRejections.BothFleetsHaveNoStrength);
    }

    /// <summary>
    /// The construction gate, on <strong>both</strong> sides — the same discipline as the two embarkation
    /// gates in <see cref="AttackArmyCommandTests"/>, and for the same reason:
    /// <see cref="InstantBattleResolver.ResolveNaval"/> throws <see cref="ArgumentException"/> when
    /// <em>either</em> fleet is still under construction, so a half-pinned gate leaves one side of an
    /// ordinary illegal order escaping the dispatcher as an unhandled exception rather than a refusal.
    /// </summary>
    /// <remarks>
    /// Review round 1, finding 1: the attacker half was unpinned — deleting it failed nothing, because
    /// this suite only ever put the <em>target</em> under construction. What an unpinned gate in front of
    /// a throw eventually costs is T22's own Done-when 1: zero exceptions across a 50-seed soak.
    /// </remarks>
    [Theory]
    [InlineData(AttackerId)]
    [InlineData(TargetId)]
    public void DoD07_AFleetStillUnderConstructionIsRefusedOnEitherSide(string unlaunchedFleetId)
    {
        var state = Fixture();
        var unlaunched = state with
        {
            Fleets = ValueList.From(state.Fleets.Select(f =>
                f.Id == unlaunchedFleetId
                    ? f with { ConstructionTicksRemaining = 12, BuildCityId = "meridia" }
                    : f)),
        };

        AssertRefused(unlaunched, Attack(), AttackFleetRejections.UnderConstruction);
    }

    private static void AssertRefused(GameState state, AttackFleetCommand command, RejectionCode expected)
    {
        var sink = new RecordingEventSink();
        var result = Dispatcher(sink).Dispatch(state, command);

        Assert.True(result.IsRejected, result.ToString());
        Assert.Equal(expected, result.Code);
        Assert.Same(state, result.State);
        Assert.Empty(sink.Events);
    }
}
