using IC2.Engine.Model;

namespace IC2.Slice.Screens;

/// <summary>
/// What <see cref="HotseatHandoffScreen"/> needs to show for one handoff — <c>docs/tasks/T25.md</c>: "a
/// blocking 'Pass to [Nation]' screen between human turns, with the optional blind-info-hiding mode."
/// </summary>
/// <param name="NextNationId">The nation whose turn is starting.</param>
/// <param name="NextNationName">Its display name.</param>
/// <param name="Blind">The scenario's own <see cref="Scenario.BlindHotseat"/> toggle.</param>
public sealed record HotseatHandoffInfo(string NextNationId, string NextNationName, bool Blind);

/// <summary>
/// Decides whether play just passed from one human seat to a <em>different</em> human seat — the
/// condition <c>docs/tasks/T25.md</c>'s own Done-when asks for ("ending a human seat's turn in a two-human
/// hotseat game shows the handoff"). Godot-free (no <c>using Godot</c> in this file) so
/// <c>tests/IC2.Engine.Tests/Ui/Screens/HotseatHandoffDetectorTests.cs</c> can exercise it directly against
/// constructed <see cref="GameState"/> fixtures, the same seam <see cref="DiplomacyGridViewModel"/> and
/// <see cref="BattleResultViewModel"/> use.
/// </summary>
/// <remarks>
/// <strong>Never a single-player false positive.</strong> A CLI <c>--seat</c> session, or any scenario
/// with only one human seat, never has a "different" human seat to pass to — <paramref name="wasHuman"/>
/// being <see langword="false"/> for every AI seat already excludes an AI-to-human transition (the very
/// first human turn of a game, or after watch mode never fires this), and the same-id check excludes a
/// human seat's own turn continuing (a mid-turn command never changes <see cref="GameState.ActiveNationId"/>
/// — only <c>end</c> rotates it, through <see cref="IC2.Engine.Core.TurnCoordinator.RunTurn"/>).
/// </remarks>
public static class HotseatHandoffDetector
{
    /// <summary>Checks the one transition described in this class's own remarks.</summary>
    /// <param name="state">The session's state <em>after</em> whatever command just ran.</param>
    /// <param name="scenario">The session's own scenario — the source of <see cref="HotseatHandoffInfo.Blind"/>.</param>
    /// <param name="previousActiveNationId">
    /// Which nation was active immediately before this command, as observed by the caller — <see langword="null"/>
    /// before any command has run yet (construction), which never yields a handoff.
    /// </param>
    /// <param name="previousActiveWasHuman">Whether that previously-active nation was <see cref="SeatControl.Human"/> at the time.</param>
    /// <returns>
    /// The handoff to show, or <see langword="null"/> when no handoff is due: the active seat did not
    /// change, the previous seat was not human (an AI turn ending mid-<c>end</c>-loop never hands off to
    /// anyone), or the new active seat is not human either (an AI seat is next; nothing to pause on).
    /// </returns>
    public static HotseatHandoffInfo? Detect(
        GameState state,
        Scenario scenario,
        string? previousActiveNationId,
        bool previousActiveWasHuman)
    {
        var newActiveId = state.ActiveNationId;
        if (!previousActiveWasHuman
            || previousActiveNationId is null
            || string.Equals(previousActiveNationId, newActiveId, StringComparison.Ordinal))
        {
            return null;
        }

        var newNation = state.NationById(newActiveId);
        if (newNation is null || newNation.Control != SeatControl.Human)
        {
            return null;
        }

        return new HotseatHandoffInfo(newActiveId, newNation.Name, scenario.BlindHotseat);
    }
}
