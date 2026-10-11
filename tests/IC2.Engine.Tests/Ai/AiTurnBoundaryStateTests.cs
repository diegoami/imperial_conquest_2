using IC2.Engine.Ai;
using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T157 (#926) rework R3: the turn-boundary reconstruction behind the decisions test's exemption preserves
/// entity lifetimes. An army destroyed before the nation's turn boundary is not a blocker; one destroyed after it is;
/// a surviving army is kept either way.
/// </summary>
public sealed class AiTurnBoundaryStateTests
{
    private const string Me = "a";
    private const string Foe = "b";

    private static readonly World World = BattleCommandTestbed.ToyWorld;
    private static readonly Ruleset Ruleset = BattleCommandTestbed.ToyRuleset;

    private static (int X, int Y) FindRun()
    {
        var probe = StateWith(Array.Empty<ArmyState>());
        var view = new AiView(probe, Ruleset, World, Me);
        for (var y = 1; y < World.Height; y++)
        {
            for (var x = 0; x + 5 < World.Width; x++)
            {
                if (Enumerable.Range(0, 6).All(i => view.IsArmyPassable(new GridPoint(x + i, y))))
                {
                    return (x, y);
                }
            }
        }

        throw new InvalidOperationException("the toy world has no row of six passable tiles");
    }

    private static GameState StateWith(IEnumerable<ArmyState> armies)
    {
        var nations = new[]
        {
            CaptureFixtures.Nation(Me, capitalCityId: "home"),
            CaptureFixtures.Nation(Foe, capitalCityId: "target"),
        };
        var cities = new[]
        {
            CaptureFixtures.City("home", "Home", 0, 0, Me, Me, 80, 0, 10, 100, 10),
            CaptureFixtures.City("target", "Target", 7, 5, Foe, Foe, 80, 0, 10, 100, 10),
        };
        var state = BattleCommandTestbed.StateWith(nations, cities, armies);
        return state with { TurnOrder = ValueList.From(new[] { Me, Foe }), ActiveSeatIndex = 0 };
    }

    private static ArmyState Army(string id, string nation, int x, int y) =>
        CaptureFixtures.Army(id, nation, x, y, 60, CaptureFixtures.Unit("light_infantry", 1000));

    private static AiTurnDecided Boundary(string nation) =>
        new(nation, ValueList<string>.Empty, 0, 0, 0, false);

    private static ArmyMoved Moved(string id, string nation, int fromX, int fromY, int toX, int toY) =>
        new(id, nation, fromX, fromY, toX, toY, 1);

    private static BattleResolved Destroyed(string loserId)
    {
        var result = new BattleResult(
            Kind: BattleKind.Field,
            AttackerId: "attacker", DefenderId: loserId,
            AttackerNationId: Foe, DefenderNationId: Me,
            AttackerPower: 100, DefenderPower: 50,
            Winner: BattleSide.Attacker,
            AppliedDefeatOutcome: null,
            LoserFate: LoserFate.Destroyed,
            WinnerCasualties: 1, LoserCasualties: 1,
            UnitCasualties: ValueList<UnitCasualty>.Empty, Promotions: ValueList<UnitPromotion>.Empty,
            AbsorbedMoney: 0, AbsorbedSupplyTons: 0,
            WinnerUnityDelta: 0, LoserUnityDelta: 0,
            WinnerShipsLost: 0, WinnerConditionLost: 0, WinnerUnitsLost: 0,
            PeaceTreatyFired: false, PeaceTreatyOffered: false,
            Scatter: null);
        return new BattleResolved(result);
    }

    private static AiArmyTargetTree.DecisionRecord Decision(string target, int tx, int ty, int ax, int ay) =>
        new(Me, "s", ax, ay, AiArmyTargetTree.Kind.AttackCity, target, tx, ty, null, 0, 0, 200, 0, 5, 0, 0);

    private static bool Superseded(GameState end, List<DomainEvent> round, int x, int y)
    {
        var first = Decision("t", x + 5, y, x, y);
        var second = Decision("u", x + 5, y - 1, x, y);
        var boundary = AiTargetTreeDecisionsAreCarriedOutTests.TurnBoundaryState(end, round, Me);
        return AiTargetTreeDecisionsAreCarriedOutTests.IsSuperseded(
            new List<AiArmyTargetTree.DecisionRecord> { first, second }, 0, boundary, Ruleset, World);
    }

    [Fact]
    public void A_blocker_destroyed_before_the_boundary_is_not_resurrected_and_the_decision_is_not_exempt()
    {
        var (x, y) = FindRun();
        var end = StateWith(new[] { Army("s", Me, x, y), Army("keeper", Foe, 7, 0) });
        var round = new List<DomainEvent>
        {
            Moved("blk", Foe, x + 2, y + 1, x + 2, y),
            Destroyed("blk"),
            Boundary(Me),
        };

        var boundary = AiTargetTreeDecisionsAreCarriedOutTests.TurnBoundaryState(end, round, Me);
        Assert.Null(boundary.ArmyById("blk"));
        Assert.NotNull(boundary.ArmyById("keeper"));
        Assert.NotNull(boundary.ArmyById("s"));
        Assert.False(Superseded(end, round, x, y));
    }

    [Fact]
    public void A_blocker_destroyed_after_the_boundary_is_present_at_the_boundary_and_the_decision_is_exempt()
    {
        var (x, y) = FindRun();
        var end = StateWith(new[] { Army("s", Me, x, y), Army("keeper", Foe, 7, 0) });
        var round = new List<DomainEvent>
        {
            Moved("blk", Foe, x + 2, y + 1, x + 2, y),
            Boundary(Me),
            Destroyed("blk"),
        };

        var boundary = AiTargetTreeDecisionsAreCarriedOutTests.TurnBoundaryState(end, round, Me);
        Assert.Equal((x + 2, y), (boundary.ArmyById("blk")!.X, boundary.ArmyById("blk")!.Y));
        Assert.NotNull(boundary.ArmyById("keeper"));
        Assert.True(Superseded(end, round, x, y));
    }

    [Fact]
    public void A_surviving_blocker_is_placed_where_it_stood_at_the_boundary()
    {
        var (x, y) = FindRun();
        // It ends the round away from the path, but stood on it when the turn ended.
        var end = StateWith(new[] { Army("s", Me, x, y), Army("blk", Foe, x + 2, y + 1) });
        var round = new List<DomainEvent>
        {
            Moved("blk", Foe, x + 2, y + 1, x + 2, y),
            Boundary(Me),
            Moved("blk", Foe, x + 2, y, x + 2, y + 1),
        };

        var boundary = AiTargetTreeDecisionsAreCarriedOutTests.TurnBoundaryState(end, round, Me);
        Assert.Equal((x + 2, y), (boundary.ArmyById("blk")!.X, boundary.ArmyById("blk")!.Y));
        Assert.True(Superseded(end, round, x, y));
    }

    [Fact]
    public void A_final_value_equal_duplicate_with_no_later_decision_is_not_exempt()
    {
        var (x, y) = FindRun();
        // The blocker stands on the path at the boundary, so the target is unreachable.
        var end = StateWith(new[] { Army("s", Me, x, y), Army("blk", Foe, x + 2, y) });
        var first = Decision("t", x + 5, y, x, y);
        var last = first with { };
        var decisions = new List<AiArmyTargetTree.DecisionRecord> { first, last };

        // The first copy has a genuinely later one (the duplicate); the final copy has nothing after it.
        Assert.True(AiTargetTreeDecisionsAreCarriedOutTests.IsSuperseded(decisions, 0, end, Ruleset, World));
        Assert.False(AiTargetTreeDecisionsAreCarriedOutTests.IsSuperseded(decisions, 1, end, Ruleset, World));
    }

    [Fact]
    public void A_genuinely_later_decision_for_the_same_army_and_position_exempts_when_the_target_is_unreachable()
    {
        var (x, y) = FindRun();
        var end = StateWith(new[] { Army("s", Me, x, y), Army("blk", Foe, x + 2, y) });
        var first = Decision("t", x + 5, y, x, y);
        var later = Decision("u", x + 5, y - 1, x, y);
        var otherPosition = Decision("u", x + 5, y - 1, x + 1, y);
        var otherArmy = later with { ArmyId = "other" };

        Assert.True(AiTargetTreeDecisionsAreCarriedOutTests.IsSuperseded(
            new List<AiArmyTargetTree.DecisionRecord> { first, later }, 0, end, Ruleset, World));
        // A later decision from another position, or for another army, supersedes nothing.
        Assert.False(AiTargetTreeDecisionsAreCarriedOutTests.IsSuperseded(
            new List<AiArmyTargetTree.DecisionRecord> { first, otherPosition }, 0, end, Ruleset, World));
        Assert.False(AiTargetTreeDecisionsAreCarriedOutTests.IsSuperseded(
            new List<AiArmyTargetTree.DecisionRecord> { first, otherArmy }, 0, end, Ruleset, World));
        // An EARLIER decision does not count as a later one.
        Assert.False(AiTargetTreeDecisionsAreCarriedOutTests.IsSuperseded(
            new List<AiArmyTargetTree.DecisionRecord> { later, first }, 1, end, Ruleset, World));
        // Reachable target: not exempt even with a later decision.
        var clear = StateWith(new[] { Army("s", Me, x, y) });
        Assert.False(AiTargetTreeDecisionsAreCarriedOutTests.IsSuperseded(
            new List<AiArmyTargetTree.DecisionRecord> { first, later }, 0, clear, Ruleset, World));
    }

    [Fact]
    public void An_id_destroyed_and_then_reused_in_the_round_is_ambiguous_and_never_exempts()
    {
        var (x, y) = FindRun();
        // The old "blk" stood on the path and was destroyed before the boundary; mobilisation then reused the
        // id for a new army that ends the round off the path. The old army's move must not be applied to it.
        var end = StateWith(new[] { Army("s", Me, x, y), Army("blk", Foe, x + 2, y + 1), Army("keeper", Foe, 7, 0) });
        var round = new List<DomainEvent>
        {
            Moved("blk", Foe, x + 2, y + 1, x + 2, y),
            Destroyed("blk"),
            Boundary(Me),
        };

        var boundary = AiTargetTreeDecisionsAreCarriedOutTests.TurnBoundaryState(end, round, Me);
        Assert.Null(boundary.ArmyById("blk"));
        Assert.NotNull(boundary.ArmyById("keeper"));
        Assert.False(Superseded(end, round, x, y));

        // The same id as the deciding army is ambiguous too: no exemption on it.
        var reusedDecider = new List<DomainEvent> { Destroyed("s"), Boundary(Me) };
        var withBlocker = StateWith(new[] { Army("s", Me, x, y), Army("blk", Foe, x + 2, y) });
        Assert.False(Superseded(withBlocker, reusedDecider, x, y));
    }
}
