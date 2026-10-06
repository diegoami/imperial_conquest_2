using System.Reflection;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// <c>docs/tasks/T138.md</c> Done-when 1–3: the engine exposes each human seat's fall on
/// <see cref="SessionOutput.SeatFalls"/>, one test per reason, hotseat's partial loss, and the
/// once-per-call dedup.
/// </summary>
/// <remarks>
/// Every session here is built on <c>toy-3city</c> from <see cref="CoreTestbed.Toy"/> (its two nations,
/// three cities and fixed turn order), or on the same real-play elimination/conquest fixtures
/// <c>SeatCliTests</c> already uses — no <see cref="NationState"/> is set by reflection unless the case
/// genuinely cannot be reached through play (the hotseat second fall, where the seat is healthy until the
/// test script says otherwise).
/// </remarks>
public sealed class SeatFallOutputTests
{
    private static ResolvedScenario Toy => CoreTestbed.Toy;

    /// <summary>
    /// Done-when 1, reason <see cref="SeatFallReason.AllCities"/>: a human seat owning every city at its
    /// turn start. Meridia is moved to north in the world input, so north holds all three at construction
    /// (<c>AdvanceToHumanSeat</c>'s own turn-start check) and the fall arrives with the first
    /// <see cref="GameSession.Submit"/>'s own prelude.
    /// </summary>
    [Fact]
    public void A_human_seat_holding_every_city_yields_AllCities_with_the_conquest_text()
    {
        var world = Toy.World with
        {
            Cities = ValueList.From(Toy.World.Cities.Select(c => c with { Owner = "north" })),
        };
        var session = new GameSession(world, Toy.Ruleset, Toy.Scenario);
        var northAtStart = Toy.World.NationById("north")!;

        var output = session.Submit("status");

        var fall = Assert.Single(output.SeatFalls);
        Assert.Equal("north", fall.NationId);
        Assert.Equal(SeatFallReason.AllCities, fall.Reason);
        Assert.Null(fall.ConquerorNationId);
        Assert.Equal("You have conquerred the Mediterranean, a unique achievement.", fall.Text);
        Assert.Contains(fall.Text, output.Lines);
        Assert.Equal(fall.LeaderName, session.State.NationById("north")!.LeaderName);
        Assert.Equal(session.State.Calendar.YearBc, fall.YearBc);
        Assert.Equal(northAtStart.Wealth, fall.EndWealth);
        Assert.Equal(3, fall.EndCityCount);
        Assert.Equal(northAtStart.Treasury, fall.EndTreasury);
        Assert.True(output.GameOver);
    }

    /// <summary>
    /// Done-when 1, reason <see cref="SeatFallReason.HardEndYear"/>: a state at or past
    /// <c>victory.hardEndYearBc</c>. The ruleset's own 250 BC start year makes
    /// <see cref="IC2.Engine.Economy.Deposition.ShouldFallAtHumanTurnStart"/>'s first branch fire, never a
    /// re-invented literal.
    /// </summary>
    [Fact]
    public void A_state_past_the_hard_end_year_yields_HardEndYear()
    {
        var ruleset = Toy.Ruleset with
        {
            Calendar = Toy.Ruleset.Calendar with { StartYearBc = Toy.Ruleset.Victory.HardEndYearBc },
        };
        var session = new GameSession(Toy.World, ruleset, Toy.Scenario);
        var northAtStart = Toy.World.NationById("north")!;

        var output = session.Submit("status");

        var fall = Assert.Single(output.SeatFalls);
        Assert.Equal("north", fall.NationId);
        Assert.Equal(SeatFallReason.HardEndYear, fall.Reason);
        Assert.Equal("You have reached the end of your allotted 20 years.", fall.Text);
        Assert.Contains(fall.Text, output.Lines);
        Assert.Equal(fall.LeaderName, session.State.NationById("north")!.LeaderName);
        Assert.Equal(session.State.Calendar.YearBc, fall.YearBc);
        Assert.Equal(northAtStart.Wealth, fall.EndWealth);
        Assert.Equal(2, fall.EndCityCount);
        Assert.True(output.GameOver);
    }

