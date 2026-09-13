using System.Text.Json.Nodes;
using IC2.Engine.Model;

namespace IC2.Engine.Serialization;

/// <summary>
/// Checks a parsed JSON document against a model record's <see cref="JsonContract"/> before it is
/// deserialized.
/// </summary>
/// <remarks>
/// <para>
/// System.Text.Json's default behaviour is exactly what the task entry forbids: a missing field becomes
/// a default value and an unrecognised field is dropped, both silently. This walk closes both holes and
/// — because it runs over the document tree rather than over the serializer's internals — it can name
/// the offending field by its full path (<c>cities[1].loyalty</c>) instead of relying on the wording of
/// a <c>JsonException</c>.
/// </para>
/// </remarks>
public static class SchemaValidator
{
    /// <summary>Validates <paramref name="node"/> as an instance of <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The model record type the document should describe.</typeparam>
    /// <param name="documentPath">The file name, used in error messages.</param>
    /// <param name="node">The parsed document.</param>
    /// <exception cref="MalformedGameDataException">A node has the wrong JSON kind.</exception>
    /// <exception cref="MissingRequiredFieldException">A required field is absent.</exception>
    /// <exception cref="UnknownFieldException">A field the contract does not declare is present.</exception>
    public static void Validate<T>(string documentPath, JsonNode? node) =>
        ValidateNode(documentPath, node, typeof(T), path: string.Empty, allowsNull: false);

    private static void ValidateNode(string documentPath, JsonNode? node, Type type, string path, bool allowsNull)
    {
        if (node is null)
        {
            if (!allowsNull)
            {
                throw new MalformedGameDataException(
                    documentPath,
                    $"field '{Describe(path)}' is null, but {FriendlyName(type)} does not permit null there.");
            }

            return;
        }

        var elementType = JsonContract.ValueListElementType(type);
        if (elementType is not null)
        {
            if (node is not JsonArray array)
            {
                throw new MalformedGameDataException(
                    documentPath, $"field '{Describe(path)}' must be a JSON array.");
            }

            for (var i = 0; i < array.Count; i++)
            {
                ValidateNode(documentPath, array[i], elementType, $"{path}[{i}]", allowsNull: false);
            }

            return;
        }

        var contract = JsonContract.For(type);
        if (contract is null)
        {
            // A leaf the model reads directly (number, string, bool, enum) or a converter-owned type.
            // Its value is checked when the document is deserialized; a bad value surfaces as malformed.
            return;
        }

        if (node is not JsonObject obj)
        {
            throw new MalformedGameDataException(
                documentPath, $"field '{Describe(path)}' must be a JSON object describing {FriendlyName(type)}.");
        }

        foreach (var property in obj)
        {
            if (contract.MemberByJsonName(property.Key) is null)
            {
                throw new UnknownFieldException(documentPath, Join(path, property.Key), FriendlyName(type));
            }
        }

        foreach (var member in contract.Members)
        {
            var memberPath = Join(path, member.JsonName);
            if (!obj.TryGetPropertyValue(member.JsonName, out var value))
            {
                if (member.Required)
                {
                    throw new MissingRequiredFieldException(documentPath, memberPath, FriendlyName(type));
                }

                continue;
            }

            ValidateNode(documentPath, value, member.Type, memberPath, member.AllowsNull);
        }
    }

    private static string Join(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    private static string Describe(string path) => path.Length == 0 ? "<root>" : path;

    private static string FriendlyName(Type type) => type.Name;
}
