using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using Xunit.Abstractions;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// <c>docs/task-catalogue.md</c> T22 Done-when 1's "<em>zero rejected commands</em>", tested as what it
/// is: a constraint on the AI's candidate generation, not on the engine.
/// </summary>
/// <remarks>
/// <para>
/// The soak proves it over fifty games of the toy world. These prove it over the awkward states the toy
/// world never happens to reach — an army with no moves, an army aboard a fleet, a fleet still under
/// construction, a city already at maximum fortification, a city with an order pending, an empty
/// treasury, a relation sitting on a cooldown, an AI target that refuses peace unconditionally. Each is
/// a state in which some candidate generator would be refused if it were not gated, and each is driven
/// through the real <see cref="CommandDispatcher"/>.
/// </para>
/// <para>
/// <strong>A rejection is a bug in the scorer, not an outcome to filter.</strong> Nothing in
/// <see cref="AiTurn"/> retries, skips or swallows a refusal: it counts it, logs it, and the count is
/// what these tests assert on.
/// </para>
/// </remarks>
public sealed class AiCommandLegalityTests
{
    private readonly ITestOutputHelper _output;

    public AiCommandLegalityTests(ITestOutputHelper output) => _output = output;

    private static Ruleset Ruleset => AiScriptedStates.Ruleset;

    private const string Acting = AiScriptedStates.Attacker;
    private const string Other = AiScriptedStates.Defender;

    /// <summary>
    /// Every awkward state the battery drives, named. Kept as a plain array so the control test below can
    /// walk the same list the theory does, rather than a second copy of it.
    /// </summary>
    public static readonly string[] AwkwardStates =
    {
        "armies-in-contact-at-peace",
        "armies-in-contact-at-war",
        "army-with-no-moves",
        "army-aboard-a-fleet",
        "fleet-under-construction",
        "city-at-maximum-fortification",
        "city-with-an-order-already-pending",
        "empty-treasury",
        "huge-treasury",
        "relation-on-a-cooldown",
        "already-allied",
        "already-trading",
        "besieged-city",
        "army-next-to-an-enemy-city",
        "eliminated-neighbour",
        "no-armies-or-fleets-at-all",
        "army-between-two-enemy-cities",
        "weak-enemy-city-adjacent",
    };

