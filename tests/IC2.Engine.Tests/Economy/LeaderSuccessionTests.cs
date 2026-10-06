using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// T146 Done-when 4 and 4a: the fall's two draws, the deposition's retry loop, the per-event stream keys,
/// and the human fall wired through <see cref="GameSession"/>.
/// </summary>
public sealed class LeaderSuccessionTests
{
    private static ValueList<string> Pool() =>
        ValueList.Of(Enumerable.Range(0, 12).Select(i => $"leader-{i}").ToArray());

    private static NationState South(string leader) =>
        CoreTestbed.InitialState().NationById("south")! with { LeaderName = leader };

    [Fact]
    public void A_fall_drawing_3_3_7_writes_pool_7_after_three_draws()
    {
        var pool = Pool();
        var rng = new ScriptedRng(
            nextIntDraws: new[] { 3, 3, 7 },
            expectedNextIntBounds: new[] { 12, 12, 12 });

        var result = LeaderSuccession.ApplyFall(South("old-leader"), pool, rng);

        rng.AssertAllDrawsConsumed();
        Assert.Equal(pool[7], result.LeaderName);
    }

    [Fact]
    public void A_fall_drawing_2_5_from_a_leader_at_pool_5_leaves_pool_5_the_old_leader()
    {
        var pool = Pool();
        var rng = new ScriptedRng(
            nextIntDraws: new[] { 2, 5 },
            expectedNextIntBounds: new[] { 12, 12 });

        var result = LeaderSuccession.ApplyFall(South(pool[5]), pool, rng);

        rng.AssertAllDrawsConsumed();
        Assert.Equal(pool[5], result.LeaderName);
        Assert.NotEqual(pool[2], result.LeaderName);
    }

    [Fact]
    public void A_pool_of_identical_names_is_refused_not_spun_on()
    {
        var identical = ValueList.Of(Enumerable.Repeat("same", 12).ToArray());
        var rng = new ScriptedRng();

        Assert.Throws<InvalidOperationException>(() => LeaderSuccession.ApplyFall(South("same"), identical, rng));
        Assert.Throws<InvalidOperationException>(() => LeaderSuccession.ApplyDeposition(South("same"), identical, rng));
    }

    // ---- Done-when 4a: distinct calendar points draw afresh, the same point reproduces ----------

    [Fact]
    public void Different_calendar_points_of_one_nation_derive_different_keys_and_names()
    {
        var pool = Pool();
        var nation = South("old-leader");
        var cal1 = new CalendarState(Week: 1, SeasonIndex: 0, YearBc: 270, TurnIndex: 0);
        var cal2 = cal1 with { Week = 3 }; // same YearBc, a different Week.

        Assert.NotEqual(
            LeaderSuccession.FallStreamKey("south", cal1),
            LeaderSuccession.FallStreamKey("south", cal2));

        var name1 = LeaderSuccession.ApplyFall(
            nation, pool, LeaderSuccession.FallStream(12345, "south", cal1)).LeaderName;
        var name2 = LeaderSuccession.ApplyFall(
            nation, pool, LeaderSuccession.FallStream(12345, "south", cal2)).LeaderName;

        Assert.NotEqual(name1, name2);
    }

    [Fact]
    public void The_same_calendar_point_derives_the_same_key_and_name()
    {
        var pool = Pool();
        var nation = South("old-leader");
        var cal = new CalendarState(Week: 1, SeasonIndex: 0, YearBc: 270, TurnIndex: 0);

        Assert.Equal(
            LeaderSuccession.FallStreamKey("south", cal),
            LeaderSuccession.FallStreamKey("south", cal));

        var first = LeaderSuccession.ApplyFall(
            nation, pool, LeaderSuccession.FallStream(12345, "south", cal)).LeaderName;
        var again = LeaderSuccession.ApplyFall(
            nation, pool, LeaderSuccession.FallStream(12345, "south", cal)).LeaderName;

        Assert.Equal(first, again);
    }

