using IC2.Engine.Model;

namespace IC2.Engine.MapGen;

/// <summary>
/// T294b: a recipe composes one candidate map per attempt — landmass, water, rivers, settlements,
/// ports and forces — under its style's own rules. Everything else (validation, retry with a
/// bounded attempt count, conversion to a <see cref="World"/>) belongs to <see cref="MapGenerator"/>;
/// the five shipped styles are T294c's deliverable and none ship from here.
/// </summary>
public interface IMapStyle
{
    /// <summary>The style's stable id, part of generated world and scenario identities.</summary>
    string Id { get; }

    /// <summary>Composes one candidate from this style. Must be deterministic in (rng, size, parameters).</summary>
    GeneratedMap Compose(MapSeedStream rng, MapSize size, IReadOnlyDictionary<string, string> parameters);
}
