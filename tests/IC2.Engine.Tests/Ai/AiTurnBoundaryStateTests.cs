using IC2.Engine.Ai;
using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Naval;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T157 (#926) rework R3 and round 3: the turn-boundary reconstruction behind the decisions test's exemption preserves
/// entity lifetimes. An army destroyed before the nation's turn boundary is not a blocker; one destroyed after it is;
/// a surviving army is kept either way.
/// </summary>
public sealed class AiTurnBoundaryStateTests
{
    private const string Me = "a";
    private const string Foe = "b";

    private static readonly World World = BattleCommandTestbed.ToyWorld;
    private static readonly Ruleset Ruleset = BattleCommandTestbed.ToyRuleset;

    private static readonly Lazy<(int X, int Y)> Run = new(FindRunOnTerrain);

    private static (int X, int Y) FindRun() => Run.Value;

    private static (int X, int Y) FindRunOnTerrain()
    {
        var probe = BattleCommandTestbed.StateWith(
            new[] { CaptureFixtures.Nation(Me, capitalCityId: "home") },
            new[] { CaptureFixtures.City("home", "Home", 0, 0, Me, Me, 80, 0, 10, 100, 10) },
            Array.Empty<ArmyState>());
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
        // #1012: the walker steps around a marker on the line through a lateral cell, so the blocker on
        // (x + 2, y) only cuts the path with both of its lateral cells walled off as well.
        var (runX, runY) = Run.Value;
        var cities = new[]
        {
            CaptureFixtures.City("home", "Home", 0, 0, Me, Me, 80, 0, 10, 100, 10),
            CaptureFixtures.City("target", "Target", 7, 5, Foe, Foe, 80, 0, 10, 100, 10),
            CaptureFixtures.City("wall-north", "Wall north", runX + 2, runY - 1, Foe, Foe, 80, 0, 10, 100, 10),
            CaptureFixtures.City("wall-south", "Wall south", runX + 2, runY + 1, Foe, Foe, 80, 0, 10, 100, 10),
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

    private static RecruitMobilized Created(string id, string nation = Foe) =>
        new(nation, "home", id, true, "light_infantry", 1000, 6, "Recruit");

    private static FleetState Fleet(string id, int x, int y) =>
        new(id, Foe, x, y, 0, 10, 100, 0, 0, null, null, null, null);

    private static ArmyScattered Scattered(string id, int toX, int toY) => new(id, Foe, toX, toY);

    private static ArmyState? At(GameState start, GameState end, List<DomainEvent> round, string id) =>
        AiTargetTreeDecisionsAreCarriedOutTests.TurnBoundaryState(start, end, round, Me).ArmyById(id);

    private static (int X, int Y)? Where(GameState start, GameState end, List<DomainEvent> round, string id) =>
        At(start, end, round, id) is { } army ? (army.X, army.Y) : null;

    // The deciding army "s" stands at (x, y) and aims at (x + 5, y); a blocker on (x + 2, y) makes the target
    // unreachable, and a later decision for the same army and position is in the log.
    private static bool Superseded(GameState start, GameState end, List<DomainEvent> round, int x, int y)
    {
        var first = Decision("t", x + 5, y, x, y);
        var second = Decision("u", x + 5, y - 1, x, y);
        var boundary = AiTargetTreeDecisionsAreCarriedOutTests.TurnBoundaryState(start, end, round, Me);
        return AiTargetTreeDecisionsAreCarriedOutTests.IsSuperseded(
            new List<AiArmyTargetTree.DecisionRecord> { first, second }, 0, boundary, Ruleset, World);
    }

    // Start and end states of a round, each holding the deciding army "s" and an untouched "keeper".
    private static (GameState Start, GameState End) Round(ArmyState[] startExtra, ArmyState[] endExtra)
    {
        var (x, y) = FindRun();
        var keeper = Army("keeper", Foe, 7, 0);
        var s = Army("s", Me, x, y);
        return (
            StateWith(new[] { s, keeper }.Concat(startExtra)),
            StateWith(new[] { s, keeper }.Concat(endExtra)));
    }

    // ---- TurnBoundaryState's branches

    [Fact]
    public void Without_the_nations_boundary_no_army_or_fleet_is_kept_and_nothing_is_exempt()
    {
        var (x, y) = FindRun();
        // The blocker stands on the path at the start and the end, and only another nation's boundary is in the
        // round: the deciding nation's boundary cannot be placed, so nothing may be exempted.
        var (start, end) = Round(new[] { Army("blk", Foe, x + 2, y) }, new[] { Army("blk", Foe, x + 2, y) });
        start = start with { Fleets = ValueList.From(new[] { Fleet("f", x + 3, y) }) };
        end = end with { Fleets = ValueList.From(new[] { Fleet("f", x + 3, y) }) };
        var round = new List<DomainEvent> { Boundary(Foe) };

        var boundary = AiTargetTreeDecisionsAreCarriedOutTests.TurnBoundaryState(start, end, round, Me);

        Assert.Empty(boundary.Armies);
        Assert.Empty(boundary.Fleets);
        Assert.False(Superseded(start, end, round, x, y));
        Assert.True(Superseded(start, end, new List<DomainEvent> { Boundary(Me) }, x, y));
    }

    [Fact]
    public void An_army_destroyed_before_the_boundary_is_absent_and_the_others_stay()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(new[] { Army("blk", Foe, x + 2, y + 1) }, Array.Empty<ArmyState>());
        var round = new List<DomainEvent> { Moved("blk", Foe, x + 2, y + 1, x + 2, y), Destroyed("blk"), Boundary(Me) };

        Assert.Null(At(start, end, round, "blk"));
        Assert.NotNull(At(start, end, round, "keeper"));
        Assert.NotNull(At(start, end, round, "s"));
        Assert.False(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void An_army_destroyed_after_the_boundary_is_there_at_its_position_and_the_decision_is_exempt()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(new[] { Army("blk", Foe, x + 2, y + 1) }, Array.Empty<ArmyState>());
        var round = new List<DomainEvent> { Moved("blk", Foe, x + 2, y + 1, x + 2, y), Boundary(Me), Destroyed("blk") };

        Assert.Equal((x + 2, y), Where(start, end, round, "blk"));
        Assert.NotNull(At(start, end, round, "keeper"));
        Assert.True(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void An_army_destroyed_twice_is_omitted_whatever_its_recorded_moves_and_the_decision_is_not_exempt()
    {
        var (x, y) = FindRun();
        // It stood on the path at the boundary by its recorded move, was destroyed twice afterwards, and is absent
        // at the end: the second destruction contradicts the lifetime, so the id is ambiguous and left out.
        var (start, end) = Round(new[] { Army("blk", Foe, x + 2, y + 1) }, Array.Empty<ArmyState>());
        var round = new List<DomainEvent>
        {
            Moved("blk", Foe, x + 2, y + 1, x + 2, y), Boundary(Me), Destroyed("blk"), Destroyed("blk"),
        };

        Assert.Null(At(start, end, round, "blk"));
        Assert.NotNull(At(start, end, round, "keeper"));
        Assert.False(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void A_reused_id_before_the_boundary_is_the_new_incarnation_at_its_own_position()
    {
        var (x, y) = FindRun();
        // The old army moved onto the path and was destroyed; the id was reused, and the new army ends at (x+2,y+1).
        var (start, end) = Round(
            new[] { Army("blk", Foe, x + 2, y + 1) }, new[] { Army("blk", Foe, x + 2, y + 1) });
        var round = new List<DomainEvent>
        {
            Moved("blk", Foe, x + 2, y + 1, x + 2, y), Destroyed("blk"), Created("blk"), Boundary(Me),
        };

        Assert.Equal((x + 2, y + 1), Where(start, end, round, "blk"));
        Assert.False(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void A_reused_id_after_the_boundary_is_the_old_incarnation_at_the_boundary()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(
            new[] { Army("blk", Foe, x + 2, y) }, new[] { Army("blk", Foe, x + 2, y + 1) });
        var round = new List<DomainEvent> { Boundary(Me), Destroyed("blk"), Created("blk") };

        Assert.Equal((x + 2, y), Where(start, end, round, "blk"));
        Assert.True(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void An_army_created_after_the_boundary_that_never_moved_is_not_in_the_boundary_state()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(Array.Empty<ArmyState>(), new[] { Army("blk", Foe, x + 2, y) });
        var round = new List<DomainEvent> { Boundary(Me), Created("blk") };

        Assert.Null(At(start, end, round, "blk"));
        Assert.False(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void An_army_created_before_the_boundary_that_never_moved_stands_where_the_round_ends()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(Array.Empty<ArmyState>(), new[] { Army("blk", Foe, x + 2, y) });
        var round = new List<DomainEvent> { Created("blk"), Boundary(Me) };

        Assert.Equal((x + 2, y), Where(start, end, round, "blk"));
        Assert.True(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void The_position_is_the_last_move_before_the_boundary()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(
            new[] { Army("blk", Foe, x + 2, y + 3) }, new[] { Army("blk", Foe, x + 2, y + 1) });
        var round = new List<DomainEvent>
        {
            Moved("blk", Foe, x + 2, y + 3, x + 2, y + 2),
            Moved("blk", Foe, x + 2, y + 2, x + 2, y),
            Boundary(Me),
            Moved("blk", Foe, x + 2, y, x + 2, y + 1),
        };

        Assert.Equal((x + 2, y), Where(start, end, round, "blk"));
    }

    [Fact]
    public void The_position_of_an_unmoved_incarnation_is_the_origin_of_its_first_move_after_the_boundary()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(Array.Empty<ArmyState>(), new[] { Army("blk", Foe, x + 2, y + 4) });
        var round = new List<DomainEvent>
        {
            Created("blk"),
            Boundary(Me),
            Moved("blk", Foe, x + 2, y, x + 2, y + 1),
            Moved("blk", Foe, x + 2, y + 1, x + 2, y + 4),
        };

        Assert.Equal((x + 2, y), Where(start, end, round, "blk"));
    }

    [Fact]
    public void An_unmoved_army_that_started_the_round_stands_at_its_start_position()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(new[] { Army("blk", Foe, x + 2, y) }, new[] { Army("blk", Foe, x + 2, y) });
        var round = new List<DomainEvent> { Boundary(Me) };

        Assert.Equal((x + 2, y), Where(start, end, round, "blk"));
    }

    [Fact]
    public void An_army_gone_without_a_destruction_event_is_omitted()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(new[] { Army("blk", Foe, x + 2, y) }, Array.Empty<ArmyState>());
        var round = new List<DomainEvent> { Boundary(Me) };

        Assert.Null(At(start, end, round, "blk"));
        Assert.False(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void An_army_present_at_the_end_with_no_creation_event_is_omitted()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(Array.Empty<ArmyState>(), new[] { Army("blk", Foe, x + 2, y) });
        var round = new List<DomainEvent> { Boundary(Me) };

        Assert.Null(At(start, end, round, "blk"));
        Assert.False(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void An_army_aboard_a_fleet_only_at_the_start_is_omitted_and_the_decision_is_not_exempt()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(
            new[] { Army("blk", Foe, x + 2, y) with { AboardFleetId = "f" } }, new[] { Army("blk", Foe, x + 2, y) });
        var round = new List<DomainEvent> { Boundary(Me) };

        Assert.Null(At(start, end, round, "blk"));
        Assert.NotNull(At(start, end, round, "keeper"));
        Assert.False(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void An_army_aboard_a_fleet_only_at_the_end_is_omitted_and_the_decision_is_not_exempt()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(
            new[] { Army("blk", Foe, x + 2, y) }, new[] { Army("blk", Foe, x + 2, y) with { AboardFleetId = "f" } });
        var round = new List<DomainEvent> { Boundary(Me) };

        Assert.Null(At(start, end, round, "blk"));
        Assert.NotNull(At(start, end, round, "keeper"));
        Assert.False(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void A_scatter_before_the_boundary_sets_the_position()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(
            new[] { Army("blk", Foe, x + 2, y + 3) }, new[] { Army("blk", Foe, x + 2, y) });
        var round = new List<DomainEvent> { Scattered("blk", x + 2, y), Boundary(Me) };

        Assert.Equal((x + 2, y), Where(start, end, round, "blk"));
    }

    [Fact]
    public void A_scatter_after_the_boundary_of_an_army_with_no_known_position_omits_it()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(Array.Empty<ArmyState>(), new[] { Army("blk", Foe, x + 2, y) });
        var round = new List<DomainEvent> { Created("blk"), Boundary(Me), Scattered("blk", x + 2, y) };

        Assert.Null(At(start, end, round, "blk"));
    }

    [Fact]
    public void A_move_after_the_armys_destruction_contradicts_its_lifetime_and_omits_the_id()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(new[] { Army("blk", Foe, x + 2, y + 1) }, Array.Empty<ArmyState>());
        var round = new List<DomainEvent>
        {
            Moved("blk", Foe, x + 2, y + 1, x + 2, y), Boundary(Me), Destroyed("blk"), Moved("blk", Foe, x + 2, y, x + 2, y + 1),
        };

        Assert.Null(At(start, end, round, "blk"));
        Assert.False(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void A_scatter_after_the_armys_destruction_contradicts_its_lifetime_and_omits_the_id()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(new[] { Army("blk", Foe, x + 2, y) }, Array.Empty<ArmyState>());
        var round = new List<DomainEvent> { Boundary(Me), Destroyed("blk"), Scattered("blk", x + 2, y + 1) };

        Assert.Null(At(start, end, round, "blk"));
        Assert.False(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void An_id_destroyed_before_the_boundary_and_recreated_after_it_is_absent_at_the_boundary()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(
            new[] { Army("blk", Foe, x + 2, y + 1) }, new[] { Army("blk", Foe, x + 2, y) });
        var round = new List<DomainEvent> { Destroyed("blk"), Boundary(Me), Created("blk") };

        Assert.Null(At(start, end, round, "blk"));
        Assert.False(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void A_creation_of_an_army_that_is_already_alive_omits_the_id()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(new[] { Army("blk", Foe, x + 2, y) }, new[] { Army("blk", Foe, x + 2, y) });
        var round = new List<DomainEvent> { Created("blk"), Boundary(Me) };

        Assert.Null(At(start, end, round, "blk"));
    }

    [Fact]
    public void A_created_army_destroyed_after_the_boundary_without_ever_moving_has_no_position_and_is_omitted()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(Array.Empty<ArmyState>(), Array.Empty<ArmyState>());
        var round = new List<DomainEvent> { Created("blk"), Boundary(Me), Destroyed("blk") };

        Assert.Null(At(start, end, round, "blk"));
        Assert.False(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void A_move_by_a_later_incarnation_does_not_give_the_boundary_position_of_an_earlier_one()
    {
        var (x, y) = FindRun();
        // Created before the boundary and never moved, so its boundary position is unknown; destroyed after the
        // boundary, the id is recreated, and the NEW army moves from the path tile. That origin is the new army's,
        // not where the old one stood at the boundary.
        var (start, end) = Round(Array.Empty<ArmyState>(), new[] { Army("blk", Foe, x + 2, y + 1) });
        var round = new List<DomainEvent>
        {
            Created("blk"), Boundary(Me), Destroyed("blk"), Created("blk"), Moved("blk", Foe, x + 2, y, x + 2, y + 1),
        };

        Assert.Null(At(start, end, round, "blk"));
        Assert.NotNull(At(start, end, round, "keeper"));
        Assert.False(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void A_scatter_after_the_boundary_keeps_a_known_boundary_position()
    {
        var (x, y) = FindRun();
        var (start, end) = Round(new[] { Army("blk", Foe, x + 2, y) }, new[] { Army("blk", Foe, x + 2, y + 3) });
        var round = new List<DomainEvent> { Boundary(Me), Scattered("blk", x + 2, y + 3) };

        Assert.Equal((x + 2, y), Where(start, end, round, "blk"));
        Assert.True(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void An_army_created_and_destroyed_within_the_round_stands_where_it_moved_at_the_boundary()
    {
        var (x, y) = FindRun();
        // In neither the start nor the end state: only its events place it on the path at the boundary.
        var (start, end) = Round(Array.Empty<ArmyState>(), Array.Empty<ArmyState>());
        var round = new List<DomainEvent>
        {
            Created("blk"), Moved("blk", Foe, x + 2, y + 1, x + 2, y), Boundary(Me), Destroyed("blk"),
        };

        Assert.Equal((x + 2, y), Where(start, end, round, "blk"));
        Assert.True(Superseded(start, end, round, x, y));
    }

    // ---- Fleets

    private static (GameState Start, GameState End) FleetRound(FleetState? atStart, FleetState? atEnd)
    {
        var (start, end) = Round(Array.Empty<ArmyState>(), Array.Empty<ArmyState>());
        return (
            start with { Fleets = atStart is null ? ValueList<FleetState>.Empty : ValueList.From(new[] { atStart }) },
            end with { Fleets = atEnd is null ? ValueList<FleetState>.Empty : ValueList.From(new[] { atEnd }) });
    }

    [Fact]
    public void A_fleet_on_the_same_tile_at_the_start_and_the_end_with_no_fleet_event_blocks()
    {
        var (x, y) = FindRun();
        var (start, end) = FleetRound(Fleet("f", x + 2, y), Fleet("f", x + 2, y));
        var round = new List<DomainEvent> { Boundary(Me) };

        Assert.Single(AiTargetTreeDecisionsAreCarriedOutTests.TurnBoundaryState(start, end, round, Me).Fleets);
        Assert.True(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void A_fleet_only_at_the_end_is_omitted()
    {
        var (x, y) = FindRun();
        var (start, end) = FleetRound(null, Fleet("f", x + 2, y));
        var round = new List<DomainEvent> { Boundary(Me) };

        Assert.False(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void A_fleet_on_another_tile_at_the_start_is_omitted()
    {
        var (x, y) = FindRun();
        var round = new List<DomainEvent> { Boundary(Me) };

        var (startX, endX) = FleetRound(Fleet("f", x + 3, y), Fleet("f", x + 2, y));
        Assert.False(Superseded(startX, endX, round, x, y));

        var (startY, endY) = FleetRound(Fleet("f", x + 2, y + 1), Fleet("f", x + 2, y));
        Assert.False(Superseded(startY, endY, round, x, y));
    }

    [Fact]
    public void A_fleet_under_construction_at_the_start_is_omitted()
    {
        var (x, y) = FindRun();
        var (start, end) = FleetRound(Fleet("f", x + 2, y) with { ConstructionTicksRemaining = 1 }, Fleet("f", x + 2, y));
        var round = new List<DomainEvent> { Boundary(Me) };

        Assert.False(Superseded(start, end, round, x, y));
    }

    [Fact]
    public void A_fleet_that_moved_or_scattered_in_the_round_is_omitted_even_back_on_its_tile()
    {
        var (x, y) = FindRun();
        var (start, end) = FleetRound(Fleet("f", x + 2, y), Fleet("f", x + 2, y));

        var moved = new List<DomainEvent>
        {
            new FleetMoved("f", Foe, x + 2, y, x + 2, y + 1), Boundary(Me), new FleetMoved("f", Foe, x + 2, y + 1, x + 2, y),
        };
        Assert.False(Superseded(start, end, moved, x, y));

        var scattered = new List<DomainEvent> { new FleetScattered("f", Foe, x + 2, y + 1), Boundary(Me) };
        Assert.False(Superseded(start, end, scattered, x, y));

        var otherFleet = new List<DomainEvent>
        {
            new FleetMoved("g", Foe, 0, 0, 1, 0), new FleetScattered("g", Foe, 1, 1), Boundary(Me),
        };
        Assert.True(Superseded(start, end, otherFleet, x, y));
    }

    // ---- IsSuperseded

    [Fact]
    public void A_final_value_equal_duplicate_with_no_later_decision_is_not_exempt()
    {
        var (x, y) = FindRun();
        var end = StateWith(new[] { Army("s", Me, x, y), Army("blk", Foe, x + 2, y) });
        var first = Decision("t", x + 5, y, x, y);
        var last = first with { };
        var decisions = new List<AiArmyTargetTree.DecisionRecord> { first, last };

        Assert.True(AiTargetTreeDecisionsAreCarriedOutTests.IsSuperseded(decisions, 0, end, Ruleset, World));
        Assert.False(AiTargetTreeDecisionsAreCarriedOutTests.IsSuperseded(decisions, 1, end, Ruleset, World));
    }

    [Fact]
    public void A_later_decision_exempts_only_for_the_same_army_and_position_and_an_unreachable_target()
    {
        var (x, y) = FindRun();
        var blocked = StateWith(new[] { Army("s", Me, x, y), Army("blk", Foe, x + 2, y) });
        var first = Decision("t", x + 5, y, x, y);
        var later = Decision("u", x + 5, y - 1, x, y);
        var otherPosition = Decision("u", x + 5, y - 1, x + 1, y);
        var otherArmy = later with { ArmyId = "other" };

        bool Check(GameState state, params AiArmyTargetTree.DecisionRecord[] log) =>
            AiTargetTreeDecisionsAreCarriedOutTests.IsSuperseded(log.ToList(), 0, state, Ruleset, World);

        Assert.True(Check(blocked, first, later));
        Assert.False(Check(blocked, first));
        Assert.False(Check(blocked, first, otherPosition));
        Assert.False(Check(blocked, first, otherArmy));
        Assert.False(Check(blocked, first, later with { NationId = Foe }));
        Assert.False(Check(blocked, first, later with { ArmyY = y + 1 }));
        Assert.False(AiTargetTreeDecisionsAreCarriedOutTests.IsSuperseded(
            new List<AiArmyTargetTree.DecisionRecord> { later, first }, 1, blocked, Ruleset, World));
        Assert.False(Check(StateWith(new[] { Army("s", Me, x, y) }), first, later));
    }

    [Fact]
    public void A_deciding_army_absent_from_the_boundary_state_is_never_exempt()
    {
        var (x, y) = FindRun();
        var noDecider = StateWith(new[] { Army("blk", Foe, x + 2, y) });
        var first = Decision("t", x + 5, y, x, y);
        var later = Decision("u", x + 5, y - 1, x, y);

        Assert.False(AiTargetTreeDecisionsAreCarriedOutTests.IsSuperseded(
            new List<AiArmyTargetTree.DecisionRecord> { first, later }, 0, noDecider, Ruleset, World));
    }
}
