using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IC2.Engine.Model;

/// <summary>
/// One member of a model record's JSON contract.
/// </summary>
/// <param name="JsonName">The name the member appears under in JSON.</param>
/// <param name="ClrName">The record property's name.</param>
/// <param name="Type">The member's declared type.</param>
/// <param name="Required">
/// Whether a document must carry this member. True for every constructor parameter without a default
/// value — which is how the loader knows a missing field is an error rather than a silent default.
/// </param>
/// <param name="AllowsNull">
/// Whether JSON <c>null</c> is a legal value for this member, taken from the parameter's nullable
/// annotation. A required member may still be nullable (a city's capital, an unbuilt fleet's
/// countdown): the key has to be present, but its value may be <c>null</c>.
/// </param>
public sealed record JsonContractMember(string JsonName, string ClrName, Type Type, bool Required, bool AllowsNull);

/// <summary>
/// The JSON contract of a model record, derived from its primary constructor.
/// </summary>
/// <remarks>
/// The loader validates documents against this before deserializing, so that a missing required field
/// or an unrecognised field is a typed, path-carrying error instead of a default-constructed value.
/// It is derived by reflection rather than hand-written so the two can never drift apart.
/// </remarks>
public sealed class JsonContract
{
    private static readonly ConcurrentDictionary<Type, JsonContract?> Cache = new();

    private JsonContract(Type type, JsonContractMember[] members)
    {
        Type = type;
        Members = ValueList<JsonContractMember>.Of(members);
    }

    /// <summary>The record type this contract describes.</summary>
    public Type Type { get; }

    /// <summary>The contract's members, in declaration (and therefore serialization) order.</summary>
    public ValueList<JsonContractMember> Members { get; }

    /// <summary>
    /// Returns the contract for <paramref name="type"/>, or <see langword="null"/> when the type is not
    /// a model record (a primitive, string, enum, collection, or any type outside this model).
    /// </summary>
    public static JsonContract? For(Type type) => Cache.GetOrAdd(type, Build);

    /// <summary>Finds a member by its JSON name, or <see langword="null"/>.</summary>
    public JsonContractMember? MemberByJsonName(string jsonName)
    {
        foreach (var member in Members)
        {
            if (string.Equals(member.JsonName, jsonName, StringComparison.Ordinal))
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="type"/> is one of the model's numeric value types.</summary>
    public static bool IsNumeric(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(int)
               || underlying == typeof(long)
               || underlying == typeof(uint)
               || underlying == typeof(ulong)
               || underlying == typeof(short)
               || underlying == typeof(double)
               || underlying == typeof(float)
               || underlying == typeof(decimal);
    }

    /// <summary>The element type when <paramref name="type"/> is a <see cref="ValueList{T}"/>, else null.</summary>
    public static Type? ValueListElementType(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueList<>)
            ? type.GetGenericArguments()[0]
            : null;

    private static JsonContract? Build(Type type)
    {
        var candidate = Nullable.GetUnderlyingType(type) ?? type;
        if (candidate.Namespace != typeof(JsonContract).Namespace)
        {
            return null;
        }

        if (!candidate.IsClass || candidate == typeof(string) || ValueListElementType(candidate) is not null)
        {
            return null;
        }

        // A type that brings its own converter (ValueList<T>, ProvenanceMap) is opaque to the schema
        // walk: its JSON shape is the converter's business, not its constructor's.
        if (candidate.GetCustomAttribute<JsonConverterAttribute>() is not null)
        {
            return null;
        }

        var constructors = candidate.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var constructor = constructors.FirstOrDefault(c => c.GetCustomAttribute<JsonConstructorAttribute>() is not null)
                          ?? constructors.OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
        if (constructor is null || constructor.GetParameters().Length == 0)
        {
            return null;
        }

        var nullability = new NullabilityInfoContext();
        var members = new List<JsonContractMember>();
        foreach (var parameter in constructor.GetParameters())
        {
            var clrName = parameter.Name
                          ?? throw new InvalidOperationException($"{candidate.Name} has an unnamed constructor parameter.");
            var property = candidate.GetProperty(clrName, BindingFlags.Public | BindingFlags.Instance);
            var explicitName = property?.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
            var allowsNull = Nullable.GetUnderlyingType(parameter.ParameterType) is not null
                             || nullability.Create(parameter).WriteState != NullabilityState.NotNull;
            members.Add(new JsonContractMember(
                JsonName: explicitName ?? JsonNamingPolicy.CamelCase.ConvertName(clrName),
                ClrName: clrName,
                Type: parameter.ParameterType,
                Required: !parameter.HasDefaultValue,
                AllowsNull: allowsNull));
        }

        return new JsonContract(candidate, members.ToArray());
    }
}
