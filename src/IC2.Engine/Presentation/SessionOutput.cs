using IC2.Engine.Battle;

namespace IC2.Engine.Presentation;

/// <summary>What one submitted line produced: the text to print, and whether the session should stop.</summary>
/// <param name="Lines">
/// The rendered transcript for this one command, in order — the echoed input line, the command's own
/// output, and a trailing blank separator line.
/// </param>
/// <param name="ShouldExit">Whether the caller (a script loop or a stdin loop) should stop reading.</param>
/// <param name="Battles">
/// T25 (plan #474): every <see cref="BattleResult"/> a <see cref="BattleResolved"/> event published while
/// this one <see cref="GameSession.Submit"/> call ran — a human-issued attack/siege/naval attack
/// (<see cref="GameSession.IssueCommand"/>), or any battle an AI seat's own turn resolved while this call
/// played it (<c>end</c> can play several AI seats in one call). In submission order. Defaults to empty so
/// no existing caller of this record changes; nothing about what the engine computes or prints changes —
/// this only exposes results already computed, for T25's battle-result screen to read.
/// </param>
/// <param name="SeatFalls">
/// T138: every human seat that fell while this one <see cref="GameSession.Submit"/> call ran, in the
/// order it happened — the same seam <paramref name="Battles"/> established, one step further. A fall in
/// the construction-time prelude (before any <see cref="GameSession.Submit"/> call exists) arrives with
/// the first call's output, exactly as the prelude's own lines do. A nation falls at most once per call,
/// however many of the session's own paths report it. Defaults to empty so no existing caller of this
/// record changes.
/// </param>
/// <param name="GameOver">
/// T138: whether no human seat can still give orders once this call returned — the game is over
/// (<c>_gameOver</c>), or a <c>--seat</c>-style session's own seat is lost (<c>_seatLost</c>). Defaults
/// to <see langword="false"/> so no existing caller of this record changes.
/// </param>
public sealed record SessionOutput(
    IReadOnlyList<string> Lines,
    bool ShouldExit,
    IReadOnlyList<BattleResult>? Battles = null,
    IReadOnlyList<SeatFall>? SeatFalls = null,
    bool GameOver = false)
{
    /// <summary>See this record's own <c>Battles</c> parameter doc. Never <see langword="null"/>.</summary>
    public IReadOnlyList<BattleResult> Battles { get; init; } = Battles ?? Array.Empty<BattleResult>();

    /// <summary>See this record's own <c>SeatFalls</c> parameter doc. Never <see langword="null"/>.</summary>
    public IReadOnlyList<SeatFall> SeatFalls { get; init; } = SeatFalls ?? Array.Empty<SeatFall>();
}
