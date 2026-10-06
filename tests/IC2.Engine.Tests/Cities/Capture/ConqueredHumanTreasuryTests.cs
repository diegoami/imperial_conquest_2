using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// T146 Done-when 7: a <em>human</em> loser's treasury gets <c>FUN_0044c8f0</c>'s
/// <c>treasury &lt; 0 ? 0 : treasury + 1000</c> credit in both elimination paths
/// (<see cref="ConquestCascade"/> and <see cref="NationElimination.ApplyIfLastCityLost"/>), a computer
/// loser keeps its treasury, the winner's own copy happens <em>before</em> the credit, and the
/// game-end window records the pre-credit figure.
/// </summary>
public sealed class ConqueredHumanTreasuryTests
{
    private static Ruleset Ruleset => CaptureTestbed.Ruleset;

    private static NationState Loser(int treasury, SeatControl control, string? capital = null) =>
        CaptureTestbed.Nation("loser", treasury: treasury, unity: 668, capitalCityId: capital) with
        {
            Control = control,
        };

    private static GameState ConquestState(int loserTreasury, SeatControl loserControl)
    {
        var loserCity = CaptureTestbed.City(
            "loser-city", "LoserCity", 0, 0, "loser", "loser", loyalty: 30, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 0);
        var winnerCity = CaptureTestbed.City(
            "winner-city", "WinnerCity", 5, 5, "winner", "winner", loyalty: 90, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 0);

        return CaptureTestbed.StateWith(
            new[] { Loser(loserTreasury, loserControl, capital: "loser-city"), CaptureTestbed.Nation("winner", treasury: 0, unity: 600) },
            new[] { loserCity, winnerCity });
    }

    private static GameState EliminationState(int loserTreasury, SeatControl loserControl)
    {
        var winnerCity = CaptureTestbed.City(
            "winner-city", "WinnerCity", 5, 5, "winner", "winner", loyalty: 90, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 0);

        return CaptureTestbed.StateWith(
            new[] { Loser(loserTreasury, loserControl), CaptureTestbed.Nation("winner", unity: 600) },
            new[] { winnerCity });
    }

    [Fact]
    public void Conquest_of_a_human_loser_credits_its_treasury_and_the_winner_copies_the_pre_credit_value()
    {
        var result = ConquestCascade.Apply(
            ConquestState(315, SeatControl.Human), Ruleset, "loser", "winner", new RecordingEventSink());

        Assert.Equal(1_315, result.NationById("loser")!.Treasury);
        Assert.Equal(315, result.NationById("winner")!.Treasury);
        Assert.Equal(0, result.NationById("loser")!.Unity);
        Assert.Equal(SeatControl.Ai, result.NationById("loser")!.Control);
    }

    [Fact]
    public void Conquest_of_a_human_loser_below_zero_floors_to_zero()
    {
        var result = ConquestCascade.Apply(
            ConquestState(-64, SeatControl.Human), Ruleset, "loser", "winner", new RecordingEventSink());

        Assert.Equal(0, result.NationById("loser")!.Treasury);
        Assert.Equal(0, result.NationById("winner")!.Treasury);
    }

    [Fact]
    public void Conquest_of_a_computer_loser_keeps_its_treasury()
    {
        var result = ConquestCascade.Apply(
            ConquestState(315, SeatControl.Ai), Ruleset, "loser", "winner", new RecordingEventSink());

        Assert.Equal(315, result.NationById("loser")!.Treasury);
        Assert.Equal(315, result.NationById("winner")!.Treasury);
    }

    [Fact]
    public void Elimination_of_a_human_loser_credits_its_treasury()
    {
        var state = EliminationState(315, SeatControl.Human);
        var (eliminated, justEliminated) =
            NationElimination.ApplyIfLastCityLost(state, state.NationById("loser")!, Ruleset, "winner");

        Assert.True(justEliminated);
        Assert.Equal(1_315, eliminated.Treasury);
        Assert.Equal(SeatControl.Ai, eliminated.Control);
    }

    [Fact]
    public void Elimination_of_a_human_loser_below_zero_floors_to_zero()
    {
        var state = EliminationState(-64, SeatControl.Human);
        var (eliminated, _) =
            NationElimination.ApplyIfLastCityLost(state, state.NationById("loser")!, Ruleset, "winner");

        Assert.Equal(0, eliminated.Treasury);
    }

    [Fact]
    public void Elimination_of_a_computer_loser_keeps_its_treasury()
    {
        var state = EliminationState(315, SeatControl.Ai);
        var (eliminated, _) =
            NationElimination.ApplyIfLastCityLost(state, state.NationById("loser")!, Ruleset, "winner");

        Assert.Equal(315, eliminated.Treasury);
    }

    // ---- Through GameSession: the window keeps the pre-credit figure, the state is credited -------

    private static ValueList<string> Pool() =>
        ValueList.Of(Enumerable.Range(0, 12).Select(i => $"leader-{i}").ToArray());

