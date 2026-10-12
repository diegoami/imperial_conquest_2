using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.SerializationTests;

/// <summary>
/// Bug #979: every non-AI ruleset key the engine divides by (<c>/</c> or <c>%</c>) at runtime is refused at
/// load when it is 0 or negative, naming the key, rather than throwing DivideByZeroException mid-game.
/// Also: the span <c>AiFaithfulEconomy.RollTroops</c> draws from must be at least 1 for every recruit type.
/// </summary>
public class RuntimeDivisorValidationTests
{
    private const string Document = "mutated-ruleset.json";

    public static IEnumerable<object[]> DivisorMutations()
    {
        yield return new object[] { "economy.taxRateDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { TaxRateDivisor = v } }) };
        yield return new object[] { "economy.supplyTonsPerTalent", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { SupplyTonsPerTalent = v } }) };
        yield return new object[] { "economy.armySupplyTonsPerTroops", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { ArmySupplyTonsPerTroops = v } }) };
        yield return new object[] { "economy.mercenaryDesertionSupplyDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { MercenaryDesertionSupplyDivisor = v } }) };
        yield return new object[] { "economy.debtWealthDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { DebtWealthDivisor = v } }) };
        yield return new object[] { "economy.treasuryCreditTaxBaseQuarterShareDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { TreasuryCreditTaxBaseQuarterShareDivisor = v } }) };
        yield return new object[] { "economy.treasuryCreditWealthDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { TreasuryCreditWealthDivisor = v } }) };
        yield return new object[] { "economy.tradeIncomeTaxBaseDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { TradeIncomeTaxBaseDivisor = v } }) };
        yield return new object[] { "economy.populationGrowthGapDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { PopulationGrowthGapDivisor = v } }) };
        yield return new object[] { "economy.populationGrowthTaxDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { PopulationGrowthTaxDivisor = v } }) };
        yield return new object[] { "economy.populationGrowthMobilizationDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { PopulationGrowthMobilizationDivisor = v } }) };
        yield return new object[] { "economy.unityTaxRateDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { UnityTaxRateDivisor = v } }) };
        yield return new object[] { "economy.unityMobilizationDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { UnityMobilizationDivisor = v } }) };
        yield return new object[] { "economy.loyaltyFallTaxDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { LoyaltyFallTaxDivisor = v } }) };
        yield return new object[] { "economy.citySupplyProductionDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { CitySupplyProductionDivisor = v } }) };
        yield return new object[] { "economy.citySupplyMobilizationDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { CitySupplyMobilizationDivisor = v } }) };
        yield return new object[] { "economy.supplyPercentNumerator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { SupplyPercentNumerator = v } }) };
        yield return new object[] { "economy.supplyConsumption.consumptionDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { SupplyConsumption = r.Economy.SupplyConsumption with { ConsumptionDivisor = v } } }) };
        yield return new object[] { "economy.supplyConsumption.fleetEmbarkedDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { SupplyConsumption = r.Economy.SupplyConsumption with { FleetEmbarkedDivisor = v } } }) };
        yield return new object[] { "economy.supplyMorale.movesTroopDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Economy = r.Economy with { SupplyMorale = r.Economy.SupplyMorale with { MovesTroopDivisor = v } } }) };
        yield return new object[] { "endTurnWarnings.fleetSupplyShipsDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { EndTurnWarnings = r.EndTurnWarnings with { FleetSupplyShipsDivisor = v } }) };
        yield return new object[] { "recruitment.troopsPerCostUnit", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Recruitment = r.Recruitment with { TroopsPerCostUnit = v } }) };
        yield return new object[] { "recruitment.mercenaryHireTroopDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Recruitment = r.Recruitment with { MercenaryHireTroopDivisor = v } }) };
        yield return new object[] { "recruitment.mercenaryUpkeepQualityDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Recruitment = r.Recruitment with { MercenaryUpkeepQualityDivisor = v } }) };
        yield return new object[] { "recruitment.mercenaryRestockTroopsScaleDenominator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Recruitment = r.Recruitment with { MercenaryRestockTroopsScaleDenominator = v } }) };
        yield return new object[] { "recruitment.mobilizationQualityDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Recruitment = r.Recruitment with { MobilizationQualityDivisor = v } }) };
        yield return new object[] { "naval.repairCostDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Naval = r.Naval with { RepairCostDivisor = v } }) };
        yield return new object[] { "naval.stormDamageRandomDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Naval = r.Naval with { StormDamageRandomDivisor = v } }) };
        yield return new object[] { "naval.stormNearCoastDamageDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Naval = r.Naval with { StormNearCoastDamageDivisor = v } }) };
        yield return new object[] { "naval.stormShipLossRatioScale", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Naval = r.Naval with { StormShipLossRatioScale = v } }) };
        yield return new object[] { "naval.stormShipLossDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Naval = r.Naval with { StormShipLossDivisor = v } }) };
        yield return new object[] { "naval.movesShipDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Naval = r.Naval with { MovesShipDivisor = v } }) };
        yield return new object[] { "naval.movesCarriedArmyTroopDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Naval = r.Naval with { MovesCarriedArmyTroopDivisor = v } }) };
        yield return new object[] { "naval.damageSlowdownDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Naval = r.Naval with { DamageSlowdownDivisor = v } }) };
        yield return new object[] { "combat.powerTroopDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { PowerTroopDivisor = v } }) };
        yield return new object[] { "combat.powerDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { PowerDivisor = v } }) };
        yield return new object[] { "combat.absorbedSupplyTroopDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { AbsorbedSupplyTroopDivisor = v } }) };
        yield return new object[] { "combat.naval.conditionDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { Naval = r.Combat.Naval with { ConditionDivisor = v } } }) };
        yield return new object[] { "combat.naval.carriedArmyPowerDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { Naval = r.Combat.Naval with { CarriedArmyPowerDivisor = v } } }) };
        yield return new object[] { "combat.naval.winnerDamageDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { Naval = r.Combat.Naval with { WinnerDamageDivisor = v } } }) };
        yield return new object[] { "combat.naval.damageRatioScale", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { Naval = r.Combat.Naval with { DamageRatioScale = v } } }) };
        yield return new object[] { "combat.naval.unitLossDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { Naval = r.Combat.Naval with { UnitLossDivisor = v } } }) };
        yield return new object[] { "combat.naval.unitySwingShipDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { Naval = r.Combat.Naval with { UnitySwingShipDivisor = v } } }) };
        yield return new object[] { "combat.detailedResolver.meleePowerDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { DetailedResolver = r.Combat.DetailedResolver with { MeleePowerDivisor = v } } }) };
        yield return new object[] { "combat.tactical.copyInColumns", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { Tactical = r.Combat.Tactical with { CopyInColumns = v } } }) };
        yield return new object[] { "combat.tactical.shotShooterTroopsDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { Tactical = r.Combat.Tactical with { ShotShooterTroopsDivisor = v } } }) };
        yield return new object[] { "combat.tactical.shotTargetTroopsDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { Tactical = r.Combat.Tactical with { ShotTargetTroopsDivisor = v } } }) };
        yield return new object[] { "combat.tactical.meleeAttackerExchangeDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { Tactical = r.Combat.Tactical with { MeleeAttackerExchangeDivisor = v } } }) };
        yield return new object[] { "combat.tactical.meleeDefenderExchangeDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { Tactical = r.Combat.Tactical with { MeleeDefenderExchangeDivisor = v } } }) };
        yield return new object[] { "combat.tactical.routTroopsDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { Tactical = r.Combat.Tactical with { RoutTroopsDivisor = v } } }) };
        yield return new object[] { "combat.tactical.scoreDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { Tactical = r.Combat.Tactical with { ScoreDivisor = v } } }) };
        yield return new object[] { "combat.tactical.dangerQualityDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { Tactical = r.Combat.Tactical with { DangerQualityDivisor = v } } }) };
        yield return new object[] { "siege.powerDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Siege = r.Siege with { PowerDivisor = v } }) };
        yield return new object[] { "siege.highLoyaltyBonusDenominator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Siege = r.Siege with { HighLoyaltyBonusDenominator = v } }) };
        yield return new object[] { "siege.defenderGarrisonTroopDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Siege = r.Siege with { DefenderGarrisonTroopDivisor = v } }) };
        yield return new object[] { "siege.defenderNonAllegiantDenominator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Siege = r.Siege with { DefenderNonAllegiantDenominator = v } }) };
        yield return new object[] { "capture.cascadeAllegiantDefenseDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Capture = r.Capture with { CascadeAllegiantDefenseDivisor = v } }) };
        yield return new object[] { "capture.capitalMoveStrengthDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Capture = r.Capture with { CapitalMoveStrengthDivisor = v } }) };
        yield return new object[] { "diplomacy.reparationsWealthDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Diplomacy = r.Diplomacy with { ReparationsWealthDivisor = v } }) };
        yield return new object[] { "diplomacy.aiOwnDiplomacy.powerWealthDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Diplomacy = r.Diplomacy with { AiOwnDiplomacy = r.Diplomacy.AiOwnDiplomacy with { PowerWealthDivisor = v } } }) };
        yield return new object[] { "diplomacy.aiOwnDiplomacy.powerUnityDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Diplomacy = r.Diplomacy with { AiOwnDiplomacy = r.Diplomacy.AiOwnDiplomacy with { PowerUnityDivisor = v } } }) };
    }

    private static Ruleset Toy() => GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);

    [Theory]
    [MemberData(nameof(DivisorMutations))]
    public void A_divisor_of_zero_is_refused_naming_the_key(string key, Func<Ruleset, int, Ruleset> withValue)
    {
        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(Document, withValue(Toy(), 0)));
        Assert.Contains($"'{key}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("runtime divisor", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(DivisorMutations))]
    public void A_negative_divisor_is_refused_naming_the_key(string key, Func<Ruleset, int, Ruleset> withValue)
    {
        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(Document, withValue(Toy(), -1)));
        Assert.Contains($"'{key}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_order_with_a_zero_encoding_radix_is_refused_naming_the_key()
    {
        var r = Toy();
        var orders = new List<CityOrderRule>(r.CityOrders.Orders);
        orders[0] = orders[0] with { InProgressEncodingRadix = 0 };
        var bad = r with { CityOrders = r.CityOrders with { Orders = new ValueList<CityOrderRule>(orders) } };
        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(Document, bad));
        Assert.Contains("cityOrders.orders[0].inProgressEncodingRadix", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_recruit_amount_span_of_zero_for_a_bucket_type_is_refused_naming_both_keys()
    {
        var r = Toy();
        var typeId = r.Ai.FaithfulRecruitTypeBuckets[0].UnitTypeId;
        var types = new List<UnitTypeRules>(r.UnitTypes);
        var i = types.FindIndex(t => t.Id == typeId);
        types[i] = types[i] with { StandardBattalionSize = 1 };
        var bad = r with
        {
            UnitTypes = new ValueList<UnitTypeRules>(types),
            Ai = r.Ai with { FaithfulAmountSpanNumerator = 1, FaithfulAmountSpanDenominator = 2 },
        };
        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(Document, bad));
        Assert.Contains("ai.faithfulAmountSpanNumerator", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ai.faithfulAmountSpanDenominator", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"'{typeId}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_recruit_amount_span_of_one_is_accepted()
    {
        var r = Toy();
        var typeId = r.Ai.FaithfulRecruitTypeBuckets[0].UnitTypeId;
        var types = new List<UnitTypeRules>(r.UnitTypes);
        var i = types.FindIndex(t => t.Id == typeId);
        types[i] = types[i] with { StandardBattalionSize = 2 };
        GameDataValidation.Validate(
            Document,
            r with { UnitTypes = new ValueList<UnitTypeRules>(types), Ai = r.Ai with { FaithfulAmountSpanNumerator = 1, FaithfulAmountSpanDenominator = 2 } });
    }
}
