using IC2.Engine.Armies;
using IC2.Engine.Model;
using Xunit;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T15 Army and unit management", Done-when 5: auto-naming produces the
/// <c>Nth Foot/Guards/Bowmen/Lancers/Dragoons Battalion</c> ordinals counted across the whole nation,
/// matching the published roster in <c>army-records-and-roman-roster.md</c> (the Roman army at
/// <c>(100, 42)</c> in <c>11_supply.sav</c>, 13 units).
/// </summary>
public sealed class ArmyNamingTests
{
    /// <summary>
    /// The published roster, transcribed verbatim (name/type only -- troops and quality do not affect
    /// naming and are irrelevant here): <c>1st</c>/<c>2nd Foot</c>, <c>1st</c>-<c>8th Guards</c>,
    /// <c>1st</c>/<c>2nd Dragoons</c>, and a lone <c>2nd Lancers</c> with no <c>1st</c> present -- the
    /// gap this task's free-ordinal algorithm is meant to notice and reuse.
    /// </summary>
    private static ArmyState RomanRoster() => Army("roman-13", NorthNationId, 100, 42, new[]
    {
        RegularUnit("1st Foot Battalion", "light_infantry"),
        RegularUnit("1st Guards Battalion", "heavy_infantry"),
        RegularUnit("2nd Guards Battalion", "heavy_infantry"),
        RegularUnit("3rd Guards Battalion", "heavy_infantry"),
        RegularUnit("1st Dragoons Battalion", "heavy_cavalry"),
        RegularUnit("7th Guards Battalion", "heavy_infantry"),
        RegularUnit("2nd Dragoons Battalion", "heavy_cavalry"),
        RegularUnit("2nd Lancers Battalion", "light_cavalry"),
        RegularUnit("6th Guards Battalion", "heavy_infantry"),
        RegularUnit("5th Guards Battalion", "heavy_infantry"),
        RegularUnit("4th Guards Battalion", "heavy_infantry"),
        RegularUnit("8th Guards Battalion", "heavy_infantry"),
        RegularUnit("2nd Foot Battalion", "light_infantry"),
    });

    [Theory]
    [InlineData("light_infantry", "3rd Foot Battalion")] // 1st, 2nd used -> next free is 3rd.
    [InlineData("heavy_infantry", "9th Guards Battalion")] // 1st..8th used -> next free is 9th.
    [InlineData("heavy_cavalry", "3rd Dragoons Battalion")] // 1st, 2nd used -> next free is 3rd.
    [InlineData("light_cavalry", "1st Lancers Battalion")] // only 2nd used -> the 1st gap is free.
    // The roster itself has no archers, but the toy scenario's own "portus" (owned by north) already
    // garrisons a real "1st Bowmen Battalion" -- WithArmies replaces state.Armies only, so that city
    // fixture is still there. The next free ordinal is 2nd, which is itself evidence the city-garrison
    // half of the scan (NextName_CountsAnOwnedCitysGarrisonToo, below) is really wired in, not just typed.
    [InlineData("archers", "2nd Bowmen Battalion")]
    public void NextName_AgainstThePublishedRomanRoster_MatchesTheExpectedOrdinal(string unitTypeId, string expected)
    {
        var state = WithArmies(InitialState(), RomanRoster());

        var actual = ArmyNaming.NextName(state, NorthNationId, unitTypeId);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NextName_CountsAcrossEveryArmyOfTheNation_NotJustOne()
    {
        var first = Army("roman-a", NorthNationId, 1, 1, new[] { RegularUnit("1st Foot Battalion", "light_infantry") });
        var second = Army("roman-b", NorthNationId, 2, 2, new[] { RegularUnit("2nd Foot Battalion", "light_infantry") });
        var state = WithArmies(InitialState(), first, second);

        Assert.Equal("3rd Foot Battalion", ArmyNaming.NextName(state, NorthNationId, "light_infantry"));
    }

    [Fact]
    public void NextName_CountsAnOwnedCitysGarrisonToo()
    {
        var army = Army("roman-c", NorthNationId, 1, 1, new[] { RegularUnit("1st Foot Battalion", "light_infantry") });
        var state = WithArmies(InitialState(), army);
        var arx = state.CityById("arx")!; // owned by north.
        state = WithCity(state, arx with { Garrison = ValueList.Of(RegularUnit("2nd Foot Battalion", "light_infantry")) });

        Assert.Equal("3rd Foot Battalion", ArmyNaming.NextName(state, NorthNationId, "light_infantry"));
    }

    [Fact]
    public void NextName_IgnoresAnotherNationsUnitsOfTheSameType()
    {
        var north = Army("roman-d", NorthNationId, 1, 1, new[] { RegularUnit("1st Foot Battalion", "light_infantry") });
        var south = Army("roman-e", SouthNationId, 2, 2, new[] { RegularUnit("2nd Foot Battalion", "light_infantry") });
        var state = WithArmies(InitialState(), north, south);

        // South's "2nd" does not count against north's ordinal scan: north's next free is 2nd, not 3rd.
        Assert.Equal("2nd Foot Battalion", ArmyNaming.NextName(state, NorthNationId, "light_infantry"));
    }

    [Fact]
    public void NextName_WithNoExistingUnitsOfThatType_StartsAtFirst()
    {
        // Unlike archers (portus already garrisons "1st Bowmen Battalion"), the toy scenario's north
        // has no light_cavalry unit anywhere -- a genuinely clean slate.
        var state = InitialState();

        Assert.Equal("1st Lancers Battalion", ArmyNaming.NextName(state, NorthNationId, "light_cavalry"));
    }

    [Theory]
    [InlineData(10, "11th")]
    [InlineData(12, "13th")]
    [InlineData(20, "21st")]
    [InlineData(22, "23rd")]
    public void NextName_OrdinalSuffix_FollowsEnglishRulesIncludingTheElevenToThirteenException(int existingCount, string expectedOrdinal)
    {
        var units = Enumerable.Range(1, existingCount).Select(n => RegularUnit($"{Suffix(n)} Foot Battalion", "light_infantry"));
        var state = WithArmies(InitialState(), Army("roman-f", NorthNationId, 1, 1, units));

        var actual = ArmyNaming.NextName(state, NorthNationId, "light_infantry");

        Assert.Equal($"{expectedOrdinal} Foot Battalion", actual);
    }

    private static string Suffix(int n) => (n % 100) switch
    {
        11 or 12 or 13 => $"{n}th",
        _ => (n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th",
        },
    };

    [Fact]
    public void NextName_UnknownUnitType_Throws()
    {
        var state = InitialState();

        Assert.Throws<ArgumentException>(() => ArmyNaming.NextName(state, NorthNationId, "no-such-type"));
    }
}
