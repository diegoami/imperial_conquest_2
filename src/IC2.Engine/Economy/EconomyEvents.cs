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

[DomainEvent("economy.army-mutinied")]
public sealed record ArmyMutinied(string ArmyId, string NationId) : DomainEvent;

[DomainEvent("economy.rebellion-risk-detected")]
public sealed record RebellionRiskDetected(string CityId, int Loyalty) : DomainEvent;