    /// <summary>
    /// <c>SeatFallOutputTests.HotseatConquestSession</c>'s own fixture with south's treasury set to the
    /// report's 315 and a leader pool, so the conquest credits it and redraws its leader.
    /// </summary>
    private static GameSession HotseatConquestSession()
    {
        var toy = CoreTestbed.Toy;
        var pool = Pool();
        var world = toy.World with
        {
            Nations = ValueList.Of(
                toy.World.NationById("north")! with { Treasury = 0, Wealth = 40_000 },
                toy.World.NationById("south")! with { Treasury = 315, LeaderName = pool[0], LeaderNames = pool }),
            StartingArmies = ValueList.Of(
                new StartingArmy(
                    "north-overwhelming-army", "north", X: 3, Y: 2, Morale: 60, Money: 0, SupplyTons: 0,
                    Moves: 1, Units: ValueList.Of(CaptureTestbed.Unit("archers", 400_000)))),
            TurnOrder = ValueList.Of("south", "north"),
        };
        var scenario = toy.Scenario with
        {
            Seats = ValueList.Of(
                new Seat("south", SeatControl.Human),
                new Seat(
                    "north", SeatControl.Ai,
                    new AiPersonality(Aggression: 0.5, ExpansionDrive: 0.5, LoyaltyToAlliances: 0.5))),
        };
        return new GameSession(world, toy.Ruleset, scenario, seedOverride: 2);
    }

    [Fact(Timeout = 15000)]
    public async Task A_human_seat_conquered_in_a_hotseat_game_records_315_and_ends_at_1315()
    {
        await Task.Run(() =>
        {
            var session = HotseatConquestSession();
            var pool = Pool();

            session.Submit("end"); // round 1: the approach march.
            var round2 = session.Submit("end");

            var fall = Assert.Single(round2.SeatFalls);
            Assert.Equal("south", fall.NationId);
            Assert.Equal(SeatFallReason.Conquered, fall.Reason);
            Assert.Equal(315, fall.EndTreasury);

            var south = session.State.NationById("south")!;
            Assert.Equal(1_315, south.Treasury);
            Assert.Contains(south.LeaderName, pool);
        });
    }

    /// <summary>
    /// T146 rework round 1 (Opus review R1, the reviewer's own exact probe): the ending seat's own turn can
    /// eliminate it inside the same <c>RunTurn</c> that also runs the quarterly tick — quarterly billing
    /// (upkeep, Order 0) moves the treasury, then the rebellion (Order 100) defects the last city away and
    /// applies the fall's own <c>+1000</c>. The game-end window opens before that credit, so
    /// <see cref="SeatFall.EndTreasury"/> must be south's post-upkeep, pre-credit 354 — never 450, its
    /// value at the whole turn's start, which was the snapshot the earlier revision wrongly read.
    /// </summary>
    /// <remarks>
    /// The fixture is <c>SeatCliTests.ATurnEndingSeatsOwnRoundScopedQuarterTick_CanEliminateItThatSameEnd</c>
    /// reproduced here (that file is not this task's to change): "meridia" is south's only city but not its
    /// capital, with allegiance "north" and loyalty 5, and the ruleset's own
    /// <see cref="CalendarRules.StartWeek"/> is moved to <see cref="CalendarRules.SeasonAdvanceFromWeek"/>
    /// so the very first round already wraps the quarter boundary.
    /// </remarks>
    [Fact(Timeout = 15000)]
    public async Task A_human_seat_eliminated_by_the_quarters_rebellion_records_the_pre_credit_treasury()
    {
        await Task.Run(() =>
        {
            var toy = CoreTestbed.Toy;
            var ownerNation = new NationDefinition(
                Id: "owner", Name: "Ownership League", ColorHex: "#795548", LeaderName: "Toy Leader of Ownership",
                CapitalCityId: "portus", Treasury: 400, Unity: 600, Wealth: 300, TaxBase: 100, TaxRatePercent: 15,
                MobilizedPercent: 10, Population: 80);
            var portusToOwner = toy.World.Cities.Single(c => c.Id == "portus") with { Owner = "owner", Allegiance = "owner" };
            var meridiaRebellionCandidate = toy.World.Cities.Single(c => c.Id == "meridia") with
            {
                Allegiance = "north",
                Loyalty = 5,
            };
            var southNoCapital = toy.World.NationById("south")! with { CapitalCityId = null };

            var world = toy.World with
            {
                Nations = ValueList.From(
                    toy.World.Nations.Select(n => n.Id == "south" ? southNoCapital : n).Append(ownerNation)),
                Cities = ValueList.From(
                    toy.World.Cities.Select(c => c.Id switch
                    {
                        "portus" => portusToOwner,
                        "meridia" => meridiaRebellionCandidate,
                        _ => c,
                    })),
                TurnOrder = ValueList.Of("owner", "north", "south"),
            };
            var ruleset = toy.Ruleset with
            {
                Calendar = toy.Ruleset.Calendar with { StartWeek = toy.Ruleset.Calendar.SeasonAdvanceFromWeek },
            };
            var scenario = toy.Scenario with
            {
                Seats = ValueList.From(
                    toy.Scenario.Seats.Select(s => s.Nation == "south" ? s with { Control = SeatControl.Human, Personality = null } : s)
                        .Append(new Seat("owner", SeatControl.Ai))),
            };
            var session = new GameSession(world, ruleset, scenario);

            session.Submit("end"); // north's own turn: nothing for it to do, pauses on south next.
            var southEnd = session.Submit("end"); // south's own end, which is the round's last seat too.

            Assert.True(session.State.NationById("south")!.Eliminated);
            var fall = Assert.Single(southEnd.SeatFalls);
            Assert.Equal("south", fall.NationId);
            // 354, not 450: the pre-credit treasury after the same turn's quarterly billing, before the
            // rebellion's own +1000 credit.
            Assert.Equal(354, fall.EndTreasury);
            Assert.Equal(1_354, session.State.NationById("south")!.Treasury);
        });
    }
}
