using IC2.Engine.Core;

namespace IC2.Engine.Naval;

/// <summary>
/// A fleet's construction countdown reached zero and it launched onto the map —
/// <c>docs/task-catalogue.md</c> "T14 Naval" Done-when 2. Matches the news catalog's already-registered
/// <c>fleet.finished</c> template (<c>"&lt;nation&gt; finishes a new fleet at &lt;cityName[fleet[+20]]&gt;."</c>,
/// added by T10/T42): <see cref="Nation"/> and <see cref="CityName"/> resolve the template's two
/// placeholders, the bracket annotation on the second being provenance text the renderer ignores.
/// </summary>
/// <param name="Nation">The fleet's owning nation.</param>
/// <param name="CityName">The city the fleet was built at.</param>
[DomainEvent("fleet.finished", NewsWorthy = true)]
public sealed record FleetFinished(string Nation, string CityName) : DomainEvent;

/// <summary>
/// A launched fleet's condition fell below <see cref="Model.NavalRules.DeathConditionThreshold"/> during
/// the storm pass and it was destroyed — <c>docs/task-catalogue.md</c> "T14 Naval" Done-when 13. Matches
/// the news catalog's already-registered <c>fleet.lost-at-sea</c> template (corrected to end with a
/// period by T42, bug #88), confirmed verbatim in <c>1_cartago_271_summer_9.sav</c>'s own news log.
/// </summary>
/// <param name="Nation">The fleet's owning nation.</param>
[DomainEvent("fleet.lost-at-sea", NewsWorthy = true)]
public sealed record FleetLostAtSea(string Nation) : DomainEvent;

/// <summary>
/// A launched fleet took the storm pass's heavy-damage branch and survived —
/// <c>docs/task-catalogue.md</c> "T14 Naval" Scope. Matches the news catalog's already-registered
/// <c>fleet.damaged-in-storm</c> template, added by T42 (<c>news-log-format-and-messages.md</c> Q4 #20).
/// </summary>
/// <param name="Nation">The fleet's owning nation.</param>
[DomainEvent("fleet.damaged-in-storm", NewsWorthy = true)]
public sealed record FleetDamagedInStorm(string Nation) : DomainEvent;