    /// <summary>
    /// Done-when 1, reason <see cref="SeatFallReason.Unpopularity"/>: unity below
    /// <c>economy.debtUnityThreshold</c> with a treasury that is not the binding term. North's treasury
    /// starts positive, so the recorded end treasury is the pre-<c>FUN_0044C8F0</c> value the original's
    /// window reads — the deposition then credits it, which this asserts the record did <em>not</em> pick
    /// up (<see cref="SeatFall"/>'s own remarks).
    /// </summary>
    [Fact]
    public void Unity_below_the_threshold_yields_Unpopularity()
    {
        var ruleset = Toy.Ruleset;
        var unity = ruleset.Economy.DebtUnityThreshold - 50;
        var session = SessionWithNorth(n => n with { Unity = unity, Treasury = 500 });
        var northAtStart = Toy.World.NationById("north")!;

        var output = session.Submit("status");

        var fall = Assert.Single(output.SeatFalls);
        Assert.Equal(SeatFallReason.Unpopularity, fall.Reason);
        Assert.Equal("Your unpopularity has forced the army to overthrow you.", fall.Text);
        Assert.Contains(fall.Text, output.Lines);
        Assert.Equal(500, fall.EndTreasury);
        Assert.NotEqual(session.State.NationById("north")!.Treasury, fall.EndTreasury);
        Assert.Equal(fall.LeaderName, session.State.NationById("north")!.LeaderName);
        Assert.Equal(session.State.Calendar.YearBc, fall.YearBc);
        Assert.Equal(northAtStart.Wealth, fall.EndWealth);
        Assert.True(output.GameOver);
    }

    /// <summary>
    /// Done-when 1, reason <see cref="SeatFallReason.Unpaid"/>: a treasury below the debt limit with unity
    /// at or above <c>economy.debtUnityThreshold</c> — <see cref="IC2.Engine.Economy.Deposition.InDebt"/>'s
    /// treasury term alone. The recorded end treasury is the negative one that caused the fall, not the
    /// post-fall 0 the credit leaves.
    /// </summary>
    [Fact]
    public void A_treasury_below_the_debt_limit_yields_Unpaid()
    {
        var session = SessionWithNorth(n => n with { Unity = 600, Treasury = -1_000 });
        var northAtStart = Toy.World.NationById("north")!;

        var output = session.Submit("status");

        var fall = Assert.Single(output.SeatFalls);
        Assert.Equal(SeatFallReason.Unpaid, fall.Reason);
        Assert.Equal("Your army have deposed you because they have not been paid.", fall.Text);
        Assert.Contains(fall.Text, output.Lines);
        Assert.Equal(-1_000, fall.EndTreasury);
        Assert.Equal(0, session.State.NationById("north")!.Treasury);
        Assert.Equal(fall.LeaderName, session.State.NationById("north")!.LeaderName);
        Assert.Equal(session.State.Calendar.YearBc, fall.YearBc);
        Assert.Equal(northAtStart.Wealth, fall.EndWealth);
        Assert.True(output.GameOver);
    }

    /// <summary>
    /// Done-when 1, reason <see cref="SeatFallReason.Conquered"/>: the human seat's last city is taken in
    /// an AI seat's own turn, through real play (<c>SeatCliTests.A_hotseat_conquest_of_the_last_human_seat_reaches_game_over</c>'s
    /// own fixture). South owns one city, north's overwhelming army besieges it in round 2.
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task A_last_city_captured_in_an_ai_turn_yields_Conquered_naming_the_captor()
    {
        await Task.Run(() =>
        {
            var session = HotseatConquestSession();
            Assert.Equal("south", session.State.ActiveNationId);

            var round1 = session.Submit("end");
            Assert.False(session.State.NationById("south")!.Eliminated, "round 1 is only the approach march");
            Assert.Empty(round1.SeatFalls);

            var round2 = session.Submit("end");

            var fall = Assert.Single(round2.SeatFalls);
            Assert.Equal("south", fall.NationId);
            Assert.Equal(SeatFallReason.Conquered, fall.Reason);
            Assert.Equal("north", fall.ConquerorNationId);
            Assert.Equal("Your nation has been conquerred by Northern League (north).", fall.Text);
            Assert.Contains(fall.Text, round2.Lines);
            Assert.Equal(0, fall.EndCityCount);

            var southNow = session.State.NationById("south")!;
            Assert.Equal(southNow.Wealth, fall.EndWealth);
            Assert.Equal(southNow.Treasury, fall.EndTreasury);
            Assert.Equal(southNow.LeaderName, fall.LeaderName);
            Assert.Equal(session.State.Calendar.YearBc, fall.YearBc);
            Assert.True(round2.GameOver);
        });
    }

