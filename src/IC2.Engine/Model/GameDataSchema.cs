namespace IC2.Engine.Model;

/// <summary>
/// Every file the engine loads declares its schema version in its first field, and the loader
/// rejects anything it does not know rather than defaulting missing fields silently.
/// </summary>
public interface IVersionedDocument
{
    /// <summary>The schema version this document was written against.</summary>
    int SchemaVersion { get; }
}

/// <summary>Schema versions the engine accepts.</summary>
/// <remarks>
/// This is the one numeric constant in <c>IC2.Engine.Model</c> that is <em>not</em> a gameplay value:
/// it identifies the file contract, not a rule, so it deliberately does not live in a
/// <see cref="Ruleset"/>. <c>tests/IC2.Engine.Tests/Model/NoHardcodedConstantsTests.cs</c> allowlists it
/// by name and fails on any other numeric constant in the model or serialization namespaces.
/// </remarks>
public static class GameDataSchema
{
    /// <summary>The only schema version this build reads or writes.</summary>
    public const int CurrentVersion = 1;
}
