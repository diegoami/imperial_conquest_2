using IC2.Engine.Core;

namespace IC2.Engine.Tests.News;

/// <summary>Test domain events used by news tests. Marked as news-worthy for catalog coverage tests.</summary>

/// <summary>Test event for city capture.</summary>
[DomainEvent("city.falls-to", NewsWorthy = true)]
public sealed record CityFallsToEvent(string CityName, string OldOwner, string NewOwner) : DomainEvent;

/// <summary>Test event for failed capture.</summary>
[DomainEvent("city.fails-to-capture", NewsWorthy = true)]
public sealed record CityFailsToCaptureEvent(string AttackerNation, string CityName, string DefenderNation) : DomainEvent;

/// <summary>Test event for city defection.</summary>
[DomainEvent("city.defects-to", NewsWorthy = true)]
public sealed record CityDefectsToEvent(string CityName, string OldOwner, string NewOwner) : DomainEvent;

/// <summary>Test event for nation conquest.</summary>
[DomainEvent("nation.conquered", NewsWorthy = true)]
public sealed record NationConqueredEvent(string ConqueringNation, string ConqueredNation) : DomainEvent;

/// <summary>Test event for army destroyed.</summary>
[DomainEvent("battle.army-destroyed", NewsWorthy = true)]
public sealed record BattleArmyDestroyedEvent(string WinnerNation, string LoserNation) : DomainEvent;

/// <summary>Test event for fleet sunk.</summary>
[DomainEvent("battle.fleet-sunk", NewsWorthy = true)]
public sealed record BattleFleetSunkEvent(string WinnerNation, string LoserNation) : DomainEvent;

/// <summary>Test event for honourable peace.</summary>
[DomainEvent("peace.honourable", NewsWorthy = true)]
public sealed record PeaceHonourableEvent(string WinnerNation, string LoserNation) : DomainEvent;

/// <summary>Test event for fleet finished.</summary>
[DomainEvent("fleet.finished", NewsWorthy = true)]
public sealed record FleetFinishedEvent(string Nation, string CityName) : DomainEvent;

/// <summary>Test event for fleet lost at sea.</summary>
[DomainEvent("fleet.lost-at-sea", NewsWorthy = true)]
public sealed record FleetLostAtSeaEvent(string Nation) : DomainEvent;

/// <summary>Test event for victory (all cities).</summary>
[DomainEvent("victory.all-cities", NewsWorthy = true)]
public sealed record VictoryAllCitiesEvent : DomainEvent;

/// <summary>Test event for nation conquered by enemy.</summary>
[DomainEvent("victory.conquered-by-nation", NewsWorthy = true)]
public sealed record VictoryConqueredByNationEvent(string ConqueringNation) : DomainEvent;

/// <summary>Test event that is NOT news-worthy.</summary>
[DomainEvent("test.non-newsworthy", NewsWorthy = false)]
public sealed record NonNewsworthyTestEvent(string Data) : DomainEvent;
