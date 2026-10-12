using IC2.Engine.Calendar;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Export;
using Xunit;

namespace IC2.Engine.Tests.Calendar;

/// <summary>
/// T119 (issue #602): the New Game turn-order shuffle -- the original's algorithm against its own draws,
/// its bias, the preset gate, and "fixed for the whole game, saved as today".
/// </summary>
public sealed class NewGameTurnOrderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ic2-t119-" + Guid.NewGuid().ToString("N"));

    public NewGameTurnOrderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leaked temp directory is a nuisance, not a test failure.
        }
    }

    private static readonly Lazy<(World World, Ruleset Ruleset, Scenario Scenario)> LazyClassical = new(() =>
        (GameDataLoader.LoadFile<World>(ExportedDataPaths.WorldFile),
            GameDataLoader.LoadFile<Ruleset>(ExportedDataPaths.RulesetFile),
            GameDataLoader.LoadFile<Scenario>(ExportedDataPaths.ScenarioFile)));

    private static (World World, Ruleset Ruleset, Scenario Scenario) Classical => LazyClassical.Value;

    private static GameState ClassicalInitial() =>
        GameStateFactory.CreateInitial(Classical.World, Classical.Ruleset, Classical.Scenario);

    /// <summary>Returns the scripted draws in order and records every bound it was asked for.</summary>
    private sealed class DrawScript : IRng
    {
        private readonly Queue<int> _values;

        public DrawScript(IEnumerable<int> values) => _values = new Queue<int>(values);

        public List<int> Bounds { get; } = new();

        public int Remaining => _values.Count;

        public ulong Seed => 0;

        public ulong State => 0;

        public ulong NextUInt64() => throw new NotSupportedException();

        public int NextInt(int exclusiveUpperBound)
        {
            Bounds.Add(exclusiveUpperBound);
            return _values.Dequeue();
        }

        public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound) => throw new NotSupportedException();

        public bool NextChance(int numerator, int denominator) => throw new NotSupportedException();

        public IRng ForStream(string streamName) => this;
    }

    public static TheoryData<string, string, string> OriginalDraws => new()
    {
        {
            "seed 12345",
            "1 15 11 12 15 1 13 10 15 13 6 4 2 0 9 15",
            "9 5 3 12 2 15 7 10 4 14 13 0 11 1 6 8"
        },
        {
            "seed 81899841",
            "3 6 14 7 8 3 6 9 3 8 15 1 5 0 11 1",
            "13 10 14 4 8 12 1 9 0 5 15 2 7 3 6 11"
        },
        {
            "seed 8363996",
            "2 5 9 2 1 1 10 9 5 13 5 15 1 6 14 4",
            "2 12 3 9 11 6 7 0 4 13 8 15 1 10 14 5"
        },
    };

    private static int[] Ints(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();

    // ---- Done-when 1: the algorithm against the original's own draws ----

    [Theory]
    [MemberData(nameof(OriginalDraws))]
    public void The_original_draws_give_the_original_order(string seed, string draws, string expectedOrder)
    {
        _ = seed;
        var state = ClassicalInitial();
        Assert.Equal(16, state.TurnOrder.Count);
        var script = new DrawScript(Ints(draws));

        var after = NewGameTurnOrder.Shuffle(state, script);

        Assert.Equal(0, script.Remaining);
        Assert.Equal(Enumerable.Repeat(16, 16), script.Bounds);
        Assert.Equal(Ints(expectedOrder).Select(i => state.TurnOrder[i]), after.TurnOrder);
        Assert.Equal(0, after.ActiveSeatIndex);
        Assert.Equal(after.TurnOrder[0], after.ActiveNationId);
    }

    // ---- Done-when 2: the bias is the original's ----

    private static IEnumerable<int[]> AllSequences(int n, Func<int, int> lowestJ)
    {
        IEnumerable<int[]> Recurse(int i, int[] prefix)
        {
            if (i == n)
            {
                yield return prefix;
                yield break;
            }

            for (var j = lowestJ(i); j < n; j++)
            {
                foreach (var seq in Recurse(i + 1, prefix.Append(j).ToArray()))
                {
                    yield return seq;
                }
            }
        }

        return Recurse(0, Array.Empty<int>());
    }

    [Fact]
    public void Over_all_27_draw_sequences_the_shuffle_is_biased_the_way_the_original_is()
    {
        var state = ClassicalInitial() with { TurnOrder = ValueList.From(new[] { "a", "b", "c" }) };
        var counts = new Dictionary<string, int>();
        var sequences = AllSequences(3, _ => 0).ToList();
        Assert.Equal(27, sequences.Count);

        foreach (var sequence in sequences)
        {
            var order = string.Concat(NewGameTurnOrder.Shuffle(state, new DrawScript(sequence)).TurnOrder);
            counts[order] = counts.GetValueOrDefault(order) + 1;
        }

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["abc"] = 4, ["acb"] = 5, ["bac"] = 5, ["bca"] = 5, ["cab"] = 4, ["cba"] = 4,
            }.OrderBy(p => p.Key, StringComparer.Ordinal),
            counts.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void A_Fisher_Yates_reference_over_its_own_six_sequences_gives_each_order_once()
    {
        var counts = new Dictionary<string, int>();
        var sequences = AllSequences(3, i => i).ToList();
        Assert.Equal(6, sequences.Count);

        foreach (var sequence in sequences)
        {
            var order = new[] { 'a', 'b', 'c' };
            for (var i = 0; i < 3; i++)
            {
                (order[i], order[sequence[i]]) = (order[sequence[i]], order[i]);
            }

            var key = new string(order);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        Assert.Equal(6, counts.Count);
        Assert.All(counts.Values, v => Assert.Equal(1, v));
    }

    // ---- Done-when 3: the preset gate ----

    [Fact]
    public void Classical_faithful_returns_a_permutation_with_the_first_nation_active_and_is_seed_stable()
    {
        var (_, ruleset, _) = Classical;
        Assert.Equal(NewGameTurnOrderPolicy.Shuffle, ruleset.Flags.NewGameTurnOrder);
        var initial = ClassicalInitial();

        var orders = new[] { 12345UL, 54321UL }.Select(seed =>
        {
            var after = NewGameTurnOrder.Apply(initial with { RandomSeed = seed, ActiveSeatIndex = 5 }, ruleset);
            Assert.Equal(
                initial.TurnOrder.OrderBy(x => x, StringComparer.Ordinal),
                after.TurnOrder.OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal(0, after.ActiveSeatIndex);
            Assert.Equal(after.TurnOrder[0], after.ActiveNationId);
            Assert.Equal(seed, after.RandomSeed);

            var again = NewGameTurnOrder.Apply(initial with { RandomSeed = seed }, ruleset);
            Assert.Equal(after.TurnOrder, again.TurnOrder);
            return after.TurnOrder.ToArray();
        }).ToArray();

        Assert.NotEqual(orders[0], orders[1]);
    }

    [Fact]
    public void Two_new_games_on_one_seed_with_different_human_seats_share_the_order_and_pause_at_their_own_seat()
    {
        var (world, ruleset, scenario) = Classical;
        var humanA = world.TurnOrder[0];
        var humanB = world.TurnOrder[7];

        var a = new GameSession(world, ruleset, scenario, 12345UL, humanA);
        var b = new GameSession(world, ruleset, scenario, 12345UL, humanB);

        Assert.Equal(a.State.TurnOrder, b.State.TurnOrder);
        Assert.NotEqual(world.TurnOrder, a.State.TurnOrder);
        Assert.Equal(humanA, a.State.ActiveNationId);
        Assert.Equal(humanB, b.State.ActiveNationId);
    }

    [Fact]
    public void Improved_and_the_toy_ruleset_keep_the_worlds_order()
    {
        var improvedPath = Path.Combine(Path.GetDirectoryName(ExportedDataPaths.RulesetFile)!, "improved.json");
        var improved = GameDataLoader.LoadFile<Ruleset>(improvedPath);
        Assert.Equal(NewGameTurnOrderPolicy.Fixed, improved.Flags.NewGameTurnOrder);
        var initial = ClassicalInitial() with { RandomSeed = 12345UL };
        Assert.Equal(initial, NewGameTurnOrder.Apply(initial, improved));

        var toy = CoreTestbed.Toy;
        Assert.Equal(NewGameTurnOrderPolicy.Fixed, toy.Ruleset.Flags.NewGameTurnOrder);
        var toyState = CoreTestbed.InitialState();
        Assert.Equal(toyState, NewGameTurnOrder.Apply(toyState, toy.Ruleset));
    }

    // ---- Done-when 4: fixed and saved ----

    [Fact]
    public void A_full_round_leaves_the_order_unchanged_and_advances_the_calendar_once()
    {
        var (world, ruleset, scenario) = Classical;
        var human = world.TurnOrder[3];
        var session = new GameSession(world, ruleset, scenario, 12345UL, human);
        var order = session.State.TurnOrder;
        var turn = session.State.Calendar.TurnIndex;

        session.Submit("end");

        Assert.Equal(order, session.State.TurnOrder);
        Assert.Equal(turn + 1, session.State.Calendar.TurnIndex);
        Assert.Equal(human, session.State.ActiveNationId);
    }

    [Fact]
    public void A_save_round_trip_and_resume_keeps_the_order_and_the_active_seat_and_draws_nothing()
    {
        var (world, ruleset, scenario) = Classical;
        var session = new GameSession(world, ruleset, scenario, 12345UL, world.TurnOrder[3]);
        var path = Path.Combine(_directory, "t119.sav");
        session.Submit($"save {path}");

        var save = SaveManager.LoadFile(path, world, ruleset);
        var resumed = new GameSession(world, ruleset, scenario, save);

        Assert.Equal(session.State.TurnOrder, resumed.State.TurnOrder);
        Assert.Equal(session.State.ActiveSeatIndex, resumed.State.ActiveSeatIndex);
        Assert.Equal(session.State.RandomSeed, resumed.State.RandomSeed);
    }
}
