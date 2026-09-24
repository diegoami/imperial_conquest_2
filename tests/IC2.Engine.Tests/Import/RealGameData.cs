using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Export;

namespace IC2.Engine.Tests.Import;

/// <summary>
/// The real, committed <c>classical-mediterranean</c> World/Ruleset/Scenario — the only ones an
/// original save is ever balanced against — loaded once and shared read-only across this namespace's
/// test classes, via <see cref="ExportedDataPaths"/> (<c>tests/IC2.Engine.Tests/Export</c>, same
/// assembly).
/// </summary>
internal static class RealGameData
{
    public static World World { get; } = GameDataLoader.LoadFile<World>(ExportedDataPaths.WorldFile);
    public static Ruleset Ruleset { get; } = GameDataLoader.LoadFile<Ruleset>(ExportedDataPaths.RulesetFile);
    public static Scenario Scenario { get; } = GameDataLoader.LoadFile<Scenario>(ExportedDataPaths.ScenarioFile);
}