    /// <summary>
    /// Done-when 2: with two human seats, one falling yields exactly one <see cref="SeatFall"/> for it,
    /// <see cref="SessionOutput.GameOver"/> is false, and the other seat still plays; when the second falls
    /// too, the game is over. North falls at the turn start it reaches at construction; south is healthy
    /// until the test scripts its own fall (a state no real toy-world turn reaches in one round).
    /// </summary>
    [Fact]
    public void Hotseat_one_fall_then_the_other_ends_the_game()
    {
        var unity = Toy.Ruleset.Economy.DebtUnityThreshold - 50;
        var world = Toy.World with
        {
            Nations = ValueList.From(Toy.World.Nations.Select(n =>
                n.Id == "north" ? n with { Unity = unity } : n)),
        };
        var scenario = Toy.Scenario with
        {
            Seats = ValueList.From(Toy.Scenario.Seats.Select(s => s with { Control = SeatControl.Human })),
        };
        var session = new GameSession(world, Toy.Ruleset, scenario);

        var first = session.Submit("end");

        var northFall = Assert.Single(first.SeatFalls);
        Assert.Equal("north", northFall.NationId);
        Assert.False(first.GameOver);
        Assert.False(session.IsGameOver);

        // The other human seat still gives orders.
        var second = session.Submit("status");
        Assert.DoesNotContain(second.Lines, l => l.Contains("No further commands", StringComparison.Ordinal));
        Assert.False(second.GameOver);

        // Now the last human seat falls too.
        ForceNationState(session, "south", n => n with { Unity = unity });
        var third = session.Submit("end");

        var southFall = Assert.Single(third.SeatFalls);
        Assert.Equal("south", southFall.NationId);
        Assert.True(third.GameOver);
        Assert.True(session.IsGameOver);
    }

    /// <summary>
    /// Done-when 3: a seat reported by both
    /// <c>AppendFallMessagesForNewlyLostHumanSeats</c> and <c>AnnounceAndAdoptWatchModeIfSeatIsLost</c> in
    /// one call yields one <see cref="SeatFall"/>. A <c>--seat</c> session's own nation eliminated by an AI
    /// seat's turn reaches both: the first prints the specific "conquerred by" line, the second the
    /// watch-mode notice.
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task A_fall_reported_twice_in_one_call_yields_one_SeatFall()
    {
        await Task.Run(() =>
        {
            var session = EliminationFixtureSession(ValueList.Of("south", "north"), "south", seed: 2);
            Assert.Equal("south", session.State.ActiveNationId);

            var round1 = session.Submit("end");
            Assert.Empty(round1.SeatFalls);

            var round2 = session.Submit("end");

            Assert.True(session.State.NationById("south")!.Eliminated);
            // The generic watch-mode notice is on the same call as the specific reason text.
            Assert.Contains(round2.Lines, l => l.Contains("has fallen", StringComparison.Ordinal));
            Assert.Contains(
                round2.Lines, l => l.Contains("Your nation has been conquerred by", StringComparison.Ordinal));

            var fall = Assert.Single(round2.SeatFalls);
            Assert.Equal("south", fall.NationId);
            Assert.Equal(SeatFallReason.Conquered, fall.Reason);
            var southNow = session.State.NationById("south")!;
            Assert.Equal(southNow.Wealth, fall.EndWealth);
            Assert.Equal(southNow.LeaderName, fall.LeaderName);
            Assert.Equal(session.State.Calendar.YearBc, fall.YearBc);
            Assert.True(round2.GameOver);
        });
    }

    // ---- fixtures ----

