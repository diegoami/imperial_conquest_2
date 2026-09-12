namespace IC2.Data;

/// <summary>Unit-type labels corroborated by the user's army and recruitment screenshots.</summary>
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
        6 => "average",
        7 => "good",
        8 => "very good",
        9 => "elite",
        _ => $"quality {code}"
    };
}
