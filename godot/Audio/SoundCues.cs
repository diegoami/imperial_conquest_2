using IC2.Engine.Assets;
using IC2.Engine.Battle;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Model;
using IC2.Engine.Naval;

namespace IC2.Slice.Audio;

/// <summary>
/// T149: a session's domain events -> the sounds to play, in order. Godot-free so a plain xunit test
/// (<c>tests/IC2.Engine.Tests/Ui/SoundCuesTests.cs</c>) can pin the mapping without the Godot SDK.
/// Maps every <see cref="DomainEvent"/> the original plays a sound for (the report's 16 call sites,
/// 10 sounds, 7 new keys) into one or more asset keys, in the order the events themselves were
/// published.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Human-only steps, every seat's other events.</strong> The report's rows 14 and 15 gate the
/// original's <c>MakeSound(1)</c> and <c>MakeSound(2)</c> on "the current seat is human" (its
/// <c>DAT_004A0320</c>'s <c>+0x490</c>). The clone's <see cref="ArmyMoved"/> and <see cref="FleetMoved"/>
/// events therefore only raise their step cues when the seat that owns the unit or fleet is
/// <see cref="SeatControl.Human"/> at the time the cue list reads <see cref="GameState.Nations"/> —
/// a snapshot the session has already advanced past by the time <see cref="GameSession.EventsPublished"/>
/// fires. Sounds 3 to 10 (sounds 6, 7, 8, 9 and 10 in particular) play for every seat, computer or
/// not, exactly as the original's <c>PlaySoundA</c> would: the "occurring elsewhere" wording the
/// decompressed help carries. The cue list therefore reads <see cref="NationState.Control"/> for the
/// owning nation, not for the active seat.
/// </para>
/// <para>
/// <strong>Step count.</strong> The original plays the step sound once per tile the walker crosses;
/// <see cref="ArmyMoved"/> and <see cref="FleetMoved"/> carry endpoints, not the walked path. The
/// Chebyshev distance is the step count of a straight move and a lower bound on a winding one (Sol's
/// review of PR 793, R3). The cue list therefore repeats the step cue
/// <c>min(max(|ToX - FromX|, |ToY - FromY|), 12)</c> times — at most 12, the cap the
/// <see cref="SoundPlayer"/> also enforces.
/// </para>
/// <para>
/// <strong>Order.</strong> Events in one call give their cues in event order, the same order
/// <see cref="GameSession.EventsPublished"/> delivers them. A step cue that produces N entries from
/// one event keeps those N entries together, ahead of whatever the next event yields, so the
/// player's per-army walk is heard as a single clip and the next event's clip starts cleanly.
/// </para>
/// </remarks>
public static class SoundCues
{
    /// <summary>
    /// Every <see cref="string"/> sound asset key one <see cref="DomainEvent"/> in
    /// <paramref name="events"/> maps to, in the order the events were published. An event outside
    /// the list contributes nothing; a step event from a computer-owned army or fleet contributes
    /// nothing. Empty when <paramref name="events"/> is empty, the common case for read-only
    /// commands.
    /// </summary>
    /// <param name="events">The events one <see cref="GameSession.Submit"/> call published.</param>
    /// <param name="state">
    /// The session's own state immediately after the call (the cue list reads
    /// <see cref="NationState.Control"/> from here, never re-derives a seat's control from
    /// <see cref="GameState.ActiveNationId"/>).
    /// </param>
    public static IReadOnlyList<string> ForEvents(IReadOnlyList<DomainEvent> events, GameState state)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(state);

        if (events.Count == 0)
        {
            return Array.Empty<string>();
        }

        var cues = new List<string>(events.Count);
        foreach (var domainEvent in events)
        {
            AppendCuesFor(domainEvent, state, cues);
        }

        return cues.Count == 0 ? Array.Empty<string>() : cues;
    }

    private static void AppendCuesFor(DomainEvent domainEvent, GameState state, List<string> cues)
    {
        switch (domainEvent)
        {
            case ArmyMoved armyMoved:
                AppendStepCuesIfHuman(state, armyMoved.NationId, AssetKeys.SfxUnitMove,
                    armyMoved.FromX, armyMoved.FromY, armyMoved.ToX, armyMoved.ToY, cues);
                break;
            case FleetMoved fleetMoved:
                AppendStepCuesIfHuman(state, fleetMoved.NationId, AssetKeys.SfxFleetMove,
                    fleetMoved.FromX, fleetMoved.FromY, fleetMoved.ToX, fleetMoved.ToY, cues);
                break;
            case CityFallsToNation:
                cues.Add(AssetKeys.SfxCityCaptured);
                break;
            case CityFailsToBeCaptured:
                cues.Add(AssetKeys.SfxSiegeFailed);
                break;
            case BattleFleetSunk:
            case FleetLostAtSea:
            case FleetScuttled:
                cues.Add(AssetKeys.SfxFleetSunk);
                break;
            case BattleResolved resolved when resolved.Result.Kind == BattleKind.Field:
                cues.Add(AssetKeys.SfxBattle);
                break;
            case NationConquered:
                cues.Add(AssetKeys.SfxNationConquered);
                break;
        }
    }

    /// <summary>
    /// Appends the step cue up to <see cref="MaxStepCues"/> times, but only when the seat that owns
    /// the moving unit or fleet is <see cref="SeatControl.Human"/> in <paramref name="state"/>. A
    /// computer-owned move is silent, as in the original (the report's rows 14 and 15 gate on the
    /// "current seat is human" check).
    /// </summary>
    private static void AppendStepCuesIfHuman(
        GameState state, string nationId, string cueKey,
        int fromX, int fromY, int toX, int toY, List<string> cues)
    {
        var nation = state.NationById(nationId);
        if (nation is null || nation.Control != SeatControl.Human)
        {
            return;
        }

        var steps = StepCount(fromX, fromY, toX, toY);
        for (var i = 0; i < steps; i++)
        {
            cues.Add(cueKey);
        }
    }

    /// <summary>
    /// The Chebyshev distance of one move, capped at <see cref="MaxStepCues"/>. A no-move
    /// (From == To) yields zero, so a fleet whose walk stops at its own tile never raises a step
    /// cue. <c>[designed]</c>: the original plays the step sound once per tile the walker crosses
    /// (rows 14 and 15 fire after every step the walker makes); <see cref="ArmyMoved"/> and
    /// <see cref="FleetMoved"/> carry endpoints, so the count is the Chebyshev distance — the step
    /// count of a straight move and a lower bound on a winding one. The cap matches the
    /// <see cref="SoundPlayer"/>'s own per-call cap.
    /// </summary>
    public static int StepCount(int fromX, int fromY, int toX, int toY)
    {
        var deltaX = Math.Abs(toX - fromX);
        var deltaY = Math.Abs(toY - fromY);
        var chebyshev = Math.Max(deltaX, deltaY);
        return Math.Min(chebyshev, MaxStepCues);
    }

    /// <summary>
    /// The cap on step cues one <see cref="ArmyMoved"/> or <see cref="FleetMoved"/> event can
    /// raise, set the same way the <see cref="SoundPlayer"/>'s own per-call cap is. A longer
    /// <c>end</c>'s own step would still be heard as a clipped walk rather than a long delay
    /// between sounds: the cue list's cap and the player's cap agree by construction.
    /// </summary>
    public const int MaxStepCues = 12;
}
