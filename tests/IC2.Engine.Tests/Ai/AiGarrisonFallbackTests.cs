using IC2.Engine.Ai;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Economy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T156 (issue #925) Done-when 3: the garrison fallback (<c>FUN_0044ebe8</c>) runs after execution, keyed
/// to the chosen army's own tile. Each state is at peace with one army whose tree choice is "move to the
/// resupply city"; a dispatcher wrapper swaps that first command for one the real handlers accept but
/// that leaves the army's tile as it was (the #907 shape), and a third run leaves the command alone.
/// </summary>
public sealed class AiGarrisonFallbackTests
{
    private const string Us = "north";
    private const string Them = "south";
    private const string ArmyId = "a1";

    private static Ruleset Ruleset => AiScriptedStates.Ruleset;

    /// <summary>
    /// North's army at (3,3), its capital at (1,1) and another own city at (6,3) that needs stock, south
    /// far away at (5,4). The tree picks the (6,3) city (the capital scores -20); the fallback, with the
    /// army 2 from its capital, heads for the nearest city of any owner, which is the capital.
    /// </summary>
    private static GameState Build(int capitalX = 1, int capitalY = 1) =>
        AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.StateWith(
                [
                    CaptureFixtures.Nation(Us, treasury: 1_000, capitalCityId: "n-cap") with { Personality = AiScriptedStates.DefaultPersonality },
                    CaptureFixtures.Nation(Them, capitalCityId: "s-cap") with { Personality = AiScriptedStates.DefaultPersonality },
                ],
                [
                    City("n-cap", Us, capitalX, capitalY) with { SupplyTons = 5_000 },
                    City("r-city", Us, 6, 3) with { SupplyTons = 0 },
                    City("s-cap", Them, 5, 4),
                ],
                [
                    CaptureFixtures.Army(ArmyId, Us, 3, 3, 60, CaptureFixtures.Unit("light_infantry", 10_000))
                        with { Moves = 9, Money = 500 },
                ]),
            Us);

    private static CityState City(string id, string owner, int x, int y) =>
        CaptureFixtures.City(id, id, x, y, owner, owner, 90, 100, 100, 100, 0);

    private sealed class ReplacingDispatch(ICommandDispatch inner, Func<ICommand, int, ICommand> replace) : ICommandDispatch
    {
        public List<ICommand> Dispatched { get; } = [];

        /// <summary>For each supply purchase: the army's supply before and after, and its tile before and after.</summary>
        public List<(int SupplyBefore, int SupplyAfter, (int X, int Y) TileBefore, (int X, int Y) TileAfter)> Purchases { get; } = [];

        public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, null!);

        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
        {
            var actual = replace(command, Dispatched.Count);
            Dispatched.Add(actual);
            var result = events is null ? inner.Dispatch(state, actual) : inner.Dispatch(state, actual, events);
            if (actual is BuySupplyCommand buy && result.IsAccepted)
            {
                var before = state.ArmyById(buy.ArmyId)!;
                var after = result.State.ArmyById(buy.ArmyId)!;
                Purchases.Add((before.SupplyTons, after.SupplyTons, (before.X, before.Y), (after.X, after.Y)));
            }

            return result;
        }
    }

    private static (AiTurnOutcome Outcome, ReplacingDispatch Seen) Drive(
        GameState state, Func<ICommand, int, ICommand> replace)
    {
        var sink = new RecordingEventSink();
        var world = AiScriptedStates.World;
        var seen = new ReplacingDispatch(
            new CommandDispatcher(SystemRegistry.FromEngineAssembly(), Ruleset, world, sink), replace);
        var outcome = AiTurn.Run(state, Ruleset, world, seen, SplitMix64Rng.ForStream(1UL, "ai.turn"), sink);
        return (outcome, seen);
    }

    private static (AiTurnOutcome Outcome, ReplacingDispatch Seen) DriveIn(
        GameState state,
        World world,
        Func<ICommand, int, ICommand> replace)
    {
        var sink = new RecordingEventSink();
        var seen = new ReplacingDispatch(
            new CommandDispatcher(SystemRegistry.FromEngineAssembly(), Ruleset, world, sink), replace);
        var outcome = AiTurn.Run(state, Ruleset, world, seen, SplitMix64Rng.ForStream(1UL, "ai.turn"), sink);
        return (outcome, seen);
    }

    private static IEnumerable<MoveArmyCommand> MovesOf(ReplacingDispatch seen) =>
        seen.Dispatched.OfType<MoveArmyCommand>().Where(m => m.ArmyId == ArmyId);

    private static (int X, int Y) FallbackDestination(GameState state) =>
        AiArmyTargetTree.GarrisonFallback(
            new AiView(state, Ruleset, AiScriptedStates.World, Us), state.ArmyById(ArmyId)!, state)!.Value;

    [Fact]
    public void The_tree_without_interference_moves_the_army_to_the_resupply_city_and_takes_no_fallback()
    {
        var state = Build();

        var (outcome, seen) = Drive(state, (command, _) => command);

        var moves = MovesOf(seen).ToList();
        Assert.Single(moves);
        Assert.Equal((6, 3), (moves[0].X, moves[0].Y));
        Assert.NotEqual((3, 3), (outcome.State.ArmyById(ArmyId)!.X, outcome.State.ArmyById(ArmyId)!.Y));
        Assert.DoesNotContain(outcome.Log, line => line.Contains("garrison fallback", StringComparison.Ordinal));
        Assert.Equal(0, outcome.CommandsRejected);
    }

    [Fact]
    public void An_accepted_move_that_moves_nothing_takes_the_fallback_that_turn()
    {
        var state = Build();
        var expected = FallbackDestination(state);
        Assert.NotEqual((6, 3), expected);

        // The first move is swapped for an order to the army's own tile: the real handler accepts it and
        // the army stays where it was, exactly what #907's blocked march did.
        var swapped = false;
        var (outcome, seen) = Drive(state, (command, _) =>
        {
            if (command is MoveArmyCommand move && move.ArmyId == ArmyId && !swapped)
            {
                swapped = true;
                return new MoveArmyCommand(move.IssuingNationId, ArmyId, 3, 3);
            }

            return command;
        });

        var moves = MovesOf(seen).ToList();
        Assert.Equal(2, moves.Count);
        Assert.Equal((3, 3), (moves[0].X, moves[0].Y));
        Assert.Equal(expected, (moves[1].X, moves[1].Y));
        Assert.Contains(outcome.Log, line => line.Contains("garrison fallback moved", StringComparison.Ordinal));
        var army = outcome.State.ArmyById(ArmyId)!;
        Assert.NotEqual((3, 3), (army.X, army.Y));
        Assert.Equal(0, outcome.CommandsRejected);
    }

    [Fact]
    public void A_command_that_changes_the_armys_supply_but_not_its_tile_takes_the_fallback_that_turn()
    {
        var state = Build(capitalX: 2, capitalY: 2);
        var expected = FallbackDestination(state);

        // The first move is swapped for a supply purchase: it changes the army's supply and a purse, not its tile.
        var swapped = false;
        var (outcome, seen) = Drive(state, (command, _) =>
        {
            if (command is MoveArmyCommand move && move.ArmyId == ArmyId && !swapped)
            {
                swapped = true;
                return new BuySupplyCommand(move.IssuingNationId, ArmyId, "n-cap", 50);
            }

            return command;
        });

        var moves = MovesOf(seen).ToList();
        var purchase = Assert.Single(seen.Purchases);
        Assert.True(purchase.SupplyAfter > purchase.SupplyBefore, "the purchase must change the army's supply");
        Assert.Equal(purchase.TileBefore, purchase.TileAfter);
        Assert.Single(moves);
        Assert.Equal(expected, (moves[0].X, moves[0].Y));
        Assert.Contains(outcome.Log, line => line.Contains("garrison fallback moved", StringComparison.Ordinal));
        Assert.Equal(0, outcome.CommandsRejected);
    }

    // ---- Done-when 3, fourth and fifth rows: the Hazards' reachable fallback, through a full AI turn ----

    private const int Sea = 0;

    /// <summary>
    /// North's army at (5, 5) with 3 moves inside a ring of sea tiles at Chebyshev distance 2: its eight
    /// neighbours are plain, and every straight line out of the ring crosses a sea tile. North's capital
    /// (20, 5) and its supply-starved city (5, 18) lie outside, so the resupply candidate and the
    /// garrison fallback (the capital alone: the army is 15 tiles from it) are both unreachable.
    /// </summary>
    private static (GameState State, World World) InsideARing()
    {
        var world = AiReachableFallbackTests.Terrain(cells => AiReachableFallbackTests.SeaRing(cells, 5, 5, 2));
        var army = AiReachableFallbackTests.Army(5, 5, moves: 3);
        var state = AiReachableFallbackTests.State(
            [
                AiReachableFallbackTests.City("n-cap", Us, 20, 5, supply: 5_000),
                AiReachableFallbackTests.City("r-city", Us, 5, 18, supply: 0),
                AiReachableFallbackTests.City("s-cap", Them, 30, 30),
            ],
            army);
        return (state, world);
    }

    private static void AssertEveryResupplyAndGarrisonCandidateIsUnreachable(GameState state, World world)
    {
        var view = new AiView(state, Ruleset, world, Us);
        var army = state.ArmyById(ArmyId)!;
        Assert.Null(AiArmyTargetTree.ResolveResupplyCity(view, army, atWar: false).City);
        Assert.Null(AiArmyTargetTree.GarrisonFallback(view, army, state));
    }

    /// <summary>
    /// The fourth row, before the turn's first command: the tree can give the army no command, the
    /// emergency move names the attainable tile nearest the army's nearest own city, (4, 6) (the three
    /// southern neighbours tie at distance 12 from (5, 18) and 1 from the army, and X ascending
    /// decides), and the army moves there that turn. Fails when the emergency move is removed.
    /// </summary>
    [Fact]
    public void An_army_with_every_resupply_and_garrison_candidate_unreachable_moves_to_the_emergency_tile()
    {
        var (state, world) = InsideARing();
        AssertEveryResupplyAndGarrisonCandidateIsUnreachable(state, world);
        var view = new AiView(state, Ruleset, world, Us);
        Assert.Equal((4, 6), AiArmyTargetTree.EmergencyMoveDestination(view, state.ArmyById(ArmyId)!, state));

        var (outcome, seen) = DriveIn(state, world, (command, _) => command);

        var army = outcome.State.ArmyById(ArmyId)!;
        Assert.Equal((4, 6), (army.X, army.Y));
        var firstMove = MovesOf(seen).First();
        Assert.Equal((4, 6), (firstMove.X, firstMove.Y));
        Assert.Contains(outcome.Log, line => line.Contains("emergency move", StringComparison.Ordinal));
        Assert.DoesNotContain(outcome.Log, line => line.Contains("no reachable move", StringComparison.Ordinal));
        Assert.Equal(0, outcome.CommandsRejected);
    }

    /// <summary>
    /// The fourth row, after the turn's first command: the command the tree chose is swapped for an
    /// accepted order that leaves the army's tile as it was, so the driver's post-execution fallback
    /// runs, finds no garrison destination, and moves the army to the emergency tile. Fails when the
    /// driver's emergency tier is removed.
    /// </summary>
    [Fact]
    public void The_post_execution_fallback_takes_the_emergency_move_when_no_garrison_destination_is_reachable()
    {
        var (state, world) = InsideARing();
        AssertEveryResupplyAndGarrisonCandidateIsUnreachable(state, world);

        var swapped = false;
        var (outcome, seen) = DriveIn(state, world, (command, _) =>
        {
            if (command is MoveArmyCommand move && move.ArmyId == ArmyId && !swapped)
            {
                swapped = true;
                return new MoveArmyCommand(move.IssuingNationId, ArmyId, 5, 5);
            }

            return command;
        });

        Assert.True(swapped, "the tree must have offered the army a march to swap");
        var moves = MovesOf(seen).ToList();
        Assert.Equal(2, moves.Count);
        Assert.Equal((5, 5), (moves[0].X, moves[0].Y));
        Assert.Equal((4, 6), (moves[1].X, moves[1].Y));
        var army = outcome.State.ArmyById(ArmyId)!;
        Assert.Equal((4, 6), (army.X, army.Y));
        Assert.Contains(outcome.Log, line => line.Contains("emergency move moved", StringComparison.Ordinal));
        Assert.DoesNotContain(outcome.Log, line => line.Contains("no reachable move", StringComparison.Ordinal));
        Assert.Equal(0, outcome.CommandsRejected);
    }

    /// <summary>
    /// The fifth row: a boxed-in army. All eight neighbours of (5, 5) are sea, so with moves left there
    /// is no distinct tile to reach. The turn runs, the army does not move, and the log says
    /// <c>no reachable move</c> (written once). Fails when the log line is removed.
    /// </summary>
    [Fact]
    public void A_boxed_in_army_does_not_move_and_the_turn_logs_no_reachable_move()
    {
        var world = AiReachableFallbackTests.Terrain(cells => AiReachableFallbackTests.SeaRing(cells, 5, 5, 1));
        var army = AiReachableFallbackTests.Army(5, 5, moves: 3);
        var state = AiReachableFallbackTests.State(
            [
                AiReachableFallbackTests.City("n-cap", Us, 20, 5, supply: 5_000),
                AiReachableFallbackTests.City("r-city", Us, 5, 18, supply: 0),
                AiReachableFallbackTests.City("s-cap", Them, 30, 30),
            ],
            army);
        AssertEveryResupplyAndGarrisonCandidateIsUnreachable(state, world);
        var view = new AiView(state, Ruleset, world, Us);
        Assert.Null(AiArmyTargetTree.EmergencyMoveDestination(view, state.ArmyById(ArmyId)!, state));

        var (outcome, seen) = DriveIn(state, world, (command, _) => command);

        Assert.Empty(MovesOf(seen));
        var after = outcome.State.ArmyById(ArmyId)!;
        Assert.Equal((5, 5), (after.X, after.Y));
        Assert.Equal(1, outcome.Log.Count(line => line.Contains("no reachable move for a1", StringComparison.Ordinal)));
        Assert.Equal(0, outcome.CommandsRejected);
    }

    /// <summary>
    /// The fifth row's other path: the army does have a command (an attack on the weak army beside it,
    /// the only land neighbour), the command is swapped for an accepted order that leaves its tile as it
    /// was, and the driver's post-execution fallback finds neither a garrison destination nor an
    /// emergency tile (the sea ring and the enemy army fill every neighbour). The army stays and the log
    /// says <c>no reachable move</c>. Fails when that log line is removed from the driver.
    /// </summary>
    [Fact]
    public void A_boxed_in_army_found_after_its_command_also_logs_no_reachable_move()
    {
        var world = AiReachableFallbackTests.Terrain(cells =>
        {
            AiReachableFallbackTests.SeaRing(cells, 5, 5, 1);
            cells[(5 * 32) + 6] = 2;
        });
        var army = AiReachableFallbackTests.Army(5, 5, moves: 3);
        var enemy = CaptureFixtures.Army("e1", Them, 6, 5, 60, CaptureFixtures.Unit("light_infantry", 1_000))
            with { Moves = 3 };
        var baseState = AiReachableFallbackTests.State(
            [
                AiReachableFallbackTests.City("n-cap", Us, 20, 5, supply: 5_000),
                AiReachableFallbackTests.City("r-city", Us, 5, 18, supply: 0),
                AiReachableFallbackTests.City("s-cap", Them, 30, 30),
            ],
            army);
        var state = baseState with
        {
            Armies = ValueList.From(baseState.Armies.Append(enemy)),
            Relations = baseState.Relations.WithRelation(Us, Them, Ruleset.Diplomacy.StateCodes.War),
        };
        AssertEveryResupplyAndGarrisonCandidateIsUnreachable(state, world);
        var view = new AiView(state, Ruleset, world, Us);
        Assert.Null(AiArmyTargetTree.EmergencyMoveDestination(view, state.ArmyById(ArmyId)!, state));

        var swapped = false;
        var (outcome, seen) = DriveIn(state, world, (command, _) =>
        {
            if (command is AttackArmyCommand attack && attack.AttackerArmyId == ArmyId && !swapped)
            {
                swapped = true;
                return new MoveArmyCommand(attack.IssuingNationId, ArmyId, 5, 5);
            }

            return command;
        });

        Assert.True(swapped, "the tree must have chosen to attack the army beside it");
        var after = outcome.State.ArmyById(ArmyId)!;
        Assert.Equal((5, 5), (after.X, after.Y));
        Assert.Equal(1, outcome.Log.Count(line => line.Contains("no reachable move for a1", StringComparison.Ordinal)));
        Assert.Equal(0, outcome.CommandsRejected);
    }
}
