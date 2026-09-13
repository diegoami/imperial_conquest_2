using System.Text.Json;

namespace IC2.Engine.Tests.Fixtures;

/// <summary>
/// The tag a fixture entry carries, matching the source report's own tagging convention
/// (<c>docs/game-design.md</c> / <c>docs/design-audit.md</c>): direct RE evidence, an
/// extrapolation from it, or a new design decision with no RE evidence.
/// </summary>
public enum FixtureTag
{
    Confirmed,
    Derived,
    Designed,
}

/// <summary>
/// One transcribed entry in the fixtures corpus (<c>tests/fixtures/corpus.json</c>). Every field
/// is a pure transcription from a research-repo report — nothing here is computed, rounded,
/// converted, or reconciled between disagreeing reports (docs/build-orchestration-plan.md,
/// "T04 Fixtures corpus").
/// </summary>
/// <remarks>
/// <see cref="Tag"/> is kept as the raw JSON string (rather than parsed straight into
/// <see cref="FixtureTag"/>) so that a structurally-present-but-empty tag surfaces as a clean
/// assertion failure in <c>FixturesCorpusTests</c> (Done-when line 1) instead of an exception
/// thrown deep inside JSON deserialization. Use <see cref="ParsedTag"/> once that invariant is
/// established.
/// </remarks>
/// <param name="Id">Stable dotted identifier, e.g. <c>"tax.nationTaxBaseRome"</c> (see corpus.json).</param>
/// <param name="Value">The transcribed value: a number, string, or boolean.</param>
/// <param name="Source">The report filename this value was read from (checked against known-reports.json).</param>
/// <param name="Tag">What the source report itself calls this value's evidentiary status: "confirmed" / "derived" / "designed".</param>
/// <param name="Note">Optional short quote/context so a reviewer can check the reading without reopening the report.</param>
public sealed record FixtureEntry(string Id, JsonElement Value, string Source, string Tag, string? Note)
{
    /// <summary>The value as a <see cref="double"/>. Throws if the value is not a JSON number.</summary>
    public double AsNumber() => Value.GetDouble();

    /// <summary>The value as an <see cref="int"/>. Throws if the value is not a JSON number.</summary>
    public int AsInt() => Value.GetInt32();

    /// <summary>The value as a <see cref="string"/>. Throws if the value is not a JSON string.</summary>
    public string AsString() => Value.GetString()
        ?? throw new InvalidOperationException($"Fixture '{Id}' has a JSON null string value.");

    /// <summary>The value as a <see cref="bool"/>. Throws if the value is not a JSON boolean.</summary>
    public bool AsBool() => Value.GetBoolean();

    /// <summary><see cref="Tag"/> parsed into <see cref="FixtureTag"/>, case-insensitively.</summary>
    public FixtureTag ParsedTag() => Enum.Parse<FixtureTag>(Tag, ignoreCase: true);
}
