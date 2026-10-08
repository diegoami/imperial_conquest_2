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

/// <summary>
/// T149: a fleet's accepted move order placed it on a new tile. The starting tile is the fleet's
/// pre-order position; the ending tile is <see cref="Movement.MovementWalkResult.FinalPosition"/> — what
/// the walker actually reached, which can be a stop short of the order's own target. Published by
/// <see cref="Commands.MoveFleetCommandHandler"/> once an order is accepted, with no event raised on a
/// rejection. Not news-worthy: the original's `MakeSound(2)` fires on every step the walker takes, and
/// the sound's <em>call</em> is the only place this event's information would reach the player.
/// <c>docs/build-process.md</c> §2.5 leaves the news literal to whichever task reviews the catalog
/// entry, and no catalog entry for this exists today.
/// </summary>
/// <param name="FleetId">The moving fleet's id.</param>
/// <param name="NationId">The fleet's owning nation.</param>
/// <param name="FromX">The tile the fleet stood on before the order.</param>
/// <param name="FromY">See <paramref name="FromX"/>.</param>
/// <param name="ToX">The tile the walker actually ended on.</param>
/// <param name="ToY">See <paramref name="ToX"/>.</param>
[DomainEvent("naval.fleet-moved")]
public sealed record FleetMoved(
    string FleetId,
    string NationId,
    int FromX,
    int FromY,
    int ToX,
    int ToY) : DomainEvent;

/// <summary>
/// T149: an accepted scuttle order removed the fleet from the map. Not news-worthy: the original
/// fires `MakeSound(8)` on the same path and writes no line, so the sound is the only thing the
/// player perceives — a duplicate news entry would be a different line for the same event.
/// </summary>
/// <param name="FleetId">The scuttled fleet's id.</param>
/// <param name="NationId">The fleet's owning nation.</param>
[DomainEvent("naval.fleet-scuttled")]
public sealed record FleetScuttled(string FleetId, string NationId) : DomainEvent;
