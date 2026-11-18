namespace IC2.Engine.MapGen;

using IC2.Engine.MapGen.Styles;

/// <summary>
/// T294c: the five shipped style recipes, in the design's order (Mediterranean first). The New
/// Game flow (T294f) offers exactly these; tests pin the registry to the five ids so a recipe is
/// never silently dropped or added.
/// </summary>
public static class MapStyles
{
    public static IReadOnlyList<IMapStyle> All { get; } =
    [
        new MediterraneanStyle(),
        new MiddleEastStyle(),
        new CentralAsiaStyle(),
        new IndiaStyle(),
        new ChinaStyle(),
    ];

    /// <summary>The shipped style with this id, or null.</summary>
    public static IMapStyle? ById(string id) => All.FirstOrDefault(s => s.Id == id);
}
