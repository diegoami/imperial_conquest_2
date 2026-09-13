using System.Text.Json.Serialization;

namespace IC2.Engine.Model;

/// <summary>
/// One <see cref="World"/> plus one <see cref="Ruleset"/> plus victory conditions and seat
/// assignments — the actual moddable, shareable unit
/// (<c>docs/game-design.md</c> §"The scenario system").
/// </summary>
public sealed record Scenario(
    int SchemaVersion,
    string Id,
    string Name,
    string WorldId,
    string RulesetId,
    ValueList<Seat> Seats,
    VictoryCondition Victory,
    int? TurnLimit,
    bool BlindHotseat,
    ulong RandomSeed,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null) : IVersionedDocument
{
    /// <summary>Finds the seat assigned to a nation id, or <see langword="null"/>.</summary>
    public Seat? SeatFor(string nationId) => Seats.FindById(s => s.Nation, nationId);
}

/// <summary>One nation's seat: who plays it, and with what personality when that is the AI.</summary>
public sealed record Seat(
    string Nation,
    SeatControl Control,
    AiPersonality? Personality = null);

/// <summary>Who controls a seat.</summary>
public enum SeatControl
{
    /// <summary>A local human player (single player, or one hotseat seat).</summary>
    Human,

    /// <summary>The heuristic AI.</summary>
    Ai,
}

/// <summary>
/// Per-nation AI tuning, each value in the 0–1 range
/// (<c>docs/game-design.md</c> §AI, <strong>[designed]</strong>).
/// </summary>
public sealed record AiPersonality(
    double Aggression,
    double ExpansionDrive,
    double LoyaltyToAlliances);

/// <summary>The scenario's win condition.</summary>
/// <param name="Type">Which shipped condition applies.</param>
/// <param name="Goal">
/// The scenario-authored goal text, required by <see cref="VictoryConditionType.Custom"/> and unused
/// otherwise.
/// </param>
public sealed record VictoryCondition(
    VictoryConditionType Type,
    string? Goal = null);
