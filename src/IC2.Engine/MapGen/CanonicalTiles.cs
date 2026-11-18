using IC2.Engine.Model;

namespace IC2.Engine.MapGen;

/// <summary>
/// T294b: the canonical tile table a generated world ships, matching the shipped authored tables
/// (italy-500bc.json and every sibling world). A generated world reuses these ids rather than
/// inventing new ones so every shipped ruleset's move-cost table applies unchanged; a test pins
/// this table against the shipped data to stop drift. Costs here are the canonical land costs the
/// validator checks corridors against (plain 1, desert 1, forest 2, mountains 4, rivers 4).
/// </summary>
public static class CanonicalTiles
{
    public const int SeaCalm = 0;
    public const int SeaRough = 1;
    public const int Plain = 2;
    public const int Desert = 3;
    public const int Forest = 4;
    public const int Mountains = 5;
    public const int River1 = 6;

    /// <summary>Whether an army may enter the tile code.</summary>
    public static bool PassableByArmies(int code) => code is >= 2;

    /// <summary>Whether a fleet may enter the tile code — sea only; rivers are army terrain.</summary>
    public static bool PassableByFleets(int code) => code is 0 or 1;

    /// <summary>The canonical land move cost of the tile code (the shipped tables' values).</summary>
    public static int MoveCost(int code) => code switch
    {
        Plain or Desert => 1,
        Forest => 2,
        Mountains => 4,
        >= River1 => 4,
        _ => 1,
    };

    public static ValueList<TileType> Values => new(new List<TileType>
    {
        new("sea_calm", 0, "Sea", PassableByArmies: false, PassableByFleets: true),
        new("sea_rough", 1, "Sea", PassableByArmies: false, PassableByFleets: true),
        new("plain", 2, "Plain", PassableByArmies: true, PassableByFleets: false),
        new("desert", 3, "Desert", PassableByArmies: true, PassableByFleets: false),
        new("forest", 4, "Forest", PassableByArmies: true, PassableByFleets: false),
        new("mountains", 5, "Mountains", PassableByArmies: true, PassableByFleets: false),
        new("river_1", 6, "River", PassableByArmies: true, PassableByFleets: false),
        new("river_2", 7, "River", PassableByArmies: true, PassableByFleets: false),
        new("river_3", 8, "River", PassableByArmies: true, PassableByFleets: false),
        new("river_4", 9, "River", PassableByArmies: true, PassableByFleets: false),
        new("river_5", 10, "River", PassableByArmies: true, PassableByFleets: false),
        new("river_6", 11, "River", PassableByArmies: true, PassableByFleets: false),
    });

    /// <summary>Encodes a row-major cell grid as the base64 sidecar payload (little-endian u16 per cell).</summary>
    public static string Encode(int[] codes)
    {
        var bytes = new byte[codes.Length * 2];
        for (var i = 0; i < codes.Length; i++)
        {
            bytes[i * 2] = (byte)codes[i];
            bytes[(i * 2) + 1] = (byte)(codes[i] >> 8);
        }

        return Convert.ToBase64String(bytes);
    }
}
