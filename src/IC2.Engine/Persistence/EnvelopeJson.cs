using System.Text.Json.Nodes;
using IC2.Engine.Serialization;

namespace IC2.Engine.Persistence;

/// <summary>
/// Small, typed reads over a save envelope's own <see cref="JsonObject"/> tree — shared by
/// <see cref="SaveManager"/> (reading the current envelope) and <see cref="SaveMigrations"/> (reading an
/// older one mid-migration), so both report the same typed errors for the same shape of mistake.
/// </summary>
internal static class EnvelopeJson
{
    /// <summary>Reads a required child object, or throws.</summary>
    public static JsonObject RequireObject(string documentPath, JsonObject parent, string fieldName, string owner)
    {
        if (!parent.TryGetPropertyValue(fieldName, out var node) || node is null)
        {
            throw new MissingRequiredFieldException(documentPath, fieldName, owner);
        }

        if (node is not JsonObject obj)
        {
            throw new MalformedGameDataException(documentPath, $"'{fieldName}' must be an object.");
        }

        return obj;
    }

    /// <summary>Reads a required integer field, or throws.</summary>
    public static int RequireInt(string documentPath, JsonObject parent, string fieldName, string owner)
    {
        if (!parent.TryGetPropertyValue(fieldName, out var node) || node is null)
        {
            throw new MissingRequiredFieldException(documentPath, fieldName, owner);
        }

        try
        {
            return node.GetValue<int>();
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            throw new MalformedGameDataException(documentPath, $"'{fieldName}' must be an integer.", ex);
        }
    }

    /// <summary>Reads a required string field, or throws.</summary>
    public static string RequireString(string documentPath, JsonObject parent, string fieldName, string owner)
    {
        if (!parent.TryGetPropertyValue(fieldName, out var node) || node is null)
        {
            throw new MissingRequiredFieldException(documentPath, fieldName, owner);
        }

        try
        {
            return node.GetValue<string>();
        }
        catch (InvalidOperationException ex)
        {
            throw new MalformedGameDataException(documentPath, $"'{fieldName}' must be a string.", ex);
        }
    }
}
