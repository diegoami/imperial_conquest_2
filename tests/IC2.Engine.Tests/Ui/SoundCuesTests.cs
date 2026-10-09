using IC2.Engine.Assets;
using IC2.Engine.Battle;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Model;
using IC2.Engine.Naval;
using IC2.Slice.Audio;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T149 (correction task for bug #790) Done-when 1, the <c>SoundCuesTests</c> half: the
/// Godot-free events -> sounds mapping. Every event the original game plays a sound for must
/// map to the documented key, in the order the events are published, with the step count and
/// the human-only filter the cue list's own contract pins.
/// </summary>
/// <remarks>
/// The test exercises <see cref="SoundCues.ForEvents"/> against a <see cref="GameState"/>
/// it builds by hand: no real engine dispatch, so every assertion reads the cue list
/// directly. A computer seat's army move yields no cues (the original gates the step sound
/// on "current seat is human"); a human seat's move yields the Chebyshev step count, capped
/// at <see cref="SoundCues.MaxStepCues"/>. Other event types yield exactly their documented
/// key, including for computer nations (the original plays sounds 3-10 regardless of
/// ownership).
/// </remarks>
public sealed class SoundCuesTests
{
    // ---- Fixtures ----

    private const string HumanNation = "north";
    private const string ComputerNation = "south";

    private static GameState NewStateWithTwoNations()
    {
        // Two nations, both with one city; the south is the computer, the north the human.
        // The cue list never inspects a seat's "current" state, only the nation that owns
        // the moving unit, so the test's states can have the simplest shape.
        var toy = CoreTestbed.Toy;
        var state = CoreTestbed.InitialState();
        // Mark one nation as Human, the other as AI, so the cue list's NationState.Control
        // check can read both branches.
        var nations = state.Nations.Select(n => n.Id == HumanNation
            ? n with { Control = SeatControl.Human }
            : n with { Control = SeatControl.Ai }).ToList();
        return state with { Nations = ValueList.From(nations) };
    }

    private static IReadOnlyList<string> CuesForEvents(GameState state, params DomainEvent[] events) =>
        SoundCues.ForEvents(events, state);

    // ---- Step count and human-only filter ----

    [Fact]
    public void Human_army_move_of_three_cells_gives_three_unit_move_cues()
    {
        var state = NewStateWithTwoNations();
        // (10,10) to (13,11): Chebyshev = max(3, 1) = 3.
        var cues = CuesForEvents(state, new ArmyMoved(
            ArmyId: "a1", NationId: HumanNation,
            FromX: 10, FromY: 10, ToX: 13, ToY: 11, MovesSpent: 3));

        Assert.Equal(3, cues.Count);
        Assert.All(cues, c => Assert.Equal(AssetKeys.SfxUnitMove, c));
    }

    [Fact]
    public void Computer_army_move_gives_no_cues()
    {
        var state = NewStateWithTwoNations();
        var cues = CuesForEvents(state, new ArmyMoved(
            ArmyId: "a1", NationId: ComputerNation,
            FromX: 10, FromY: 10, ToX: 13, ToY: 11, MovesSpent: 3));

        Assert.Empty(cues);
    }

    [Fact]
    public void Human_army_move_of_twenty_cells_gives_twelve_unit_move_cues()
    {
        var state = NewStateWithTwoNations();
        // (10,10) to (30,10): Chebyshev = 20, capped at 12.
        var cues = CuesForEvents(state, new ArmyMoved(
            ArmyId: "a1", NationId: HumanNation,
            FromX: 10, FromY: 10, ToX: 30, ToY: 10, MovesSpent: 20));

        Assert.Equal(SoundCues.MaxStepCues, cues.Count);
        Assert.All(cues, c => Assert.Equal(AssetKeys.SfxUnitMove, c));
    }

    [Fact]
    public void Human_fleet_move_gives_fleet_move_cues()
    {
        var state = NewStateWithTwoNations();
        var cues = CuesForEvents(state, new FleetMoved(
            FleetId: "f1", NationId: HumanNation,
            FromX: 5, FromY: 5, ToX: 7, ToY: 6));

        // Chebyshev = max(2, 1) = 2.
        Assert.Equal(2, cues.Count);
        Assert.All(cues, c => Assert.Equal(AssetKeys.SfxFleetMove, c));
    }

    [Fact]
    public void Computer_fleet_move_gives_no_cues()
    {
        var state = NewStateWithTwoNations();
        var cues = CuesForEvents(state, new FleetMoved(
            FleetId: "f1", NationId: ComputerNation,
            FromX: 5, FromY: 5, ToX: 7, ToY: 6));

        Assert.Empty(cues);
    }

    [Fact]
    public void StepCount_caps_at_twelve()
    {
        Assert.Equal(0, SoundCues.StepCount(0, 0, 0, 0));
        Assert.Equal(1, SoundCues.StepCount(0, 0, 1, 0));
        Assert.Equal(3, SoundCues.StepCount(10, 10, 13, 11));
        Assert.Equal(SoundCues.MaxStepCues, SoundCues.StepCount(0, 0, 100, 0));
        Assert.Equal(SoundCues.MaxStepCues, SoundCues.StepCount(0, 0, -100, 0));
    }

    // ---- One-shot cues (sounds 6, 7, 8, 9, 10) ----

    [Fact]
    public void CityFallsToNation_gives_city_captured_even_for_computer()
    {
        var state = NewStateWithTwoNations();
        var cues = CuesForEvents(state, new CityFallsToNation(
            CityName: "arx", OldOwner: HumanNation, NewOwner: ComputerNation));

        Assert.Equal(new[] { AssetKeys.SfxCityCaptured }, cues);
    }

    [Fact]
    public void CityFailsToBeCaptured_gives_siege_failed()
    {
        var state = NewStateWithTwoNations();
        var cues = CuesForEvents(state, new CityFailsToBeCaptured(
            AttackerNation: ComputerNation, CityName: "arx", DefenderNation: HumanNation));

        Assert.Equal(new[] { AssetKeys.SfxSiegeFailed }, cues);
    }

    [Fact]
    public void BattleFleetSunk_gives_fleet_sunk()
    {
        var state = NewStateWithTwoNations();
        var cues = CuesForEvents(state, new BattleFleetSunk(
            Winner: HumanNation, Loser: ComputerNation));

        Assert.Equal(new[] { AssetKeys.SfxFleetSunk }, cues);
    }

    [Fact]
    public void FleetLostAtSea_gives_fleet_sunk()
    {
        var state = NewStateWithTwoNations();
        var cues = CuesForEvents(state, new FleetLostAtSea(Nation: ComputerNation));

        Assert.Equal(new[] { AssetKeys.SfxFleetSunk }, cues);
    }

    [Fact]
    public void FleetScuttled_gives_fleet_sunk()
    {
        var state = NewStateWithTwoNations();
        var cues = CuesForEvents(state, new FleetScuttled(
            FleetId: "f1", NationId: HumanNation));

        Assert.Equal(new[] { AssetKeys.SfxFleetSunk }, cues);
    }

    [Fact]
    public void Field_battle_resolved_gives_battle()
    {
        var state = NewStateWithTwoNations();
        // The cue list only triggers for a field battle; a siege or naval battle has its own
        // sound in the original, but the clone's tactical screen is post-v0.6.0 and these
        // are not played today. A BattleResult with Kind != Field yields no cue.
        var fieldResult = new BattleResult(
            Kind: BattleKind.Field,
            AttackerId: "a1", DefenderId: "a2",
            AttackerNationId: HumanNation, DefenderNationId: ComputerNation,
            AttackerPower: 100, DefenderPower: 80,
            Winner: BattleSide.Attacker,
            AppliedDefeatOutcome: null,
            LoserFate: LoserFate.Destroyed,
            WinnerCasualties: 10, LoserCasualties: 50,
            UnitCasualties: ValueList<UnitCasualty>.Empty, Promotions: ValueList<UnitPromotion>.Empty,
            AbsorbedMoney: 0, AbsorbedSupplyTons: 0,
            WinnerUnityDelta: 0, LoserUnityDelta: 0,
            WinnerShipsLost: 0, WinnerConditionLost: 0, WinnerUnitsLost: 0,
            PeaceTreatyFired: false, PeaceTreatyOffered: false,
            Scatter: null);
        var cues = CuesForEvents(state, new BattleResolved(fieldResult));

        Assert.Equal(new[] { AssetKeys.SfxBattle }, cues);
    }

    [Fact]
    public void Siege_or_naval_battle_resolved_gives_no_battle_cue()
    {
        var state = NewStateWithTwoNations();
        var siegeResult = new BattleResult(
            Kind: BattleKind.Siege,
            AttackerId: "a1", DefenderId: "arx",
            AttackerNationId: HumanNation, DefenderNationId: ComputerNation,
            AttackerPower: 100, DefenderPower: 80,
            Winner: BattleSide.Attacker,
            AppliedDefeatOutcome: null,
            LoserFate: LoserFate.Unaffected,
            WinnerCasualties: 0, LoserCasualties: 0,
            UnitCasualties: ValueList<UnitCasualty>.Empty, Promotions: ValueList<UnitPromotion>.Empty,
            AbsorbedMoney: 0, AbsorbedSupplyTons: 0,
            WinnerUnityDelta: 0, LoserUnityDelta: 0,
            WinnerShipsLost: 0, WinnerConditionLost: 0, WinnerUnitsLost: 0,
            PeaceTreatyFired: false, PeaceTreatyOffered: false,
            Scatter: null);
        var cues = CuesForEvents(state, new BattleResolved(siegeResult));

        Assert.Empty(cues);
    }

    [Fact]
    public void NationConquered_gives_nation_conquered()
    {
        var state = NewStateWithTwoNations();
        var cues = CuesForEvents(state, new NationConquered(
            ConqueringNation: HumanNation, ConqueredNation: ComputerNation));

        Assert.Equal(new[] { AssetKeys.SfxNationConquered }, cues);
    }

    // ---- Ordering and event list ----

    [Fact]
    public void Events_in_one_call_give_their_cues_in_event_order()
    {
        var state = NewStateWithTwoNations();
        var cues = SoundCues.ForEvents(new DomainEvent[]
        {
            new ArmyMoved(ArmyId: "a1", NationId: HumanNation,
                FromX: 0, FromY: 0, ToX: 2, ToY: 0, MovesSpent: 2),
            new CityFallsToNation(CityName: "arx", OldOwner: ComputerNation, NewOwner: HumanNation),
            new NationConquered(ConqueringNation: HumanNation, ConqueredNation: ComputerNation),
        }, state);

        // The first event yields two unit_move cues (Chebyshev = 2), the second yields one
        // city_captured, the third yields one nation_conquered -- in that order.
        Assert.Equal(
            new[] { AssetKeys.SfxUnitMove, AssetKeys.SfxUnitMove,
                    AssetKeys.SfxCityCaptured, AssetKeys.SfxNationConquered },
            cues);
    }

    [Fact]
    public void An_event_outside_the_list_gives_no_cues()
    {
        var state = NewStateWithTwoNations();
        var cues = SoundCues.ForEvents(new DomainEvent[]
        {
            new FleetFinished(Nation: HumanNation, CityName: "arx"),
            new FleetDamagedInStorm(Nation: HumanNation),
        }, state);

        Assert.Empty(cues);
    }

    [Fact]
    public void An_empty_event_list_gives_no_cues()
    {
        var state = NewStateWithTwoNations();
        var cues = SoundCues.ForEvents(Array.Empty<DomainEvent>(), state);

        Assert.Empty(cues);
    }
}
