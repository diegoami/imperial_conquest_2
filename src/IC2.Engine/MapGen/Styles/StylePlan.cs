namespace IC2.Engine.MapGen.Styles;

/// <summary>
/// T294c: the geographic plan one recipe declares — warped shapes (as map fractions, so a plan
/// composes at any size), the ridges with their pass counts, the texture thresholds and the
/// population sizes. The composing pipeline itself is <see cref="StyleComposer"/>.
/// </summary>
/// <param name="CoastalSeats">How many of the seats' capitals must stand on the coast (ports).</param>
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
    NameBank Names);

/// <summary>A warped disc as map fractions: centre by width/height, radius by the smaller side.</summary>
public sealed record Shape(double Fx, double Fy, double Fr);

/// <summary>A crest ridge between two fractional points, with its deliberate plain passes.</summary>
public sealed record Ridge(double FromFx, double FromFy, double ToFx, double ToFy, int Passes);

/// <summary>
/// T294c: one style's authored generic name material — invented nation and leader names, and city
/// names composed from authored prefix/suffix parts. No historical powers are transplanted
/// (the T294 owner decision, docs/tasks/T294.md); T294d owns the final authored lists.
/// </summary>
/// <param name="CityPrefixes">Leading name parts.</param>
/// <param name="CitySuffixes">Trailing name parts; a city name is one prefix plus one suffix.</param>
public sealed record NameBank(
    IReadOnlyList<string> Nations,
    IReadOnlyList<string> CityPrefixes,
    IReadOnlyList<string> CitySuffixes,
    IReadOnlyList<string> Leaders);
