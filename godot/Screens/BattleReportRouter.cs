using IC2.Engine.Battle;

namespace IC2.Slice.Screens;

/// <summary>
/// <c>docs/tasks/T116.md</c>'s rule, Godot-free: given one <c>GameSession.Submit</c> call's own
/// <see cref="BattleResult"/>s, which human seats the game still has, and the seat that is active once
/// that call returns, this decides which battle-result windows to show <em>now</em> and which to hold,
/// per seat, until that seat next becomes active.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The rule</strong> (<c>docs/tasks/T116.md</c>; the user's decision of 2026-10-02, applied per
/// seat). Each battle goes, in the order fought, to every <em>human</em> seat on either side
/// (<see cref="BattleResult.AttackerNationId"/>, <see cref="BattleResult.DefenderNationId"/>):
/// </para>
/// <list type="bullet">
/// <item><description>
/// If that seat is the active seat once the call returns, its window is shown now.
/// </description></item>
/// <item><description>
/// Otherwise it is held for that seat and shown, in the order fought, when that seat next becomes the
/// active seat — ahead of the battles of the call that made it active.
/// </description></item>
/// <item><description>
/// A battle with no human seat on either side is shown now, to the active seat (T25's unchanged
/// behaviour).
/// </description></item>
/// <item><description>
/// A held battle whose seat is no longer human (it fell, or was handed to the AI — T87) is dropped.
/// </description></item>
/// </list>
/// <para>
/// <strong>Why the seat list is passed in, not stored.</strong> "No longer human" is a property of the
/// live <c>GameState</c> (<see cref="IC2.Engine.Model.NationState.Control"/>), which elimination and
/// deposition both write; this router holds only the battles, and prunes them against whatever seat list
/// the caller passes on each <see cref="Route"/> call. It never reads a scenario's own static seats.
/// </para>
/// <para>
/// <strong>Godot-free</strong> (no <c>using Godot</c> anywhere in this file), so
/// <c>tests/IC2.Engine.Tests/Ui/Screens/BattleReportRouterTests.cs</c> can exercise the rule directly
/// against hand-built <see cref="BattleResult"/>s — the same seam <see cref="HotseatHandoffDetector"/>
/// and <see cref="BattleResultViewModel"/> already use, reachable from the xunit assembly through
/// <c>tests/IC2.Engine.Tests/IC2.Engine.Tests.csproj</c>'s own <c>Compile Include</c> line.
/// </para>
/// </remarks>
public sealed class BattleReportRouter
{
    /// <summary>
    /// The battles held for each seat, in the order fought, awaiting that seat's own turn start. Never a
    /// seat that is not currently human: <see cref="Route"/> drops a departed seat's list before it can
    /// grow again.
    /// </summary>
    private readonly Dictionary<string, List<BattleResult>> _held = new(StringComparer.Ordinal);

    /// <summary>
    /// Routes one <c>Submit</c> call's own battles and returns the windows to show now, in order.
    /// </summary>
    /// <param name="battles">
    /// The call's own <c>SessionOutput.Battles</c>/<c>GameSession.LastBattles</c>, in the order fought.
    /// </param>
    /// <param name="humanSeatIds">
    /// Every nation id still controlled by a human in the live state after the call — the caller's own
    /// <c>State.Nations.Where(n =&gt; n.Control == SeatControl.Human)</c>, so a seat deposited or
    /// eliminated mid-call is already gone here.
    /// </param>
    /// <param name="activeNationId">The nation that is active once the call returns.</param>
    /// <returns>
    /// The battle windows to show now, in order: first any battles previously held for the now-active
    /// seat (which is what makes a held report appear at that seat's own turn start), then this call's
    /// own battles in the order fought.
    /// </returns>
    public IReadOnlyList<BattleResult> Route(
        IReadOnlyList<BattleResult> battles,
        IReadOnlyCollection<string> humanSeatIds,
        string activeNationId)
    {
        ArgumentNullException.ThrowIfNull(battles);
        ArgumentNullException.ThrowIfNull(humanSeatIds);
        ArgumentNullException.ThrowIfNull(activeNationId);

        var humans = humanSeatIds as IReadOnlySet<string>
            ?? new HashSet<string>(humanSeatIds, StringComparer.Ordinal);

        // T116 Scope: a held battle whose seat is no longer human (it fell, or was handed to the AI,
        // T87) is dropped -- before release, so a departed seat's list can neither show nor regrow.
        foreach (var departed in _held.Keys.Where(seat => !humans.Contains(seat)).ToList())
        {
            _held.Remove(departed);
        }

        var showNow = new List<BattleResult>();

        // The now-active seat's held reports come first: it is their own turn start, and the call that
        // made it active must not jump ahead of them.
        if (_held.Remove(activeNationId, out var released))
        {
            showNow.AddRange(released);
        }

        foreach (var battle in battles)
        {
            var humanSides = HumanSidesOf(battle, humans);
            if (humanSides.Count == 0)
            {
                // No human seat on either side (AI versus AI): T25's behaviour, unchanged -- shown now.
                showNow.Add(battle);
                continue;
            }

            foreach (var seat in humanSides)
            {
                if (string.Equals(seat, activeNationId, StringComparison.Ordinal))
                {
                    showNow.Add(battle);
                }
                else
                {
                    HeldListFor(seat).Add(battle);
                }
            }
        }

        return showNow;
    }

    /// <summary>
    /// The battles currently held for <paramref name="nationId"/>, in the order fought — empty when none
    /// are. Exposed for the Godot-free test to pin what is held without having to release it through a
    /// second <see cref="Route"/> call.
    /// </summary>
    public IReadOnlyList<BattleResult> HeldFor(string nationId) =>
        _held.TryGetValue(nationId, out var list) ? list : Array.Empty<BattleResult>();

    /// <summary>
    /// The distinct human seats on either side of <paramref name="battle"/>, attacker first. A seat is
    /// named once even if the battle's two sides share it (a hand-built state could name one nation on
    /// both sides; never a real battle).
    /// </summary>
    private static List<string> HumanSidesOf(BattleResult battle, IReadOnlySet<string> humans)
    {
        var sides = new List<string>(2);
        if (humans.Contains(battle.AttackerNationId))
        {
            sides.Add(battle.AttackerNationId);
        }

        if (humans.Contains(battle.DefenderNationId)
            && !string.Equals(battle.DefenderNationId, battle.AttackerNationId, StringComparison.Ordinal))
        {
            sides.Add(battle.DefenderNationId);
        }

        return sides;
    }

    private List<BattleResult> HeldListFor(string seat)
    {
        if (!_held.TryGetValue(seat, out var list))
        {
            list = new List<BattleResult>();
            _held[seat] = list;
        }

        return list;
    }
}
