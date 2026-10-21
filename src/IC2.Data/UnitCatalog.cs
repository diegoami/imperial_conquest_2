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

    /// <summary>
    /// The quality word for a quality code: the DAT's word table indexed by quality (#741, research
    /// <c>2026-10-05-information-window-fields-and-bands.md</c>): "not ready" for 0–3, "very poor" for 4, then
    /// poor … elite for 5–9.
    /// </summary>
    public static string QualityName(ushort code) => code switch
    {
        <= 3 => "not ready",
        4 => "very poor",
        5 => "poor",
        6 => "average",
        7 => "good",
        8 => "very good",
        9 => "elite",
        _ => $"quality {code}"
    };
}
