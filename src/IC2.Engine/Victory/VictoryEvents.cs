using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Victory;

/// <summary>
/// Published once, by <see cref="VictoryCheckSystem"/>, when a running game's victory condition
/// resolves to a winner.
/// </summary>
/// <remarks>
/// <strong>Not news-worthy.</strong> The only two corpus literals that name a victory outcome —
/// <c>tests/fixtures/corpus.json</c>'s <c>gameOverForm.victoryAllCities</c> ("You have conquerred the
/// Mediterranean, a unique achievement.") and <c>gameOverForm.conqueredByNation</c> — are both tagged
/// under the <c>gameOverForm.*</c> prefix specifically because T42 reclassified them <em>out of</em>
/// <c>newsMessage.*</c>: they are <c>THumanFalls_InitializeForm</c> game-over-screen labels that "never
/// reaches <c>FUN_00449240</c> and so never appears in the news log" (corpus entry note). No corpus
/// literal describes a news-log line for a win, so this event is not news-worthy — a later task with
/// real evidence for a news-log victory announcement is the place to flip that, not a guess made here.
/// </remarks>
[DomainEvent("victory.game-won", NewsWorthy = false)]
public sealed record GameWon(VictoryConditionType ConditionType, string WinningNationId) : DomainEvent;

/// <summary>
/// Published once, by <see cref="VictoryCheckSystem"/>, when a running game's hard limit is reached
/// with nobody having won — <c>docs/game-design.md</c> §"Victory conditions", the 250 BC comparison in
/// <c>THumanFalls_InitializeForm</c> for <see cref="VictoryConditionType.TotalConquest"/>, or a
/// scenario's own <c>turnLimit</c> for <see cref="VictoryConditionType.ScoreAtTurnLimit"/> with a tied
/// top score.
/// </summary>
/// <remarks>Not news-worthy, for the same reason as <see cref="GameWon"/> — see its remarks.</remarks>
[DomainEvent("victory.game-expired", NewsWorthy = false)]
public sealed record GameExpired(VictoryConditionType ConditionType) : DomainEvent;
