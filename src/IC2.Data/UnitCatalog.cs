namespace IC2.Data;

/// <summary>Unit-type labels corroborated by the user's army and recruitment screenshots.
/// A fuller per-type stat table (moves, standard battalion size, shots, range, recruit cost) was
/// located directly in the DAT file at a string-search-found offset and fully decoded — see
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/unit-type-stat-table-in-dat.md. Not yet added here: only one DAT file was checked,
/// and the report deliberately left open whether its offset is stable across DAT versions.</summary>
public static class UnitCatalog
{
    public static string TypeName(ushort code) => code switch
    {
        0 => "light infantry",
        1 => "heavy infantry",
        2 => "archers",
        3 => "light cavalry",
        4 => "heavy cavalry",
        _ => $"type {code}"
    };

    public static string QualityName(ushort code) => code switch
    {
        5 => "poor",
        6 => "average",
        7 => "good",
        8 => "very good",
        9 => "elite",
        _ => $"quality {code}"
    };
}
