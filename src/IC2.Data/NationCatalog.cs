namespace IC2.Data;

/// <summary>Nation identifiers transcribed from the user's ordered nation screenshots.</summary>
public static class NationCatalog
{
    private static readonly string[] Names =
    {
        "Rome", "Carthage", "Seleucid", "Ptolemaic", "Macedonia", "Numidia",
        "Gaul", "Greece", "Celtiberia", "Illyria", "Dacia", "Bithynia",
        "Galatia", "Armenia", "Media", "Thracia"
    };

    public static string Name(ushort code) => code < Names.Length ? Names[code] : $"nation {code}";
}
