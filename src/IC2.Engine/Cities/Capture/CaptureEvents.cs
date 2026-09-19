using IC2.Engine.Core;

namespace IC2.Engine.Cities.Capture;

/// <summary>
/// A siege succeeded and the city changed hands by force — <c>FUN_0044B27C</c>'s
/// <c>"{CityName}   ({OldOwner})  falls to {NewOwner}."</c>, already registered in the news catalog as
/// <c>city.falls-to</c> (T42). Published once, by <see cref="CityCaptureResolver.Capture"/>, whether or
/// not the elimination it may trigger also publishes <see cref="NationConquered"/>.
/// </summary>
/// <param name="CityName">The captured city's display name.</param>
/// <param name="OldOwner">The losing nation's display name.</param>
/// <param name="NewOwner">The new owner's display name.</param>
[DomainEvent("city.falls-to", NewsWorthy = true)]
public sealed record CityFallsToNation(string CityName, string OldOwner, string NewOwner) : DomainEvent;

/// <summary>
/// A siege attempt failed to take the city — <c>FUN_0044B27C</c>'s
/// <c>"{AttackerNation} fails to capture {CityName}   ({DefenderNation})."</c>, already registered as
/// <c>city.fails-to-capture</c> (T42). Published only by <see cref="CityCaptureResolver.ResolveOutcome"/>;
/// nothing about the city changes.
/// </summary>
/// <param name="AttackerNation">The repulsed attacker's display name.</param>
/// <param name="CityName">The defended city's display name.</param>
/// <param name="DefenderNation">The defending nation's display name.</param>
[DomainEvent("city.fails-to-capture", NewsWorthy = true)]
public sealed record CityFailsToBeCaptured(string AttackerNation, string CityName, string DefenderNation) : DomainEvent;

/// <summary>
/// A city defected without a siege — <c>FUN_0044bed8</c>, reached only through
/// <c>FUN_0044ba1c</c>'s cascade after a nearby forced capture (<c>docs/task-catalogue.md</c> T17's
/// Known-open item; see <see cref="CityCaptureResolver"/>'s remarks for the <c>[derived]</c> inference).
/// Matches the already-registered <c>city.defects-to</c> template,
/// <c>"{CityName} defects from {OldOwner} to {NewOwner}."</c> (T42).
/// </summary>
/// <param name="CityName">The defecting city's display name.</param>
/// <param name="OldOwner">The losing nation's display name.</param>
/// <param name="NewOwner">The new owner's display name.</param>
[DomainEvent("city.defects-to", NewsWorthy = true)]
public sealed record CityDefectsToNation(string CityName, string OldOwner, string NewOwner) : DomainEvent;

/// <summary>
/// A nation lost its last city — DoD 4's elimination, published whenever
/// <see cref="NationElimination.ApplyIfLastCityLost"/> fires from either <see cref="CityCaptureResolver.Capture"/>
/// or <see cref="CityCaptureResolver.Defect"/>. Matches the already-registered <c>nation.conquered</c>
/// template, <c>"{ConqueringNation} conquers {ConqueredNation}."</c> (T42), which
/// <c>NewsMessageCatalog.IsWrappedInDashLines</c> always wraps between two dash-line entries.
/// </summary>
/// <param name="ConqueringNation">The nation that took the last city.</param>
/// <param name="ConqueredNation">The eliminated nation's display name.</param>
[DomainEvent("nation.conquered", NewsWorthy = true)]
public sealed record NationConquered(string ConqueringNation, string ConqueredNation) : DomainEvent;
