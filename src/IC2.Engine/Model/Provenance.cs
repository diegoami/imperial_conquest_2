using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IC2.Engine.Model;

/// <summary>One <c>"_provenance"</c> entry: the field it annotates, and where that value came from.</summary>
/// <param name="Field">
/// The JSON name of the sibling field this entry explains, or a dotted path into a nested object
/// when one entry covers a group.
/// </param>
/// <param name="Source">
/// Free text, by convention starting with the evidence tag — <c>confirmed:</c>, <c>derived:</c>,
/// <c>designed:</c> or <c>open:</c> — followed by the report filename under the research repo's
/// <c>docs/reports/</c> or the design document section it comes from. A <c>designed:</c> entry must
/// additionally say what was searched and came up empty (docs/design-audit.md §4.5, promoted to a
/// merge gate by docs/build-orchestration-plan.md §6.2 gate 2).
/// </param>
public sealed record ProvenanceEntry(string Field, string Source);

/// <summary>
/// The <c>"_provenance"</c> object that <c>docs/game-design.md</c> §"Ruleset format" specifies:
/// a per-field note recording where each number came from, carried alongside the fields it explains.
/// </summary>
/// <remarks>
/// Order is preserved exactly as written so that serialize → deserialize → serialize is byte-identical,
/// and equality is by ordered content so a record carrying provenance still has value equality.
/// </remarks>
[JsonConverter(typeof(ProvenanceMapConverter))]
public sealed class ProvenanceMap : IReadOnlyList<ProvenanceEntry>, IEquatable<ProvenanceMap>
{
    private readonly ProvenanceEntry[] _entries;

    /// <summary>An empty provenance block.</summary>
    public static ProvenanceMap Empty { get; } = new(Array.Empty<ProvenanceEntry>());

    private ProvenanceMap(ProvenanceEntry[] entries) => _entries = entries;

    /// <summary>Creates a provenance block from an ordered sequence of entries.</summary>
    public ProvenanceMap(IEnumerable<ProvenanceEntry> entries) => _entries = entries.ToArray();

    /// <summary>Creates a provenance block from <c>(field, source)</c> pairs, in order.</summary>
    public static ProvenanceMap Of(params (string Field, string Source)[] entries) =>
        new(entries.Select(e => new ProvenanceEntry(e.Field, e.Source)).ToArray());

    public ProvenanceEntry this[int index] => _entries[index];

    public int Count => _entries.Length;

    /// <summary>Returns the recorded source for <paramref name="field"/>, or <see langword="null"/>.</summary>
    public string? SourceFor(string field)
    {
        foreach (var entry in _entries)
        {
            if (string.Equals(entry.Field, field, StringComparison.Ordinal))
            {
                return entry.Source;
            }
        }

        return null;
    }

    public IEnumerator<ProvenanceEntry> GetEnumerator() => ((IEnumerable<ProvenanceEntry>)_entries).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _entries.GetEnumerator();

    public bool Equals(ProvenanceMap? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (_entries.Length != other._entries.Length)
        {
            return false;
        }

        for (var i = 0; i < _entries.Length; i++)
        {
            if (!_entries[i].Equals(other._entries[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as ProvenanceMap);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_entries.Length);
        foreach (var entry in _entries)
        {
            hash.Add(entry);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => $"_provenance[{_entries.Length}]";
}

/// <summary>Reads and writes <see cref="ProvenanceMap"/> as a flat JSON object of string values.</summary>
internal sealed class ProvenanceMapConverter : JsonConverter<ProvenanceMap>
{
    public override ProvenanceMap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("\"_provenance\" must be a JSON object of field name to source string.");
        }

        var entries = new List<ProvenanceEntry>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new ProvenanceMap(entries);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Unexpected token inside \"_provenance\".");
            }

            var field = reader.GetString()!;
            reader.Read();
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"\"_provenance\" entry '{field}' must be a string.");
            }

            entries.Add(new ProvenanceEntry(field, reader.GetString()!));
        }

        throw new JsonException("Unterminated \"_provenance\" object.");
    }

    public override void Write(Utf8JsonWriter writer, ProvenanceMap value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var entry in value)
        {
            writer.WriteString(entry.Field, entry.Source);
        }

        writer.WriteEndObject();
    }
}
