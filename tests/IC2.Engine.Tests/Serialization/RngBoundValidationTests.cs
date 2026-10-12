using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.SerializationTests;

/// <summary>
/// Bug #973: every ruleset value that reaches an <c>IRng</c> bound (<c>NextInt(bound)</c>,
/// <c>NextInt(min, max)</c>, <c>NextChance(1, d)</c>) is refused at load when it would make the draw throw,
/// naming the key, rather than crashing mid-game.
/// </summary>
public class RngBoundValidationTests
{
    private const string Document = "mutated-ruleset.json";

    public static IEnumerable<object[]> BoundMutations()
    {
        yield return new object[] { "ai.faithfulTypeDrawBound", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Ai = r.Ai with { FaithfulTypeDrawBound = v } }) };
        yield return new object[] { "ai.faithfulAmountSpanNumerator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Ai = r.Ai with { FaithfulAmountSpanNumerator = v } }) };
        yield return new object[] { "combat.casualtyDivisorRandomSpan", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { CasualtyDivisorRandomSpan = v } }) };
        yield return new object[] { "combat.promotionChanceDenominator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { PromotionChanceDenominator = v } }) };
        yield return new object[] { "combat.autoPeaceChanceDenominator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { AutoPeaceChanceDenominator = v } }) };
        yield return new object[] { "capture.conquestLoyaltyRandomBonusMax", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Capture = r.Capture with { ConquestLoyaltyRandomBonusMax = v } }) };
        yield return new object[] { "naval.zeroSupplyConditionRandomBound", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Naval = r.Naval with { ZeroSupplyConditionRandomBound = v } }) };
        yield return new object[] { "combat.naval.randomBandCount", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { Naval = r.Combat.Naval with { RandomBandCount = v } } }) };
        yield return new object[] { "economy.loyaltyRiseRollBound", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { LoyaltyRiseRollBound = v } }) };
        yield return new object[] { "recruitment.mercenaryRestockEmptyRollDenominator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Recruitment = r.Recruitment with { MercenaryRestockEmptyRollDenominator = v } }) };
        yield return new object[] { "recruitment.mercenaryRestockReplaceRollDenominator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Recruitment = r.Recruitment with { MercenaryRestockReplaceRollDenominator = v } }) };
        yield return new object[] { "recruitment.mercenaryRestockTemplateDrawCount", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Recruitment = r.Recruitment with { MercenaryRestockTemplateDrawCount = v } }) };
        yield return new object[] { "recruitment.mercenaryRestockQualityJitterSteps", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Recruitment = r.Recruitment with { MercenaryRestockQualityJitterSteps = v } }) };
        yield return new object[] { "diplomacy.offerRollDenominator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Diplomacy = r.Diplomacy with { OfferRollDenominator = v } }) };
        yield return new object[] { "diplomacy.thawBonusChanceDenominator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Diplomacy = r.Diplomacy with { ThawBonusChanceDenominator = v } }) };
        yield return new object[] { "diplomacy.aiOwnDiplomacy.allianceRollDenominator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Diplomacy = r.Diplomacy with { AiOwnDiplomacy = r.Diplomacy.AiOwnDiplomacy with { AllianceRollDenominator = v } } }) };
        yield return new object[] { "diplomacy.aiOwnDiplomacy.warDeclareRollDenominator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Diplomacy = r.Diplomacy with { AiOwnDiplomacy = r.Diplomacy.AiOwnDiplomacy with { WarDeclareRollDenominator = v } } }) };
        yield return new object[] { "economy.depositionRandomDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { DepositionRandomDivisor = v } }) };
        yield return new object[] { "economy.loyaltyFallProbabilityDenominator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { LoyaltyFallProbabilityDenominator = v } }) };
        yield return new object[] { "economy.famineLoyaltyLossProbabilityDenominator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { FamineLoyaltyLossProbabilityDenominator = v } }) };
        yield return new object[] { "naval.stormWinterSpikeChanceDenominator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Naval = r.Naval with { StormWinterSpikeChanceDenominator = v } }) };
    }

    private static Ruleset Toy() => GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);

    [Theory]
    [MemberData(nameof(BoundMutations))]
    public void A_bound_of_zero_is_refused_naming_the_key(string key, Func<Ruleset, int, Ruleset> withValue)
    {
        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(Document, withValue(Toy(), 0)));
        Assert.Contains($"'{key}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("random-draw bound", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(BoundMutations))]
    public void A_negative_bound_is_refused_naming_the_key(string key, Func<Ruleset, int, Ruleset> withValue)
    {
        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(Document, withValue(Toy(), -1)));
        Assert.Contains($"'{key}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scatter_minimum_above_the_maximum_is_refused_naming_both_keys()
    {
        var r = Toy();
        var bad = r with { Combat = r.Combat with { ScatteredDefeat = r.Combat.ScatteredDefeat with { ScatterTilesMin = 5, ScatterTilesMax = 4 } } };
        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(Document, bad));
        Assert.Contains("scatterTilesMin", ex.Message, StringComparison.Ordinal);
        Assert.Contains("scatterTilesMax", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scatter_minimum_equal_to_the_maximum_is_accepted()
    {
        var r = Toy();
        GameDataValidation.Validate(Document, r with { Combat = r.Combat with { ScatteredDefeat = r.Combat.ScatteredDefeat with { ScatterTilesMin = 3, ScatterTilesMax = 3 } } });
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(-1, 5)]
    [InlineData(6, 5)]
    public void A_weather_season_with_invalid_odds_is_refused_naming_the_key(int numerator, int denominator)
    {
        var r = Toy();
        var odds = r.Economy.Weather.BySeason[0] with { EarlyNumerator = numerator, EarlyDenominator = denominator };
        var list = new List<WeatherSeasonOdds>(r.Economy.Weather.BySeason) { [0] = odds };
        var bad = r with { Economy = r.Economy with { Weather = r.Economy.Weather with { BySeason = new ValueList<WeatherSeasonOdds>(list) } } };
        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(Document, bad));
        Assert.Contains("economy.weather.bySeason[0].early", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Follow-up #980 item 1: the row above mutates only the early odds; this one mutates only the late odds.</summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(-1, 5)]
    [InlineData(6, 5)]
    public void A_weather_season_with_invalid_late_odds_is_refused_naming_the_key(int numerator, int denominator)
    {
        var r = Toy();
        var odds = r.Economy.Weather.BySeason[0] with { LateNumerator = numerator, LateDenominator = denominator };
        var list = new List<WeatherSeasonOdds>(r.Economy.Weather.BySeason) { [0] = odds };
        var bad = r with { Economy = r.Economy with { Weather = r.Economy.Weather with { BySeason = new ValueList<WeatherSeasonOdds>(list) } } };
        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(Document, bad));
        Assert.Contains("economy.weather.bySeason[0].late", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>An empty effects list is legitimate: WeatherEventSystem returns before any draw.</summary>
    [Fact]
    public void An_empty_weather_effects_list_is_accepted_because_the_system_skips_the_draw()
    {
        var r = Toy();
        GameDataValidation.Validate(Document, r with { Economy = r.Economy with { Weather = r.Economy.Weather with { Effects = new ValueList<WeatherEffectRule>(new List<WeatherEffectRule>()) } } });
    }

    [Theory]
    [InlineData("toy-ruleset.json")]
    [InlineData("improved.json")]
    [InlineData("classical-faithful.json")]
    public void The_shipped_rulesets_still_load(string file)
    {
        var path = Path.Combine(Path.GetDirectoryName(TestPaths.ToyRulesetFile)!, file);
        var ruleset = GameDataLoader.LoadFile<Ruleset>(path);
        GameDataValidation.Validate(path, ruleset);
    }
}
