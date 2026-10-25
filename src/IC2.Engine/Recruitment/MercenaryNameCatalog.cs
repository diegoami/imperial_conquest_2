namespace IC2.Engine.Recruitment;

/// <summary>
/// The original mercenary label table, indexed by <see cref="IC2.Engine.Model.MercenaryPoolSlot.NameLabel"/>. The
/// 52 names come from the DAT table at 0x1F8C6 / runtime 0x0049CC94, 20-byte stride
/// [confirmed: <c>decompiled-mercenary-offer-list-and-position.md</c>, research <c>b90f691</c>].
/// </summary>
public static class MercenaryNameCatalog
{
    private static readonly string[] NamesByLabel =
    [
        "Regular", "Numidian", "Greek", "Celtiberian", "Moor", "Persian", "Mysian", "Cilician",
        "Thracian", "Elymaian", "Galatian", "Gallic", "Cretan", "Bactrian", "Kappadockian",
        "Libyan", "Arab", "Thessalian", "Paionian", "Scythian", "Sarmatian", "Ligurian",
        "Balearic", "Median", "Tarantine", "Pergamene", "Gaesatai", "Nubian", "Asturian",
        "Boeotian", "Kardackian", "Sakan", "Rhodian", "Gastulian", "Insubre", "Egyptian",
        "Vascone", "Etruscan", "Samnite", "Syracusan", "Boii", "Spanish", "African", "Aetolian",
        "Illyrian", "Cenomani", "Getai", "Agriane", "Rhoxolani", "Dahai", "Kyrtii", "Cossaei",
    ];

    /// <summary>The original name for a label, or the existing placeholder for an unknown label.</summary>
    public static string ForLabel(int label) =>
        (uint)label < (uint)NamesByLabel.Length
            ? NamesByLabel[label]
            : $"Mercenary unit (label {label})";
}
