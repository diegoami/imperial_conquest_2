using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IC2.Engine.Model;

/// <summary>
/// An immutable, order-preserving sequence with <em>value</em> equality.
/// </summary>
/// <remarks>
/// <para>
/// Every collection in the domain model is a <see cref="ValueList{T}"/> rather than an array,
/// <c>List&lt;T&gt;</c> or <c>ImmutableArray&lt;T&gt;</c>, for two reasons the task's Definition of
/// Done depends on:
/// </para>
/// <list type="number">
/// <item><description>
/// C# records synthesise <c>Equals</c> from their members, and every one of those alternatives
/// compares by reference. Without sequence equality here, "constructs a non-trivial state,
/// serializes it, deserializes it, and asserts deep equality" could never pass.
/// </description></item>
/// <item><description>
/// Determinism: a list has one stable enumeration order, whereas a dictionary does not. The engine
/// rules (docs/build-orchestration-plan.md §6.2 gate 3) forbid order-dependent dictionary
/// enumeration in gameplay paths, so the model simply never stores a dictionary.
/// </description></item>
/// </list>
/// </remarks>
[JsonConverter(typeof(ValueListConverterFactory))]
public sealed class ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    private readonly T[] _items;

    /// <summary>The empty list. Safe to share: the type is immutable.</summary>
    public static ValueList<T> Empty { get; } = new(Array.Empty<T>());

    private ValueList(T[] items) => _items = items;

    /// <summary>Copies <paramref name="items"/> into a new value list.</summary>
    public ValueList(IEnumerable<T> items) => _items = items.ToArray();

    /// <summary>Creates a value list from an explicit sequence of elements.</summary>
    public static ValueList<T> Of(params T[] items) => new((T[])items.Clone());

    public T this[int index] => _items[index];

    public int Count => _items.Length;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    public bool Equals(ValueList<T>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (_items.Length != other._items.Length)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < _items.Length; i++)
        {
            if (!comparer.Equals(_items[i], other._items[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as ValueList<T>);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_items.Length);
        foreach (var item in _items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => $"[{_items.Length} item(s)]";
}

/// <summary>Convenience factories for <see cref="ValueList{T}"/>.</summary>
public static class ValueList
{
    /// <summary>Creates a value list, inferring the element type.</summary>
    public static ValueList<T> Of<T>(params T[] items) => ValueList<T>.Of(items);

    /// <summary>Copies a sequence into a value list.</summary>
    public static ValueList<T> From<T>(IEnumerable<T> items) => new(items);
}

/// <summary>Serializes <see cref="ValueList{T}"/> as a plain JSON array.</summary>
internal sealed class ValueListConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(ValueList<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var elementType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(ValueListConverter<>).MakeGenericType(elementType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class ValueListConverter<T> : JsonConverter<ValueList<T>>
    {
        public override ValueList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var items = JsonSerializer.Deserialize<T[]>(ref reader, options)
                        ?? throw new JsonException("A value list may not be null.");
            return ValueList<T>.Of(items);
        }

        public override void Write(Utf8JsonWriter writer, ValueList<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var item in value)
            {
                JsonSerializer.Serialize(writer, item, options);
            }

            writer.WriteEndArray();
        }
    }
}

/// <summary>Lookup helpers shared by the model's id-keyed lists.</summary>
public static class ValueListLookup
{
    /// <summary>
    /// Finds the single element whose <paramref name="idSelector"/> matches <paramref name="id"/>
    /// using ordinal comparison, or <see langword="null"/> when no element matches.
    /// </summary>
    /// <remarks>
    /// A linear scan over an ordered list, deliberately: it is deterministic, and the model never
    /// enumerates a hash-ordered container in a gameplay path.
    /// </remarks>
    public static T? FindById<T>(this ValueList<T> list, Func<T, string> idSelector, string id)
        where T : class
    {
        foreach (var item in list)
        {
            if (string.Equals(idSelector(item), id, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>Returns the index of the first element whose id matches, or <c>-1</c>.</summary>
    public static int IndexOfId<T>(this ValueList<T> list, Func<T, string> idSelector, string id)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (string.Equals(idSelector(list[i]), id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Returns <see langword="true"/> when every id in the list is distinct.</summary>
    public static bool HasDistinctIds<T>(this ValueList<T> list, Func<T, string> idSelector)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in list)
        {
            if (!seen.Add(idSelector(item)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns the first duplicated id in the list, or <see langword="null"/>.</summary>
    [return: MaybeNull]
    public static string? FirstDuplicateId<T>(this ValueList<T> list, Func<T, string> idSelector)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in list)
        {
            var id = idSelector(item);
            if (!seen.Add(id))
            {
                return id;
            }
        }

        return null;
    }
}
