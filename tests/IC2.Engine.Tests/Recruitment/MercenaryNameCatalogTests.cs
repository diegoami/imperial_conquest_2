using IC2.Engine.Recruitment;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

public sealed class MercenaryNameCatalogTests
{
    [Fact]
    public void All_original_labels_match_the_DAT_table_order()
    {
        var expected = new[]
        {
            "Regular", "Numidian", "Greek", "Celtiberian", "Moor", "Persian", "Mysian", "Cilician",
            "Thracian", "Elymaian", "Galatian", "Gallic", "Cretan", "Bactrian", "Kappadockian",
            "Libyan", "Arab", "Thessalian", "Paionian", "Scythian", "Sarmatian", "Ligurian",
            "Balearic", "Median", "Tarantine", "Pergamene", "Gaesatai", "Nubian", "Asturian",
            "Boeotian", "Kardackian", "Sakan", "Rhodian", "Gastulian", "Insubre", "Egyptian",
            "Vascone", "Etruscan", "Samnite", "Syracusan", "Boii", "Spanish", "African", "Aetolian",
            "Illyrian", "Cenomani", "Getai", "Agriane", "Rhoxolani", "Dahai", "Kyrtii", "Cossaei",
        };

        for (var label = 0; label < expected.Length; label++)
        {
            Assert.Equal(expected[label], MercenaryNameCatalog.ForLabel(label));
        }

        Assert.Equal("Mercenary unit (label -1)", MercenaryNameCatalog.ForLabel(-1));
        Assert.Equal("Mercenary unit (label 52)", MercenaryNameCatalog.ForLabel(52));
    }
}
