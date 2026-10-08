using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T140 (Done-when 1): the band tables behind the city, army, fleet and nation information panels —
/// every edge the research read pins with both sides staged in play, plus the [designed] below-first-band
/// fallback for loyalty and morale. Cited row by row from
/// <c>2026-10-05-information-window-fields-and-bands.md</c>'s "Band tables" section.
/// </summary>
public sealed class InformationWordsTests
{
    // ---- Unity edges (six bands, 0–499 / 500–599 / 600–699 / 700–799 / 800–899 / 900–999, blank at 1000+) ----

    [Theory]
    [InlineData(499, "very low")]
    [InlineData(500, "low")]
    [InlineData(599, "low")]
    [InlineData(600, "normal")]
    [InlineData(699, "normal")]
    [InlineData(700, "high")]
    [InlineData(799, "high")]
    [InlineData(800, "very high")]
    [InlineData(899, "very high")]
    [InlineData(900, "excellent")]
    [InlineData(999, "excellent")]
    public void Unity_edges_map_to_their_words(int value, string word)
    {
        Assert.Equal(word, InformationWords.Unity(value));
    }

    [Theory]
    [InlineData(1000, "")]
    [InlineData(1100, "")]
    public void Unity_above_the_last_band_is_blank(int value, string word)
    {
        Assert.Equal(word, InformationWords.Unity(value));
    }

    [Theory]
    [InlineData(0, "very low")]
    [InlineData(300, "very low")]
    [InlineData(499, "very low")]
    public void Unity_below_the_first_band_uses_the_first_word(int value, string word)
    {
        // Below the first band (the research read's confirmed range is 300–990) the [designed]
        // default returns the first word.
        Assert.Equal(word, InformationWords.Unity(value));
    }

    // ---- Loyalty edges (same 6 words at /10 divisor) ----

    [Theory]
    [InlineData(0, "very low")]
    [InlineData(49, "very low")]
    [InlineData(50, "low")]
    [InlineData(59, "low")]
    [InlineData(60, "normal")]
    [InlineData(69, "normal")]
    [InlineData(70, "high")]
    [InlineData(79, "high")]
    [InlineData(80, "very high")]
    [InlineData(89, "very high")]
    [InlineData(90, "excellent")]
    [InlineData(99, "excellent")]
    public void Loyalty_edges_map_to_their_words(int value, string word)
    {
        Assert.Equal(word, InformationWords.Loyalty(value));
    }

    [Theory]
    [InlineData(100, "")]
    [InlineData(109, "")]
    public void Loyalty_above_the_last_band_is_blank(int value, string word)
    {
        Assert.Equal(word, InformationWords.Loyalty(value));
    }

    [Theory]
    [InlineData(-1, "very low")]
    [InlineData(-10, "very low")]
    public void Loyalty_below_the_first_band_returns_the_first_word(int value, string word)
    {
        // Below 0 the original reads neighbouring memory and prints junk; the [designed] default
        // returns the first word instead.
        Assert.Equal(word, InformationWords.Loyalty(value));
    }

    // ---- Morale edges (six bands indexed from m - 51 sar 2, with m - 48 below 51) ----

    [Theory]
    [InlineData(50, "very low")]
    [InlineData(54, "very low")]
    [InlineData(55, "low")]
    [InlineData(58, "low")]
    [InlineData(59, "normal")]
    [InlineData(62, "normal")]
    [InlineData(63, "high")]
    [InlineData(66, "high")]
    [InlineData(67, "very high")]
    [InlineData(70, "very high")]
    [InlineData(71, "excellent")]
    [InlineData(74, "excellent")]
    public void Morale_edges_map_to_their_words(int value, string word)
    {
        Assert.Equal(word, InformationWords.Morale(value));
    }

    [Theory]
    [InlineData(75, "")]
    [InlineData(80, "")]
    public void Morale_above_the_last_band_is_blank(int value, string word)
    {
        Assert.Equal(word, InformationWords.Morale(value));
    }

    [Fact]
    public void Morale_below_the_first_band_returns_the_first_word()
    {
        // m < 40 reads before the morale table; the [designed] default returns the first word
        // instead. 50 in particular is the research read's A4_army_00_left.png confirmation.
        Assert.Equal("very low", InformationWords.Morale(39));
    }

    // ---- Foreign tribute edges ----

    [Theory]
    [InlineData(0, "poor")]
    [InlineData(10, "poor")]
    [InlineData(11, "moderate")]
    [InlineData(30, "moderate")]
    [InlineData(31, "rich")]
    [InlineData(100, "rich")]
    [InlineData(101, "very rich")]
    [InlineData(10_000, "very rich")]
    public void Foreign_tribute_edges_map_to_their_words(int value, string word)
    {
        Assert.Equal(word, InformationWords.Tribute(value));
    }

    [Fact]
    public void Foreign_tribute_above_10000_is_blank()
    {
        // Above 10,000 the original prints no word and the talents number text stays
        // (the panel's C08 / C09 rules pin the number text rather than the word).
        Assert.Equal(string.Empty, InformationWords.Tribute(10_001));
    }

    // ---- Relations ----

    [Theory]
    [InlineData(0, "")]
    [InlineData(-1, "")]
    [InlineData(-3, "")]
    [InlineData(1, "trade")]
    [InlineData(2, "ally")]
    [InlineData(3, "war")]
    [InlineData(4, "")]
    [InlineData(5, "")]
    public void Relations_outside_the_positive_three_band_are_blank(int value, string word)
    {
        Assert.Equal(word, InformationWords.Relation(value));
    }

    // ---- Sea ----

    [Theory]
    [InlineData(0, "calm")]
    [InlineData(1, "rough")]
    [InlineData(-1, "rough")]
    [InlineData(2, "rough")]
    public void Sea_returns_calm_only_for_a_zero_tile_code(int code, string word)
    {
        Assert.Equal(word, InformationWords.Sea(code));
    }
}
