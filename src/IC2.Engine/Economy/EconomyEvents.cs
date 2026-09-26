using IC2.Engine.Core;

namespace IC2.Engine.Economy;

/// <summary>
/// This task's narration — <c>docs/build-process.md</c> §2.5: a system never touches the news log directly,
/// it publishes an event, and T10's writer decides what (if anything) becomes a news-log line.
/// </summary>
/// <remarks>
/// None of these are marked news-worthy here: T10 owns the catalog that maps an event kind to a rendered
/// message, and inventing a rendered string in this task would be inventing news-log content T10 has not
/// reviewed. A later pass (T10's own coverage test, or a small follow-up) can flip <c>NewsWorthy</c> once
/// a catalog entry exists for each of these.
/// </remarks>
[DomainEvent("economy.weather-event-fired")]
public sealed record WeatherEventFired(int SeasonIndex, int Week, string EffectId) : DomainEvent;

/// <summary>
/// An AI nation's leader is deposed for debt (<c>FUN_0044c8f0</c>) — <c>docs/task-catalogue.md</c>
/// "T39 Quarterly upkeep: who pays, mercenary desertion, and deposition for debt", Done-when 6 and 7.
/// This <em>is</em> marked news-worthy: <see cref="IC2.Engine.News.NewsMessageCatalog"/> already carries the
/// template (<c>nation.leader-deposed</c>, <c>newsMessage.deposesLeader</c> in the fixtures corpus,
/// added by T42) — a human nation's equivalent opens a game-over dialog instead, which is not news, so
/// this event is published only on the AI path (<see cref="AiDepositionHandler"/>).
/// </summary>
[DomainEvent("nation.leader-deposed", NewsWorthy = true)]
public sealed record AiLeaderDeposed(string Nation, string LeaderName) : DomainEvent;
