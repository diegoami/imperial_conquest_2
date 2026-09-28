using IC2.Engine.Model;
using IC2.Engine.Tests.Core;
using IC2.Slice.Screens;
using Xunit;

namespace IC2.Engine.Tests.Ui.Screens;

/// <summary>
/// <c>docs/tasks/T25.md</c> Done-when: "the blind-handoff toggle is read from the scenario," plus the
/// widened Done-when's own "ending a human seat's turn in a two-human hotseat game shows the handoff."
/// </summary>
public sealed class HotseatHandoffDetectorTests
{
    [Fact]
    public void Detects_a_handoff_when_the_active_seat_passes_from_one_human_to_another()
    {
        var southActive = ActiveAs(TwoHumanState(), "south");

        var info = HotseatHandoffDetector.Detect(
            southActive, TwoHumanScenario(blind: false), previousActiveNationId: "north", previousActiveWasHuman: true);

        Assert.NotNull(info);
        Assert.Equal("south", info!.NextNationId);
        Assert.False(info.Blind);
    }

    [Fact]
    public void Reads_the_blind_toggle_from_the_scenario()
    {
        var southActive = ActiveAs(TwoHumanState(), "south");

        var info = HotseatHandoffDetector.Detect(
            southActive, TwoHumanScenario(blind: true), previousActiveNationId: "north", previousActiveWasHuman: true);

        Assert.NotNull(info);
        Assert.True(info!.Blind);
    }

    [Fact]
    public void No_handoff_when_the_active_seat_has_not_changed()
    {
        var state = ActiveAs(TwoHumanState(), "north");

        var info = HotseatHandoffDetector.Detect(
            state, TwoHumanScenario(blind: false), previousActiveNationId: "north", previousActiveWasHuman: true);

        Assert.Null(info);
    }

    [Fact]
    public void No_handoff_when_the_previous_seat_was_not_human()
    {
        var state = ActiveAs(TwoHumanState(), "south");

        var info = HotseatHandoffDetector.Detect(
            state, TwoHumanScenario(blind: false), previousActiveNationId: "north", previousActiveWasHuman: false);

        Assert.Null(info);
    }

    [Fact]
    public void No_handoff_when_the_new_active_seat_is_ai()
    {
        // south is AI in the shipped toy scenario -- TwoHumanScenario is deliberately not used here.
        var state = ActiveAs(CoreTestbed.InitialState(), "south");

        var info = HotseatHandoffDetector.Detect(
            state, CoreTestbed.Toy.Scenario, previousActiveNationId: "north", previousActiveWasHuman: true);

        Assert.Null(info);
    }

    [Fact]
    public void No_handoff_before_any_command_has_run()
    {
        var state = ActiveAs(TwoHumanState(), "south");

        var info = HotseatHandoffDetector.Detect(
            state, TwoHumanScenario(blind: false), previousActiveNationId: null, previousActiveWasHuman: false);

        Assert.Null(info);
    }

    /// <summary><see cref="GameState.ActiveNationId"/> is computed from <see cref="GameState.TurnOrder"/>
    /// and <see cref="GameState.ActiveSeatIndex"/>, not itself settable — this sets the index so
    /// <c>ActiveNationId</c> reads as <paramref name="nationId"/>.</summary>
    private static GameState ActiveAs(GameState state, string nationId) =>
        state with { ActiveSeatIndex = state.TurnOrder.IndexOfId(id => id, nationId) };

    /// <summary>The shipped toy scenario's own state, with its AI seat ("south") switched to human — the
    /// same "copy of a shipped scenario with two human seats" <c>docs/tasks/T25.md</c> asks the headless
    /// check to build.</summary>
    private static GameState TwoHumanState()
    {
        var initial = CoreTestbed.InitialState();
        return initial with
        {
            Nations = ValueList.From(initial.Nations.Select(n =>
                n.Id == "south" ? n with { Control = SeatControl.Human } : n)),
        };
    }

    private static Scenario TwoHumanScenario(bool blind) =>
        CoreTestbed.Toy.Scenario with
        {
            BlindHotseat = blind,
            Seats = ValueList.From(CoreTestbed.Toy.Scenario.Seats.Select(s =>
                s.Nation == "south" ? s with { Control = SeatControl.Human } : s)),
        };
}