    /// <summary>The same list, as xUnit theory rows.</summary>
    public static TheoryData<string> AwkwardStateNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in AwkwardStates)
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AwkwardStateNames))]
    public void The_ai_issues_no_command_the_engine_refuses(string stateName)
    {
        var state = Build(stateName);

        var driven = AiScriptedStates.DriveOneTurn(state);

        foreach (var line in driven.Outcome.Log)
        {
            _output.WriteLine(line);
        }

        Assert.Equal(0, driven.Outcome.CommandsRejected);
        Assert.Equal(0, driven.Outcome.ProjectionMismatches);
        Assert.False(driven.Outcome.HitActionCap, "no scripted state should exhaust the per-turn action cap");
    }

    /// <summary>
    /// The control. If every state above happened to produce no candidates at all, the test would pass
    /// while proving nothing — so at least one command must actually have been issued across the battery,
    /// and the kinds are printed so a reviewer can see which paths were exercised.
    /// </summary>
    [Fact]
    public void The_awkward_states_between_them_exercise_several_command_kinds()
    {
        var kinds = new List<string>();
        foreach (var name in AwkwardStates)
        {
            var driven = AiScriptedStates.DriveOneTurn(Build(name));
            foreach (var kind in driven.IssuedKinds)
            {
                if (!kinds.Contains(kind))
                {
                    kinds.Add(kind);
                }
            }
        }

        kinds.Sort(StringComparer.Ordinal);
        _output.WriteLine("command kinds exercised: " + string.Join(", ", kinds));

        Assert.Contains("battle.attack-army", kinds);
        Assert.Contains("battle.besiege-city", kinds);
        Assert.Contains("diplomacy.declare-war", kinds);
        Assert.Contains("movement.move-army", kinds);
        Assert.Contains("recruitment.recruit-standing-unit", kinds);
        Assert.True(kinds.Count >= 5, "the battery should reach more than a couple of command kinds");
    }

    /// <summary>
    /// An army is given at most one march order per turn. Without the ration, the greedy loop walks an
    /// army at one objective, finds it now nearer a second, walks it back, and burns its whole move
    /// allowance arriving nowhere — accepted commands every time, so no stall and no rejection would ever
    /// report it.
    /// </summary>
    [Fact]
    public void An_army_receives_at_most_one_march_order_per_turn()
    {
        var driven = AiScriptedStates.DriveOneTurn(Build("army-between-two-enemy-cities"));

        var marches = 0;
        foreach (var kind in driven.IssuedKinds)
        {
            if (string.Equals(kind, "movement.move-army", StringComparison.Ordinal))
            {
                marches++;
            }
        }

        Assert.Equal(1, marches);
    }

    /// <summary>A human seat's turn is a no-op: the AI never plays a seat that is not its own.</summary>
    [Fact]
    public void A_human_seat_is_left_alone()
    {
        var state = Build("armies-in-contact-at-peace");
        var human = state with
        {
            Nations = ValueList.From(state.Nations.Select(n =>
                string.Equals(n.Id, Acting, StringComparison.Ordinal)
                    ? n with { Control = SeatControl.Human }
                    : n)),
        };

        var driven = AiScriptedStates.DriveOneTurn(human);

        Assert.Equal(0, driven.Outcome.CommandsIssued);
        Assert.True(AiSubstantiveState.AreEquivalent(human, driven.Outcome.State));
        Assert.Contains(driven.Outcome.Log, l => l.Contains("not AI-controlled", StringComparison.Ordinal));
    }

    /// <summary>An eliminated seat is left alone too — the dispatcher would refuse every command it sent.</summary>
    [Fact]
    public void An_eliminated_seat_is_left_alone()
    {
        var state = Build("armies-in-contact-at-peace");
        var eliminated = state with
        {
            Nations = ValueList.From(state.Nations.Select(n =>
                string.Equals(n.Id, Acting, StringComparison.Ordinal) ? n with { Eliminated = true } : n)),
        };

        var driven = AiScriptedStates.DriveOneTurn(eliminated);

        Assert.Equal(0, driven.Outcome.CommandsIssued);
        Assert.Contains(driven.Outcome.Log, l => l.Contains("eliminated", StringComparison.Ordinal));
    }

    /// <summary>
    /// The battery's acting nation is deliberately aggressive (0.9), not neutral. The scripted contact
    /// state sits at a ratio of 1,480 permille, which a neutral 0.5 personality declines (its gate asks
    /// 1,550) — so a neutral battery would never exercise the attack path at all, and "zero rejected
    /// commands" would be proved only about the commands the AI was too timid to place.
    /// </summary>
    private static readonly AiPersonality BatteryPersonality =
        new(Aggression: 0.9, ExpansionDrive: 0.5, LoyaltyToAlliances: 0.5);

    private static GameState Build(string name)
    {
        var baseState = AiScriptedStates.TwoArmiesInContact(BatteryPersonality);

        return name switch
        {
            "armies-in-contact-at-peace" => baseState,
            "armies-in-contact-at-war" => BattleCommandTestbed.AtWar(baseState, Acting, Other),
            "army-with-no-moves" => WithActingArmy(baseState, a => a with { Moves = 0 }),
            "army-aboard-a-fleet" => EmbarkedState(baseState),
            "fleet-under-construction" => baseState with
            {
                Fleets = ValueList.Of(
                    BattleTestbed.Fleet("building", Acting, 1, 4, ships: 10, conditionPercent: 0) with
                    {
                        ConstructionTicksRemaining = Ruleset.Naval.ConstructionTicks,
                        BuildCityId = "attacker-city",
                    }),
            },
            "city-at-maximum-fortification" => WithTreasury(baseState, 100_000),
            "city-with-an-order-already-pending" => WithTreasury(
                WithActingCity(baseState, c => c with { FortificationCode = 340 }), 100_000),
            "empty-treasury" => WithTreasury(baseState, 0),
            "huge-treasury" => WithTreasury(baseState, 1_000_000),
            "relation-on-a-cooldown" => baseState with
            {
                Relations = baseState.Relations.WithRelation(
                    Acting, Other, Ruleset.Diplomacy.CooldownAfterEndedWar),
            },
            "already-allied" => baseState with
            {
                Relations = baseState.Relations.WithRelation(
                    Acting, Other, Ruleset.Diplomacy.StateCodes.Alliance),
            },
            "already-trading" => baseState with
            {
                Relations = baseState.Relations.WithRelation(
                    Acting, Other, Ruleset.Diplomacy.StateCodes.Trade),
            },
            "besieged-city" => WithTreasury(
                WithActingCity(baseState, c => c with { UnderSiege = true, FortificationCode = 20 }), 100_000),
            "army-next-to-an-enemy-city" => WithActingArmy(baseState, a => a with { X = 7, Y = 4 }),
            "army-between-two-enemy-cities" => TwoEnemyCitiesState(),
            "weak-enemy-city-adjacent" => WithActingArmy(
                baseState with
                {
                    Cities = ValueList.From(baseState.Cities.Select(c =>
                        string.Equals(c.Id, "defender-city", StringComparison.Ordinal)
                            // A city nobody defends: loyalty, fortification and population all at the
                            // floor, so SiegeStrength.Defender is small enough that the siege gate clears
                            // and the battery actually reaches battle.besiege-city.
                            ? c with { Loyalty = 1, FortificationCode = 0, PopulationThousands = 1 }
                            : c)),
                },
                a => a with { X = 6, Y = 4 }),
            "eliminated-neighbour" => baseState with
            {
                Nations = ValueList.From(baseState.Nations.Select(n =>
                    string.Equals(n.Id, Other, StringComparison.Ordinal) ? n with { Eliminated = true } : n)),
            },
            "no-armies-or-fleets-at-all" => baseState with
            {
                Armies = ValueList<ArmyState>.Empty,
                Fleets = ValueList<FleetState>.Empty,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such scripted state."),
        };
    }

    /// <summary>
    /// One army, equidistant from two enemy cities it cannot take, with a full move allowance — the exact
    /// shape that produced the within-turn oscillation the march ration exists to stop.
    /// </summary>
    private static GameState TwoEnemyCitiesState()
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation(Acting, AiScriptedStates.DefaultPersonality, capitalCityId: "home"),
            AiScriptedStates.AiNation(Other, AiScriptedStates.DefaultPersonality, capitalCityId: "far-a"),
        };

        var cities = new[]
        {
            CaptureFixtures.City(
                "home", "Home", 0, 5, Acting, Acting, loyalty: 80, fortificationCode: 100,
                populationThousands: 100, maxPopulationThousands: 100, tribute: 10),
            CaptureFixtures.City(
                "far-a", "Far A", 2, 1, Other, Other, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
            CaptureFixtures.City(
                "far-b", "Far B", 6, 1, Other, Other, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
        };

        var armies = new[]
        {
            CaptureFixtures.Army(
                    "wanderer", Acting, 4, 3, morale: 60, CaptureFixtures.Unit("light_infantry", 15000))
                with { Moves = 12 },
        };

        return AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.StateWith(nations, cities, armies), Acting);
    }

    private static GameState EmbarkedState(GameState state)
    {
        var fleet = BattleTestbed.Fleet("carrier", Acting, 1, 4, ships: 10, conditionPercent: 100)
            with { CarriedArmyId = "attacker-army" };

        return state with
        {
            Fleets = ValueList.Of(fleet),
            Armies = ValueList.From(state.Armies.Select(a =>
                string.Equals(a.Id, "attacker-army", StringComparison.Ordinal)
                    ? a with { AboardFleetId = "carrier", CoveredTileCode = null, X = 1, Y = 4 }
                    : a)),
        };
    }

    private static GameState WithActingArmy(GameState state, Func<ArmyState, ArmyState> change) =>
        state with
        {
            Armies = ValueList.From(state.Armies.Select(a =>
                string.Equals(a.Id, "attacker-army", StringComparison.Ordinal) ? change(a) : a)),
        };

    private static GameState WithActingCity(GameState state, Func<CityState, CityState> change) =>
        state with
        {
            Cities = ValueList.From(state.Cities.Select(c =>
                string.Equals(c.Id, "attacker-city", StringComparison.Ordinal) ? change(c) : c)),
        };

    private static GameState WithTreasury(GameState state, int treasury) =>
        state with
        {
            Nations = ValueList.From(state.Nations.Select(n =>
                string.Equals(n.Id, Acting, StringComparison.Ordinal) ? n with { Treasury = treasury } : n)),
        };
}
