namespace IC2.Engine.MapGen.Styles;

/// <summary>
/// T294c: the geographic plan one recipe declares — warped shapes (as map fractions, so a plan
/// composes at any size), the ridges with their pass counts, the texture thresholds, the population
/// sizes and the authored opening-force profile. The composing pipeline itself is
/// <see cref="StyleComposer"/>.
/// </summary>
/// <param name="CoastalSeats">How many of the seats' capitals must stand on the coast (ports).</param>
/// <param name="ForceWeights">
/// T294d: the style's authored opening composition — weights per unit kind in the canonical order
/// <c>light_infantry, heavy_infantry, archers, light_cavalry, heavy_cavalry</c> (the five ids every
/// shipped ruleset shares). These are designed abstractions per docs/tasks/T294.md, not original data.
/// </param>
public sealed record StylePlan(
    string Id,
    string Title,
    IReadOnlyList<Shape> Land,
    IReadOnlyList<Shape> Islands,
    IReadOnlyList<Shape> Water,
    IReadOnlyList<Ridge> Ridges,
    int Rivers,
    double Dryness,
    double Woodiness,
    int Seats,
    int CoastalSeats,
    int TownsMin,
    int TownsMax,
    int Fleets,
    IReadOnlyList<int> ForceWeights,
    NameBank Names);

/// <summary>A warped disc as map fractions: centre by width/height, radius by the smaller side.</summary>
public sealed record Shape(double Fx, double Fy, double Fr);

/// <summary>A crest ridge between two fractional points, with its deliberate plain passes.</summary>
public sealed record Ridge(double FromFx, double FromFy, double ToFx, double ToFy, int Passes);

/// <summary>
/// T294d: one style's authored generic name material — invented nation and leader names, a curated
/// city-name list, and city-name parts for overflow. No historical powers are transplanted (the T294
/// owner decision, docs/tasks/T294.md); the lists live in <c>StyleNames.cs</c> so the authoring is
/// reviewable in one place.
/// </summary>
/// <param name="Cities">The style's curated city names, used before the parts generate more.</param>
/// <param name="CityPrefixes">Leading name parts.</param>
/// <param name="CitySuffixes">Trailing name parts; an overflow city name is one prefix plus one suffix.</param>
public sealed record NameBank(
    IReadOnlyList<string> Nations,
    IReadOnlyList<string> Leaders,
    IReadOnlyList<string> Cities,
    IReadOnlyList<string> CityPrefixes,
    IReadOnlyList<string> CitySuffixes);
