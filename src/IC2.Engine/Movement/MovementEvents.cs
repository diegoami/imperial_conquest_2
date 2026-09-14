using IC2.Engine.Core;

namespace IC2.Engine.Movement;

/// <summary>
/// Published when <see cref="MovementWalker.Walk"/> prices a cell using
/// <see cref="Model.TerrainRules.DefaultMoveCost"/> because the ruleset's move-cost table carries no
/// entry for that cell's tile type.
/// </summary>
/// <remarks>
/// This is the observable half of Done When 5's "arbitrary custom maps" guarantee
/// (<c>docs/game-design.md</c> §Movement): a custom world may define a tile type the loaded ruleset has
/// never heard of, the walk still prices it (at the ruleset's default) rather than failing, and this
/// event is how a caller — a UI warning banner, a modding lint tool, a test — finds out that happened.
/// Not news-worthy: it is a diagnostic for whoever is building the map or ruleset, not a headline for
/// the in-game news log.
/// </remarks>
/// <param name="TileTypeId">The unpriced tile type's id.</param>
/// <param name="X">The cell's X coordinate.</param>
/// <param name="Y">The cell's Y coordinate.</param>
/// <param name="FallbackMoveCost">The default cost that was charged instead.</param>
[DomainEvent("movement.unpriced-terrain", NewsWorthy = false)]
public sealed record UnpricedTerrainEncountered(string TileTypeId, int X, int Y, int FallbackMoveCost) : DomainEvent;
