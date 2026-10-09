using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Economy.Commands;
using IC2.Engine.Model;
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
}