    /// <summary>
    /// Two computer depositions of one nation at two successive quarter boundaries each make their own
    /// draw (the second always differs from the first, by the retry), and a save and resume between them
    /// leaves the second draw unchanged — the root seed and calendar are the whole derivation.
    /// </summary>
    [Fact]
    public void Two_depositions_at_successive_quarters_draw_afresh_and_survive_a_resume()
    {
        var toy = CoreTestbed.Toy;
        var pool = Pool();
        var world = toy.World with
        {
            Nations = ValueList.From(toy.World.Nations.Select(n =>
                n.Id == "south" ? n with { LeaderNames = pool } : n)),
        };

        var cal1 = new CalendarState(Week: 1, SeasonIndex: 0, YearBc: 270, TurnIndex: 0);
        var state1 = CoreTestbed.InitialState() with
        {
            RandomSeed = 7,
            Calendar = cal1,
            Nations = ValueList.From(CoreTestbed.InitialState().Nations.Select(n =>
                n.Id == "south"
                    ? n with { Control = SeatControl.Ai, Treasury = -50_000, Unity = 600, LeaderName = pool[0] }
                    : n)),
        };

        var handler = new AiDepositionHandler();
        var after1 = handler.OnQuarterBoundary(DepositionContext(state1, world, cal1));
        var south1 = after1.NationById("south")!.LeaderName;
        Assert.Contains(south1, pool);
        Assert.NotEqual(pool[0], south1);

        // The next quarter: re-debt the nation (the first deposition zeroed its treasury) and advance the
        // calendar, so the second event's key differs from the first's.
        var cal2 = cal1 with { TurnIndex = 1, Week = 3 };
        var state2 = after1 with
        {
            Calendar = cal2,
            Nations = ValueList.From(after1.Nations.Select(n =>
                n.Id == "south" ? n with { Treasury = -50_000 } : n)),
        };

        var after2 = handler.OnQuarterBoundary(DepositionContext(state2, world, cal2));
        var south2 = after2.NationById("south")!.LeaderName;
        Assert.NotEqual(south1, south2);

        // Save and resume between the two boundaries: the second draw is derived from the persisted root
        // seed and calendar alone, so it is unchanged.
        var save = new SaveGame(
            GameDataSchema.CurrentVersion, "t146-deposition", "T146", toy.Scenario.Id, world.Id,
            toy.Ruleset.Id, state2);
        var loaded = SaveManager.Load(
            "t146-deposition.json", SaveManager.Serialize(save), world, toy.Ruleset);

        var after2b = handler.OnQuarterBoundary(DepositionContext(loaded.State, world, cal2));
        Assert.Equal(south2, after2b.NationById("south")!.LeaderName);
    }

    private static QuarterBoundaryContext DepositionContext(GameState state, World world, CalendarState cal) =>
        new(
            state with { Calendar = cal },
            CoreTestbed.Toy.Ruleset,
            world,
            EndingSeasonIndex: 0,
            new ScriptedRng(
                nextChanceDraws: new[] { true },
                expectedNextChanceOdds: new[] { (1, CoreTestbed.Toy.Ruleset.Economy.DepositionRandomDivisor) }),
            new RecordingEventSink());

    // ---- Done-when 4: a human fall through GameSession ------------------------------------------

    [Fact]
    public void A_human_seat_deposed_at_turn_start_keeps_the_fall_leader_and_redraws_from_the_pool()
    {
        var toy = CoreTestbed.Toy;
        var ruleset = toy.Ruleset;

        var leader = toy.World.NationById("north")!.LeaderName;
        var names = ValueList.Of(new[] { leader }.Concat(Enumerable.Range(0, 11).Select(i => $"leader-{i}")).ToArray());
        var unity = ruleset.Economy.DebtUnityThreshold - 50;

        var world = toy.World with
        {
            Nations = ValueList.From(toy.World.Nations.Select(n =>
                n.Id == "north" ? n with { LeaderNames = names, Unity = unity, Treasury = 500 } : n)),
        };
        var worldWithoutPool = world with
        {
            Nations = ValueList.From(world.Nations.Select(n =>
                n.Id == "north" ? n with { LeaderNames = null } : n)),
        };

        var session = new GameSession(world, ruleset, toy.Scenario);
        var output = session.Submit("status");

        var fall = Assert.Single(output.SeatFalls);
        Assert.Equal("north", fall.NationId);
        Assert.Equal(leader, fall.LeaderName); // the window's pre-fall leader
        var redrawn = session.State.NationById("north")!.LeaderName;
        Assert.Contains(redrawn, names);

        // The CLI lines do not depend on the pool: the same fall on the same world without one prints
        // exactly the same lines.
        var noPoolOutput = new GameSession(worldWithoutPool, ruleset, toy.Scenario).Submit("status");
        Assert.Equal(output.Lines, noPoolOutput.Lines);
    }
}
