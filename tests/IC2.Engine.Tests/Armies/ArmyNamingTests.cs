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
/// <remarks>
/// <strong>The <c>Lancers</c> expectation below was inverted by
/// <a href="https://github.com/diegoami/imperial_conquest_2/issues/243">#243</a>.</strong> It used to
/// pin <c>1st Lancers Battalion</c> — a gap being reused — which the <c>1_rome_270_autumn_1 →
/// autumn_3</c> pair refutes: the same roster, one army, one light-cavalry unit named
/// <c>2nd Lancers</c>, and the original names the next one <c>3rd</c>. The fixture is kept exactly as
/// it was and only the answer changed, because that lone gap is still the most discriminating case in
/// the suite — it is the only ordinal in the whole roster on which the two candidate rules disagree.
/// </remarks>
public sealed class ArmyNamingTests
{
    /// <summary>
    /// The published roster, transcribed verbatim (name/type only -- troops and quality do not affect
    /// naming and are irrelevant here): <c>1st</c>/<c>2nd Foot</c>, <c>1st</c>-<c>8th Guards</c>,
    /// <c>1st</c>/<c>2nd Dragoons</c>, and a lone <c>2nd Lancers</c> with no <c>1st</c> present -- the
    /// gap that tells the two candidate ordinal rules apart, and which the corpus settles in favour of
    /// one-past-the-highest (see this class's remarks).
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
    [InlineData("light_infantry", "3rd Foot Battalion")] // highest used is 2nd -> 3rd.
    [InlineData("heavy_infantry", "9th Guards Battalion")] // highest used is 8th -> 9th.
    [InlineData("heavy_cavalry", "3rd Dragoons Battalion")] // highest used is 2nd -> 3rd.
    // #243: the discriminating case. Only 2nd Lancers exists and 1st is free, but the ordinal is one
    // past the HIGHEST, so the gap is not reused -- which is what 1_rome_270_autumn_3.sav records.
    [InlineData("light_cavalry", "3rd Lancers Battalion")]
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
        // has no light_cavalry unit anywhere -- a genuinely clean slate, so the highest ordinal in use
        // is 0 and the first one raised is the 1st.
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

    /// <summary>
    /// #243's second defect: the corpus stores several names with a <strong>double</strong> space before
    /// "Battalion" while units created during play use a single one, and the pattern used to require
    /// exactly one — silently discarding those ordinals.
    /// </summary>
    /// <remarks>
    /// This is not a hypothetical tolerance. Rome's only two heavy-cavalry units in
    /// <c>1_rome_270_autumn_1.sav</c> are <c>"1st Dragoons  Battalion"</c> and
    /// <c>"2nd Dragoons  Battalion"</c>, <em>both</em> double-spaced, and the unit mobilized from that
    /// save comes out <c>3rd Dragoons Battalion</c> — unreachable unless both ordinals were read. The
    /// fixture below is that pair, verbatim.
    /// </remarks>
    [Fact]
    public void NextName_ReadsAnOrdinalOutOfTheDoubleSpacedFormTheSavesHold()
    {
        var doubleSpaced = Army("roman-g", NorthNationId, 1, 1, new[]
        {
            RegularUnit("1st Dragoons  Battalion", "heavy_cavalry"),
            RegularUnit("2nd Dragoons  Battalion", "heavy_cavalry"),
        });
        var state = WithArmies(InitialState(), doubleSpaced);

        // Both ordinals are seen, so the next is the 3rd -- and it is emitted in the single-space form
        // the original's own newly created units use.
        Assert.Equal("3rd Dragoons Battalion", ArmyNaming.NextName(state, NorthNationId, "heavy_cavalry"));

        // The mixed case the same save holds: one double-spaced, one single.
        var mixed = Army("roman-h", NorthNationId, 2, 2, new[]
        {
            RegularUnit("1st Foot  Battalion", "light_infantry"),
            RegularUnit("2nd Foot Battalion", "light_infantry"),
        });
        Assert.Equal(
            "3rd Foot Battalion",
            ArmyNaming.NextName(WithArmies(InitialState(), mixed), NorthNationId, "light_infantry"));
    }

    /// <summary>
    /// #243: an ordinal left behind by a disbanded unit is retired, not recycled — stated on its own,
    /// away from the Roman roster, so the rule is pinned by more than one fixture.
    /// </summary>
    [Fact]
    public void NextName_DoesNotReuseAGapLeftBelowTheHighestOrdinal()
    {
        var gappy = Army("roman-i", NorthNationId, 1, 1, new[]
        {
            RegularUnit("4th Foot Battalion", "light_infantry"),
        });
        var state = WithArmies(InitialState(), gappy);

        // 1st, 2nd and 3rd are all free; the answer is still 5th.
        Assert.Equal("5th Foot Battalion", ArmyNaming.NextName(state, NorthNationId, "light_infantry"));
    }

    /// <summary>
    /// A mercenary takes no battalion ordinal, whatever it is called — <c>FUN_0044a218</c> skips every
    /// unit whose origin label is non-zero. Asserted with a mercenary that <em>does</em> bear a
    /// battalion-shaped name, so the origin label is doing the work rather than the name's shape.
    /// </summary>
    [Fact]
    public void NextName_SkipsAMercenaryEvenWhenItsNameLooksLikeABattalion()
    {
        var withMercenary = Army("roman-j", NorthNationId, 1, 1, new[]
        {
            RegularUnit("1st Foot Battalion", "light_infantry"),
            MercenaryUnit("9th Foot Battalion", "light_infantry"),
        });
        var state = WithArmies(InitialState(), withMercenary);

        Assert.Equal("2nd Foot Battalion", ArmyNaming.NextName(state, NorthNationId, "light_infantry"));
    }

    [Fact]
    public void NextName_UnknownUnitType_Throws()
    {
        var state = InitialState();

        Assert.Throws<ArgumentException>(() => ArmyNaming.NextName(state, NorthNationId, "no-such-type"));
    }

    /// <summary>
    /// #245 N1: the original's scan filters on <c>troops &gt; 0</c> -- a vacated slot that still carries a
    /// stale battalion name (exactly what <c>1_rome_270_autumn_1.sav</c> army 0, slot 13 shows: "4th
    /// Guards  Battalion" with 0 troops behind twelve live units) must not be counted.
    /// </summary>
    [Fact]
    public void NextName_SkipsAZeroTroopSlotEvenWithAHigherStaleOrdinal()
    {
        var withStaleSlot = Army("roman-k", NorthNationId, 1, 1, new[]
        {
            RegularUnit("1st Foot Battalion", "light_infantry"),
            RegularUnit("9th Foot Battalion", "light_infantry", troops: 0), // stale, vacated slot.
        });
        var state = WithArmies(InitialState(), withStaleSlot);

        // The live unit's highest is 1st, so next is 2nd -- the stale 9th-ordinal, zero-troop slot is
        // ignored, exactly as the original's own troops > 0 filter would skip it.
        Assert.Equal("2nd Foot Battalion", ArmyNaming.NextName(state, NorthNationId, "light_infantry"));
    }
}
