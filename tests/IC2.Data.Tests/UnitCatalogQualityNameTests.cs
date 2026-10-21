using IC2.Data;
using Xunit;

namespace IC2.Data.Tests;

/// <summary>#741: the original's quality words, the DAT word table indexed by quality.</summary>
public sealed class UnitCatalogQualityNameTests
{
    [Theory]
    [InlineData(0, "not ready")]
    [InlineData(3, "not ready")]
    [InlineData(4, "very poor")]
    [InlineData(5, "poor")]
    [InlineData(9, "elite")]
    [InlineData(10, "quality 10")]
    public void QualityName_MatchesTheOriginalsWordTable(int code, string expected) =>
        Assert.Equal(expected, UnitCatalog.QualityName((ushort)code));
}
