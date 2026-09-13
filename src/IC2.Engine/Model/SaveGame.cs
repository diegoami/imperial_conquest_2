using System.Text.Json.Serialization;

namespace IC2.Engine.Model;

/// <summary>
/// A live, versioned snapshot of a <see cref="Scenario"/> in progress, including which world and
/// ruleset it started from — the fourth file kind in
/// <c>docs/game-design.md</c> §"The core data model".
/// </summary>
/// <remarks>
/// Deliberately carries no timestamp: the engine rules forbid wall-clock reads on any path that ends
/// up in state, so a save is named by its <see cref="Label"/> and ordered by its state's
/// <see cref="CalendarState.TurnIndex"/>, never by <c>DateTime.Now</c>.
/// </remarks>
public sealed record SaveGame(
    int SchemaVersion,
    string Id,
    string Label,
    string ScenarioId,
    string WorldId,
    string RulesetId,
    GameState State,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null) : IVersionedDocument;