    /// <summary>
    /// <c>toy-3city</c> with one change to north's own definition — the shape
    /// <c>SeatCliTests.NewEliminationFixtureSession</c> already uses for the same reason.
    /// </summary>
    private static GameSession SessionWithNorth(Func<NationDefinition, NationDefinition> change)
    {
        var world = Toy.World with
        {
            Nations = ValueList.From(Toy.World.Nations.Select(n =>
                string.Equals(n.Id, "north", StringComparison.Ordinal) ? change(n) : n)),
        };
        return new GameSession(world, Toy.Ruleset, Toy.Scenario);
    }

    /// <summary>
    /// <c>SeatCliTests.NewEliminationFixtureSession</c>'s own shape: north's one starting army is replaced
    /// with a 400,000-archer force beside south's only city and both treasuries zeroed, so north's AI turn
    /// (round 2) eliminates the <c>--seat south</c> seat through real play.
    /// </summary>
    private static GameSession EliminationFixtureSession(
        ValueList<string> turnOrder, string humanSeatNationId, ulong? seed = null)
    {
        var world = Toy.World with
        {
            Nations = ValueList.Of(
                Toy.World.NationById("north")! with { Treasury = 0, Wealth = 40_000 },
                Toy.World.NationById("south")! with { Treasury = 0 }),
            StartingArmies = ValueList.Of(
                new StartingArmy(
                    "north-overwhelming-army", "north", X: 3, Y: 2, Morale: 60, Money: 0, SupplyTons: 0,
                    Moves: 1, Units: ValueList.Of(CaptureFixtures.Unit("archers", 400_000)))),
            TurnOrder = turnOrder,
        };
        var scenario = Toy.Scenario with
        {
            Seats = ValueList.Of(
                new Seat("south", SeatControl.Ai),
                new Seat(
                    "north", SeatControl.Ai,
                    new AiPersonality(Aggression: 0.5, ExpansionDrive: 0.5, LoyaltyToAlliances: 0.5))),
        };
        return new GameSession(world, Toy.Ruleset, scenario, seed, humanSeatNationId);
    }

    /// <summary>
    /// <c>SeatCliTests.A_hotseat_conquest_of_the_last_human_seat_reaches_game_over</c>'s own fixture: south
    /// is the scenario's only human seat, north's overwhelming army takes Meridia in round 2.
    /// </summary>
    private static GameSession HotseatConquestSession()
    {
        var world = Toy.World with
        {
            Nations = ValueList.Of(
                Toy.World.NationById("north")! with { Treasury = 0, Wealth = 40_000 },
                Toy.World.NationById("south")! with { Treasury = 0 }),
            StartingArmies = ValueList.Of(
                new StartingArmy(
                    "north-overwhelming-army", "north", X: 3, Y: 2, Morale: 60, Money: 0, SupplyTons: 0,
                    Moves: 1, Units: ValueList.Of(CaptureFixtures.Unit("archers", 400_000)))),
            TurnOrder = ValueList.Of("south", "north"),
        };
        var scenario = Toy.Scenario with
        {
            Seats = ValueList.Of(
                new Seat("south", SeatControl.Human),
                new Seat(
                    "north", SeatControl.Ai,
                    new AiPersonality(Aggression: 0.5, ExpansionDrive: 0.5, LoyaltyToAlliances: 0.5))),
        };
        return new GameSession(world, Toy.Ruleset, scenario, seedOverride: 2);
    }

    /// <summary>
    /// Forces <paramref name="mutate"/> onto <paramref name="nationId"/>'s live state — the same seam
    /// <c>SeatCliTests.ForceNationState</c> keeps for a case real play cannot script cheaply.
    /// </summary>
    private static void ForceNationState(
        GameSession session, string nationId, Func<NationState, NationState> mutate)
    {
        var current = session.State;
        var newState = current with
        {
            Nations = ValueList.From(current.Nations.Select(n =>
                string.Equals(n.Id, nationId, StringComparison.Ordinal) ? mutate(n) : n)),
        };

        typeof(GameSession).GetProperty(nameof(GameSession.State))!.SetValue(session, newState);
    }
}
