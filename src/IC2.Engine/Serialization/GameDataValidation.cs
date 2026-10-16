using IC2.Engine.Model;

namespace IC2.Engine.Serialization;

/// <summary>
/// The semantic checks a document has to pass once it has parsed and matched the schema: ids are
/// unique, references inside the document resolve, and the encodings that carry meaning (the terrain
/// grid's length, the relation matrix's symmetry) actually hold.
/// </summary>
/// <remarks>
/// These are reported as <see cref="MalformedGameDataException"/>: the file is syntactically fine and
/// structurally complete, but it does not describe a usable world/ruleset/scenario/save. Catching them
/// here means no later task has to defend against, say, a non-square relation matrix.
/// </remarks>
public static class GameDataValidation
{
    /// <summary>Runs the checks appropriate to whichever document kind was loaded.</summary>
    public static void Validate(string documentPath, IVersionedDocument document)
    {
        switch (document)
        {
            case World world:
                ValidateWorld(documentPath, world);
                break;
            case Ruleset ruleset:
                ValidateRuleset(documentPath, ruleset);
                break;
            case Scenario scenario:
                ValidateScenario(documentPath, scenario);
                break;
            case SaveGame save:
                ValidateSave(documentPath, save);
                break;
            case GameState state:
                ValidateState(documentPath, state);
                break;
            default:
                break;
        }
    }

    private static void ValidateWorld(string documentPath, World world)
    {
        if (world.Width <= 0 || world.Height <= 0)
        {
            throw new MalformedGameDataException(
                documentPath, $"a world's map must have a positive size; got {world.Width}×{world.Height}.");
        }

        RequireDistinct(documentPath, world.TileTypes, t => t.Id, "tileTypes");
        RequireDistinct(documentPath, world.Nations, n => n.Id, "nations");
        RequireDistinct(documentPath, world.Cities, c => c.Id, "cities");
        RequireDistinct(documentPath, world.StartingArmies, a => a.Id, "startingArmies");
        RequireDistinct(documentPath, world.StartingFleets, f => f.Id, "startingFleets");

        var codes = new HashSet<int>();
        foreach (var tileType in world.TileTypes)
        {
            if (!codes.Add(tileType.Code))
            {
                throw new MalformedGameDataException(
                    documentPath, $"two tile types share cell code {tileType.Code}.");
            }
        }

        int[] cells;
        try
        {
            cells = world.Terrain.Decode(world.Width, world.Height);
        }
        catch (InvalidOperationException ex)
        {
            throw new MalformedGameDataException(documentPath, ex.Message, ex);
        }

        foreach (var cell in cells)
        {
            if (world.TileTypeByCode(cell) is null)
            {
                throw new MalformedGameDataException(
                    documentPath, $"the terrain grid uses cell code {cell}, which no tile type defines.");
            }
        }

        foreach (var nation in world.Nations)
        {
            if (nation.CapitalCityId is { } capital && world.CityById(capital) is null)
            {
                throw new UnresolvedReferenceException(documentPath, "city", capital);
            }

            // T146: a nation's leader pool, when present, must be a drawable 12-name table. The 12 is
            // the DAT pool's own fixed width -- DatLayout.LeaderPoolNamesPerNation (16 x 12 x 26,
            // docs/investigations/dat-file-layout.md's read-order row) -- a structural fact of the
            // exported pool rather than a ruleset-tunable rule, so it is a local constant here rather
            // than a Ruleset field. DatLayout is internal to IC2.Data, so this namespace cannot
            // reference the constant directly; it is restated here against that cited source.
            // T161: a nation's starting recruitment queue. Its target city must exist, and a slot holds
            // troops (the DAT's empty slot, troops 0, is not exported). The unit type and the state
            // code's upper bound are ruleset data, so GameStateFactory checks those.
            if (nation.RecruitmentSlots is { } recruitmentSlots)
            {
                for (var i = 0; i < recruitmentSlots.Count; i++)
                {
                    var slot = recruitmentSlots[i];
                    if (world.CityById(slot.TargetCityId) is null)
                    {
                        throw new UnresolvedReferenceException(
                            documentPath, $"city (nation '{nation.Id}' recruitmentSlots[{i}].targetCityId)", slot.TargetCityId);
                    }

                    if (slot.Troops <= 0)
                    {
                        throw new MalformedGameDataException(
                            documentPath, $"nation '{nation.Id}' recruitmentSlots[{i}].troops is {slot.Troops}; a slot holds at least one troop.");
                    }

                    if (slot.StateCode < 0)
                    {
                        throw new MalformedGameDataException(
                            documentPath, $"nation '{nation.Id}' recruitmentSlots[{i}].stateCode is {slot.StateCode}; it cannot be negative.");
                    }
                }
            }

            if (nation.LeaderNames is { } leaderNames)
            {
                const int leaderNamePoolSize = 12;
                if (leaderNames.Count != leaderNamePoolSize)
                {
                    throw new MalformedGameDataException(
                        documentPath,
                        $"nation '{nation.Id}' has {leaderNames.Count} leaderNames; a leader pool has exactly {leaderNamePoolSize}.");
                }

                var distinct = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < leaderNames.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(leaderNames[i]))
                    {
                        throw new MalformedGameDataException(
                            documentPath, $"nation '{nation.Id}' has an empty leader name at index {i}.");
                    }

                    distinct.Add(leaderNames[i]);
                }

                if (distinct.Count < 2)
                {
                    throw new MalformedGameDataException(
                        documentPath,
                        $"nation '{nation.Id}' has 12 identical leader names; a fall could never draw a different one.");
                }

                if (!distinct.Contains(nation.LeaderName))
                {
                    throw new MalformedGameDataException(
                        documentPath,
                        $"nation '{nation.Id}' leaderName '{nation.LeaderName}' is not one of its leaderNames pool.");
                }
            }
        }

        foreach (var city in world.Cities)
        {
            RequireNation(documentPath, world, city.Owner);
            RequireNation(documentPath, world, city.Allegiance);
            RequireOnMap(documentPath, world, city.X, city.Y, $"city '{city.Id}'");
        }

        foreach (var army in world.StartingArmies)
        {
            RequireNation(documentPath, world, army.Nation);
            RequireOnMap(documentPath, world, army.X, army.Y, $"army '{army.Id}'");
        }

        foreach (var fleet in world.StartingFleets)
        {
            RequireNation(documentPath, world, fleet.Nation);
            RequireOnMap(documentPath, world, fleet.X, fleet.Y, $"fleet '{fleet.Id}'");
        }

        if (world.WeatherCentres is { } weatherCentres)
        {
            for (var i = 0; i < weatherCentres.Count; i++)
            {
                RequireOnMap(documentPath, world, weatherCentres[i].X, weatherCentres[i].Y, $"weather centre {i}");
            }
        }

        if (world.TurnOrder.Count != world.Nations.Count)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"turnOrder lists {world.TurnOrder.Count} nations but the world defines {world.Nations.Count}.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nationId in world.TurnOrder)
        {
            RequireNation(documentPath, world, nationId);
            if (!seen.Add(nationId))
            {
                throw new MalformedGameDataException(documentPath, $"turnOrder lists '{nationId}' twice.");
            }
        }

        // T75 (rework, Owns amendment): the load-time half of Done-when 2. Every matrix nation id is
        // checked here, the same RequireNation call every other cross-reference in this method already
        // uses -- so an unknown id surfaces as UnresolvedReferenceException, exactly as it would for a
        // city's owner or an army's nation, and World.ValidateStartingRelationsShape (called next) never
        // has to raise that case itself: by the time it runs, every id it sees is already a real nation.
        // The ruleset-dependent half (a value outside the relation codes/cooldown range, a news text over
        // the message length or holding a non-printable byte) is GameStateFactory's own check, made only
        // once a ruleset is actually available -- this method has none.
        if (world.StartingRelations is { } startingRelations)
        {
            foreach (var nationId in startingRelations.NationIds)
            {
                RequireNation(documentPath, world, nationId);
            }

            try
            {
                world.ValidateStartingRelationsShape();
            }
            catch (InvalidOperationException ex)
            {
                throw new MalformedGameDataException(documentPath, ex.Message, ex);
            }
        }

        if (world.StartingNews is not null)
        {
            try
            {
                world.ValidateStartingNewsShape();
            }
            catch (InvalidOperationException ex)
            {
                throw new MalformedGameDataException(documentPath, ex.Message, ex);
            }
        }

        // T85 Done-when 2: the same pattern as startingRelations above -- every nation id
        // startingNeighbours names (both the entry's own NationId and each of its NeighbourIds) is
        // checked here first, so an unknown id surfaces as UnresolvedReferenceException exactly as it
        // would for a city's owner or an army's nation, and World.ValidateStartingNeighboursShape
        // (called next) never has to raise that case itself.
        if (world.StartingNeighbours is { } startingNeighbours)
        {
            foreach (var entry in startingNeighbours)
            {
                RequireNation(documentPath, world, entry.NationId);
                foreach (var neighbourId in entry.NeighbourIds)
                {
                    RequireNation(documentPath, world, neighbourId);
                }
            }

            try
            {
                world.ValidateStartingNeighboursShape();
            }
            catch (InvalidOperationException ex)
            {
                throw new MalformedGameDataException(documentPath, ex.Message, ex);
            }
        }
    }

    private static void ValidateRuleset(string documentPath, Ruleset ruleset)
    {
        ValidateCalendar(documentPath, ruleset.Calendar);
        ValidateNewsLogSeasonNames(documentPath, ruleset);
        RequireDistinct(documentPath, ruleset.UnitTypes, u => u.Id, "unitTypes");
        RequireDistinct(documentPath, ruleset.Terrain.MoveCosts, m => m.TileTypeId, "terrain.moveCosts");
        RequireDistinct(documentPath, ruleset.CityOrders.Orders, o => o.Id, "cityOrders.orders");

        var matrix = ruleset.Combat.DetailedResolver.TypeEffectiveness;
        var order = ruleset.Combat.DetailedResolver.TypeEffectivenessOrder;
        if (matrix.Count != order.Count)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"combat.detailedResolver.typeEffectiveness has {matrix.Count} rows but its order lists {order.Count} types.");
        }

        foreach (var row in matrix)
        {
            if (row.Count != order.Count)
            {
                throw new MalformedGameDataException(
                    documentPath, "combat.detailedResolver.typeEffectiveness must be square.");
            }
        }

        foreach (var typeId in order)
        {
            if (ruleset.UnitTypeById(typeId) is null)
            {
                throw new UnresolvedReferenceException(documentPath, "unit type", typeId);
            }
        }

        // N5 (T63 review round 1): every one of these divides a live value at runtime -- a besieger's
        // casualties (deletionDivisor*), the population floor and erosion terms (siege.*), and a heavy
        // storm's whole-unit loss (naval.stormUnitLossDivisor). A 0 here is not a bad game balance
        // number, it is a DivideByZeroException the first time the call site runs, so this is a load
        // time check rather than leaving it to crash mid-turn.
        RequirePositiveDivisor(documentPath, ruleset.Combat.DeletionDivisorNational, "combat.deletionDivisorNational");
        RequirePositiveDivisor(documentPath, ruleset.Combat.DeletionDivisorMercenary, "combat.deletionDivisorMercenary");
        RequirePositiveDivisor(documentPath, ruleset.Siege.ErosionFloorDenominator, "siege.erosionFloorDenominator");
        RequirePositiveDivisor(documentPath, ruleset.Siege.ErosionCeilingDenominator, "siege.erosionCeilingDenominator");
        RequirePositiveDivisor(documentPath, ruleset.Siege.PopulationFloorDivisor, "siege.populationFloorDivisor");
        RequirePositiveDivisor(documentPath, ruleset.Naval.StormUnitLossDivisor, "naval.stormUnitLossDivisor");

        // N4 (T79 rework round 1, PR #429 review round 0): ai.permilleScale is a divisor too --
        // AiEconomyPhase.TurnBudget divides by it directly, and it is also the denominator in the recruit
        // and fortify scores, AiPersonalityProfile.ToPermille, AiView.RequiredAttackRatioPermille and
        // AiView.WithVictoryAwareness. A 0 here is a DivideByZeroException the first AI turn runs, exactly
        // the hazard N5 (T63) closed for the divisors above.
        RequirePositiveDivisor(documentPath, ruleset.Ai.PermilleScale, "ai.permilleScale");

        // Bug #943: the same hazard for the other AI keys the engine divides by; each comment names where.
        // ai.cityScoreRegionHalvingDenominator: AiArmyTargetTree: score / this (via the local copy `halving`), the city and army scorers (two sites).
        RequirePositiveDivisor(documentPath, ruleset.Ai.CityScoreRegionHalvingDenominator, "ai.cityScoreRegionHalvingDenominator");
        // ai.cityAttackCapitalDefenseRatioDenominator: AiArmyTargetTree: city defense * numerator / this (via the local copy `capitalDen`), the capital test.
        RequirePositiveDivisor(documentPath, ruleset.Ai.CityAttackCapitalDefenseRatioDenominator, "ai.cityAttackCapitalDefenseRatioDenominator");
        // ai.resupplyStrengthTroopsDivisor: AiArmyTargetTree: army troops / this, in the resupply target tree (two sites).
        RequirePositiveDivisor(documentPath, ruleset.Ai.ResupplyStrengthTroopsDivisor, "ai.resupplyStrengthTroopsDivisor");
        // ai.resupplyForeignMoneyDivisor: AiArmyTargetTree: strength / this, the foreign-money test (two sites).
        RequirePositiveDivisor(documentPath, ruleset.Ai.ResupplyForeignMoneyDivisor, "ai.resupplyForeignMoneyDivisor");
        // ai.mercenaryRunTroopsDivisor: AiArmyTargetTree: army troops / this, the mercenary run's well-supplied test.
        RequirePositiveDivisor(documentPath, ruleset.Ai.MercenaryRunTroopsDivisor, "ai.mercenaryRunTroopsDivisor");
        RequirePositiveDivisor(documentPath, ruleset.Ai.FaithfulMobilizeArmyDistanceBelow, "ai.faithfulMobilizeArmyDistanceBelow");
        RequirePositiveDivisor(documentPath, ruleset.Ai.FaithfulMobilizeReadyCountAtLeast, "ai.faithfulMobilizeReadyCountAtLeast");
        // ai.faithfulValueTroopsDivisor: AiFaithfulEconomy: unit troops / this, in the army value sum.
        RequirePositiveDivisor(documentPath, ruleset.Ai.FaithfulValueTroopsDivisor, "ai.faithfulValueTroopsDivisor");
        // ai.faithfulForeignNearDivisor: AiFaithfulEconomy: foreign near count / this, in the threat sum.
        RequirePositiveDivisor(documentPath, ruleset.Ai.FaithfulForeignNearDivisor, "ai.faithfulForeignNearDivisor");
        // ai.faithfulWarNearDivisor: AiFaithfulEconomy: war near count / this, in the threat sum.
        RequirePositiveDivisor(documentPath, ruleset.Ai.FaithfulWarNearDivisor, "ai.faithfulWarNearDivisor");
        // ai.faithfulAlliedArmyDivisor: AiFaithfulEconomy: allied army value / this, in the ally strength sum.
        RequirePositiveDivisor(documentPath, ruleset.Ai.FaithfulAlliedArmyDivisor, "ai.faithfulAlliedArmyDivisor");
        // ai.faithfulDeficitFloorWealthDivisor: AiFaithfulEconomy: nation wealth / this, the deficit floor.
        RequirePositiveDivisor(documentPath, ruleset.Ai.FaithfulDeficitFloorWealthDivisor, "ai.faithfulDeficitFloorWealthDivisor");
        // ai.faithfulAmountSpanDenominator: AiFaithfulEconomy: span = size * numerator / this, the order amount.
        RequirePositiveDivisor(documentPath, ruleset.Ai.FaithfulAmountSpanDenominator, "ai.faithfulAmountSpanDenominator");
        // ai.faithfulAmountMinDivisor: AiFaithfulEconomy: size / this, the order amount's minimum.
        RequirePositiveDivisor(documentPath, ruleset.Ai.FaithfulAmountMinDivisor, "ai.faithfulAmountMinDivisor");
        // ai.faithfulAmountRounding: AiFaithfulEconomy: raw / this * this, the order amount's rounding.
        RequirePositiveDivisor(documentPath, ruleset.Ai.FaithfulAmountRounding, "ai.faithfulAmountRounding");
        // ai.faithfulTaxCutTreasuryWealthDivisor: AiFaithfulEconomy: nation wealth / this, the tax-cut treasury test.
        RequirePositiveDivisor(documentPath, ruleset.Ai.FaithfulTaxCutTreasuryWealthDivisor, "ai.faithfulTaxCutTreasuryWealthDivisor");

        // combat.tactical.midBattleSave selects both the save gate and reload path; an unknown value must not
        // silently fall back to the unrestricted policy.
        if (ruleset.Combat.Tactical.MidBattleSave is not ("anyTime" or "halfRoundStart"))
        {
            throw new MalformedGameDataException(
                documentPath, "'combat.tactical.midBattleSave' must be 'anyTime' or 'halfRoundStart'.");
        }

        ValidateRuntimeDivisors(documentPath, ruleset);
        ValidateBalanceSheetDebtLimits(documentPath, ruleset);
        ValidateRngBounds(documentPath, ruleset);
        ValidateRollTroopsSpan(documentPath, ruleset);
    }

    /// <summary>
    /// Bug #1054: the <c>economy.balanceSheetDebtLimit*</c> keys round the Balance sheet's debt limit down to a
    /// step (<c>limit / step * step</c>) under <c>flags.balanceSheetTotals: original</c>. A step of 0 or less
    /// would divide by zero (or flip the sign) when the sheet is built, and the bands must ascend
    /// (0 &lt; lowMax &lt; midMax) for the three steps to apply in order, so each is refused at load.
    /// </summary>
    private static void ValidateBalanceSheetDebtLimits(string documentPath, Ruleset ruleset)
    {
        var e = ruleset.Economy;
        RequirePositiveDivisor(documentPath, e.BalanceSheetDebtLimitLowStep, "economy.balanceSheetDebtLimitLowStep");
        RequirePositiveDivisor(documentPath, e.BalanceSheetDebtLimitMidStep, "economy.balanceSheetDebtLimitMidStep");
        RequirePositiveDivisor(documentPath, e.BalanceSheetDebtLimitHighStep, "economy.balanceSheetDebtLimitHighStep");
        if (e.BalanceSheetDebtLimitLowMax <= 0)
        {
            throw new MalformedGameDataException(
                documentPath, $"'economy.balanceSheetDebtLimitLowMax' must be greater than 0; got {e.BalanceSheetDebtLimitLowMax}.");
        }

        if (e.BalanceSheetDebtLimitLowMax >= e.BalanceSheetDebtLimitMidMax)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"'economy.balanceSheetDebtLimitLowMax' ({e.BalanceSheetDebtLimitLowMax}) must be less than 'economy.balanceSheetDebtLimitMidMax' ({e.BalanceSheetDebtLimitMidMax}).");
        }
    }

    /// <summary>
    /// Bug #979: the non-AI ruleset keys the engine divides by (<c>/</c> or <c>%</c>) at runtime. A 0 or
    /// negative value is a <see cref="DivideByZeroException"/> (or a sign flip) mid-game, so it is refused
    /// at load, exactly as the divisors above and the AI divisors of bug #943 are. Each comment names its
    /// use site. Random-draw bounds are checked in <see cref="ValidateRngBounds"/> instead.
    /// </summary>
    private static void ValidateRuntimeDivisors(string documentPath, Ruleset ruleset)
    {
        // economy.taxRateDivisor: TaxIncome: taxBase * rate / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.TaxRateDivisor, "economy.taxRateDivisor");
        // economy.supplyTonsPerTalent: AutomaticResupply, SupplyPurchase: tons / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.SupplyTonsPerTalent, "economy.supplyTonsPerTalent");
        // economy.armySupplyTonsPerTroops: SupplyCapacity, InstantBattleResolver: troops / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.ArmySupplyTonsPerTroops, "economy.armySupplyTonsPerTroops");
        // economy.mercenaryDesertionSupplyDivisor: MercenaryDesertion: troops / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.MercenaryDesertionSupplyDivisor, "economy.mercenaryDesertionSupplyDivisor");
        // economy.debtWealthDivisor: the debt test: wealth / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.DebtWealthDivisor, "economy.debtWealthDivisor");
        // economy.treasuryCreditTaxBaseQuarterShareDivisor: NationTreasuryCredit: taxBase / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.TreasuryCreditTaxBaseQuarterShareDivisor, "economy.treasuryCreditTaxBaseQuarterShareDivisor");
        // economy.treasuryCreditWealthDivisor: NationTreasuryCredit: wealth / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.TreasuryCreditWealthDivisor, "economy.treasuryCreditWealthDivisor");
        // economy.tradeIncomeTaxBaseDivisor: NationTreasuryCredit: partner taxBase / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.TradeIncomeTaxBaseDivisor, "economy.tradeIncomeTaxBaseDivisor");
        // economy.populationGrowthGapDivisor: CityPopulationGrowth: gap / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.PopulationGrowthGapDivisor, "economy.populationGrowthGapDivisor");
        // economy.populationGrowthTaxDivisor: CityPopulationGrowth: d * tax / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.PopulationGrowthTaxDivisor, "economy.populationGrowthTaxDivisor");
        // economy.populationGrowthMobilizationDivisor: CityPopulationGrowth: mobilization / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.PopulationGrowthMobilizationDivisor, "economy.populationGrowthMobilizationDivisor");
        // economy.unityTaxRateDivisor: the unity update: tax rate / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.UnityTaxRateDivisor, "economy.unityTaxRateDivisor");
        // economy.unityMobilizationDivisor: the unity update: mobilization / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.UnityMobilizationDivisor, "economy.unityMobilizationDivisor");
        // economy.loyaltyFallTaxDivisor: CityLoyaltyDraws: NextInt(taxRate) / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.LoyaltyFallTaxDivisor, "economy.loyaltyFallTaxDivisor");
        // economy.citySupplyProductionDivisor: city supply: production / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.CitySupplyProductionDivisor, "economy.citySupplyProductionDivisor");
        // economy.citySupplyMobilizationDivisor: city supply: mobilization / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.CitySupplyMobilizationDivisor, "economy.citySupplyMobilizationDivisor");
        // economy.supplyPercentNumerator: SupplyCapacity, EndTurnWarnings: troops / this (a percent scale used as a divisor).
        RequirePositiveDivisor(documentPath, ruleset.Economy.SupplyPercentNumerator, "economy.supplyPercentNumerator");
        // economy.supplyConsumption.consumptionDivisor: SupplyConsumption: x * troops / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.SupplyConsumption.ConsumptionDivisor, "economy.supplyConsumption.consumptionDivisor");
        // economy.supplyConsumption.fleetEmbarkedDivisor: SupplyConsumption: embarked troops / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.SupplyConsumption.FleetEmbarkedDivisor, "economy.supplyConsumption.fleetEmbarkedDivisor");
        // economy.supplyMorale.movesTroopDivisor: SupplyMoraleRule: troops / this.
        RequirePositiveDivisor(documentPath, ruleset.Economy.SupplyMorale.MovesTroopDivisor, "economy.supplyMorale.movesTroopDivisor");
        // endTurnWarnings.fleetSupplyShipsDivisor: EndTurnWarnings: ships / this.
        RequirePositiveDivisor(documentPath, ruleset.EndTurnWarnings.FleetSupplyShipsDivisor, "endTurnWarnings.fleetSupplyShipsDivisor");
        // recruitment.troopsPerCostUnit: StandingRecruitmentCost, ArmyUpkeep: troops / this.
        RequirePositiveDivisor(documentPath, ruleset.Recruitment.TroopsPerCostUnit, "recruitment.troopsPerCostUnit");
        // recruitment.mercenaryHireTroopDivisor: MercenaryHireCost: troops * price / this.
        RequirePositiveDivisor(documentPath, ruleset.Recruitment.MercenaryHireTroopDivisor, "recruitment.mercenaryHireTroopDivisor");
        // recruitment.mercenaryUpkeepQualityDivisor: ArmyUpkeep: cost * quality / this.
        RequirePositiveDivisor(documentPath, ruleset.Recruitment.MercenaryUpkeepQualityDivisor, "recruitment.mercenaryUpkeepQualityDivisor");
        // recruitment.mercenaryRestockTroopsScaleDenominator: mercenary restock: troops scale / this.
        RequirePositiveDivisor(documentPath, ruleset.Recruitment.MercenaryRestockTroopsScaleDenominator, "recruitment.mercenaryRestockTroopsScaleDenominator");
        // recruitment.mobilizationQualityDivisor: MobilizationReadiness: state code / this.
        RequirePositiveDivisor(documentPath, ruleset.Recruitment.MobilizationQualityDivisor, "recruitment.mobilizationQualityDivisor");
        // naval.repairCostDivisor: RepairFleetCommandHandler: ships * points / this.
        RequirePositiveDivisor(documentPath, ruleset.Naval.RepairCostDivisor, "naval.repairCostDivisor");
        // naval.stormDamageRandomDivisor: FleetAttritionRule: raw storm draw / this.
        RequirePositiveDivisor(documentPath, ruleset.Naval.StormDamageRandomDivisor, "naval.stormDamageRandomDivisor");
        // naval.stormNearCoastDamageDivisor: FleetAttritionRule: damage /= this.
        RequirePositiveDivisor(documentPath, ruleset.Naval.StormNearCoastDamageDivisor, "naval.stormNearCoastDamageDivisor");
        // naval.stormShipLossRatioScale: FleetAttritionRule: ratio^2 / this.
        RequirePositiveDivisor(documentPath, ruleset.Naval.StormShipLossRatioScale, "naval.stormShipLossRatioScale");
        // naval.stormShipLossDivisor: FleetAttritionRule: ships * damage / this.
        RequirePositiveDivisor(documentPath, ruleset.Naval.StormShipLossDivisor, "naval.stormShipLossDivisor");
        // naval.movesShipDivisor: FleetAttritionRule: ships / this.
        RequirePositiveDivisor(documentPath, ruleset.Naval.MovesShipDivisor, "naval.movesShipDivisor");
        // naval.movesCarriedArmyTroopDivisor: FleetAttritionRule: troops / this.
        RequirePositiveDivisor(documentPath, ruleset.Naval.MovesCarriedArmyTroopDivisor, "naval.movesCarriedArmyTroopDivisor");
        // naval.damageSlowdownDivisor: FleetAttritionRule: condition gap / this.
        RequirePositiveDivisor(documentPath, ruleset.Naval.DamageSlowdownDivisor, "naval.damageSlowdownDivisor");
        // combat.powerTroopDivisor: ArmyPower: weight * troops / this.
        RequirePositiveDivisor(documentPath, ruleset.Combat.PowerTroopDivisor, "combat.powerTroopDivisor");
        // combat.powerDivisor: ArmyPower, SiegeStrength: total / this.
        RequirePositiveDivisor(documentPath, ruleset.Combat.PowerDivisor, "combat.powerDivisor");
        // combat.absorbedSupplyTroopDivisor: TacticalWriteBack: troops / this.
        RequirePositiveDivisor(documentPath, ruleset.Combat.AbsorbedSupplyTroopDivisor, "combat.absorbedSupplyTroopDivisor");
        // combat.naval.conditionDivisor: FleetPower, AttackLegality: ships * condition / this.
        RequirePositiveDivisor(documentPath, ruleset.Combat.Naval.ConditionDivisor, "combat.naval.conditionDivisor");
        // combat.naval.carriedArmyPowerDivisor: FleetPower, AttackLegality: siege strength / this.
        RequirePositiveDivisor(documentPath, ruleset.Combat.Naval.CarriedArmyPowerDivisor, "combat.naval.carriedArmyPowerDivisor");
        // combat.naval.winnerDamageDivisor: InstantBattleResolver: winner ships * damage / this.
        RequirePositiveDivisor(documentPath, ruleset.Combat.Naval.WinnerDamageDivisor, "combat.naval.winnerDamageDivisor");
        // combat.naval.damageRatioScale: InstantBattleResolver: ratio^2 / this.
        RequirePositiveDivisor(documentPath, ruleset.Combat.Naval.DamageRatioScale, "combat.naval.damageRatioScale");
        // combat.naval.unitLossDivisor: InstantBattleResolver: the carried-unit loss divisor.
        RequirePositiveDivisor(documentPath, ruleset.Combat.Naval.UnitLossDivisor, "combat.naval.unitLossDivisor");
        // combat.naval.unitySwingShipDivisor: InstantBattleResolver: ships / this.
        RequirePositiveDivisor(documentPath, ruleset.Combat.Naval.UnitySwingShipDivisor, "combat.naval.unitySwingShipDivisor");
        // combat.detailedResolver.meleePowerDivisor: TacticalMelee, candidates: x / this.
        RequirePositiveDivisor(documentPath, ruleset.Combat.DetailedResolver.MeleePowerDivisor, "combat.detailedResolver.meleePowerDivisor");
        // combat.tactical.copyInColumns: TacticalCopyIn: slot % this and slot / this.
        RequirePositiveDivisor(documentPath, ruleset.Combat.Tactical.CopyInColumns, "combat.tactical.copyInColumns");
        // combat.tactical.shotShooterTroopsDivisor: TacticalShooting: shooter troops / this.
        RequirePositiveDivisor(documentPath, ruleset.Combat.Tactical.ShotShooterTroopsDivisor, "combat.tactical.shotShooterTroopsDivisor");
        // combat.tactical.shotTargetTroopsDivisor: TacticalShooting: target troops / this.
        RequirePositiveDivisor(documentPath, ruleset.Combat.Tactical.ShotTargetTroopsDivisor, "combat.tactical.shotTargetTroopsDivisor");
        // combat.tactical.meleeAttackerExchangeDivisor: TacticalMelee: x / this.
        RequirePositiveDivisor(documentPath, ruleset.Combat.Tactical.MeleeAttackerExchangeDivisor, "combat.tactical.meleeAttackerExchangeDivisor");
        // combat.tactical.meleeDefenderExchangeDivisor: TacticalMelee: x / this.
        RequirePositiveDivisor(documentPath, ruleset.Combat.Tactical.MeleeDefenderExchangeDivisor, "combat.tactical.meleeDefenderExchangeDivisor");
        // combat.tactical.routTroopsDivisor: TacticalRout: standard size / this.
        RequirePositiveDivisor(documentPath, ruleset.Combat.Tactical.RoutTroopsDivisor, "combat.tactical.routTroopsDivisor");
        // combat.tactical.scoreDivisor: GeneralTargeting: score / this.
        RequirePositiveDivisor(documentPath, ruleset.Combat.Tactical.ScoreDivisor, "combat.tactical.scoreDivisor");
        // combat.tactical.dangerQualityDivisor: TacticalMovement: troops * quality / this.
        RequirePositiveDivisor(documentPath, ruleset.Combat.Tactical.DangerQualityDivisor, "combat.tactical.dangerQualityDivisor");
        // siege.powerDivisor: SiegeStrength: total / this.
        RequirePositiveDivisor(documentPath, ruleset.Siege.PowerDivisor, "siege.powerDivisor");
        // siege.highLoyaltyBonusDenominator: SiegeStrength: strength * num / this.
        RequirePositiveDivisor(documentPath, ruleset.Siege.HighLoyaltyBonusDenominator, "siege.highLoyaltyBonusDenominator");
        // siege.defenderGarrisonTroopDivisor: CompleteDefenderStrength, InstantBattleResolver: slot troops / this.
        RequirePositiveDivisor(documentPath, ruleset.Siege.DefenderGarrisonTroopDivisor, "siege.defenderGarrisonTroopDivisor");
        // siege.defenderNonAllegiantDenominator: SiegeStrength: strength * num / this.
        RequirePositiveDivisor(documentPath, ruleset.Siege.DefenderNonAllegiantDenominator, "siege.defenderNonAllegiantDenominator");
        // capture.cascadeAllegiantDefenseDivisor: CityCaptureResolver: defense /= this.
        RequirePositiveDivisor(documentPath, ruleset.Capture.CascadeAllegiantDefenseDivisor, "capture.cascadeAllegiantDefenseDivisor");
        // capture.capitalMoveStrengthDivisor: ConquestTrigger: strength / this.
        RequirePositiveDivisor(documentPath, ruleset.Capture.CapitalMoveStrengthDivisor, "capture.capitalMoveStrengthDivisor");
        // diplomacy.reparationsWealthDivisor: ReparationsFormula: taxBase / this.
        RequirePositiveDivisor(documentPath, ruleset.Diplomacy.ReparationsWealthDivisor, "diplomacy.reparationsWealthDivisor");
        // diplomacy.aiOwnDiplomacy.powerWealthDivisor: AiOwnDiplomacyRule: wealth / this.
        RequirePositiveDivisor(documentPath, ruleset.Diplomacy.AiOwnDiplomacy.PowerWealthDivisor, "diplomacy.aiOwnDiplomacy.powerWealthDivisor");
        // diplomacy.aiOwnDiplomacy.powerUnityDivisor: AiOwnDiplomacyRule: unity / this.
        RequirePositiveDivisor(documentPath, ruleset.Diplomacy.AiOwnDiplomacy.PowerUnityDivisor, "diplomacy.aiOwnDiplomacy.powerUnityDivisor");

        // cityOrders.orders[].inProgressEncodingRadix: FortificationCode: code % this and code / this.
        for (var i = 0; i < ruleset.CityOrders.Orders.Count; i++)
        {
            RequirePositiveDivisor(
                documentPath,
                ruleset.CityOrders.Orders[i].InProgressEncodingRadix,
                $"cityOrders.orders[{i}].inProgressEncodingRadix");
        }

        // T67 (#344): cityOrders.fortifyWeeklyPointCap bounds the weekly fortify step; 0 or less would freeze
        // every pending order forever.
        if (ruleset.CityOrders.FortifyWeeklyPointCap <= 0)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"'cityOrders.fortifyWeeklyPointCap' must be greater than 0 (a pending fortify order would never advance); got {ruleset.CityOrders.FortifyWeeklyPointCap}.");
        }
    }

    /// <summary>
    /// Bug #979: <c>AiFaithfulEconomy.RollTroops</c> draws <c>NextInt(span)</c> with
    /// <c>span = standardBattalionSize * faithfulAmountSpanNumerator / faithfulAmountSpanDenominator</c>,
    /// so the span must be at least 1 for every unit type the recruit-type draw can pick. Run after the
    /// divisor and bound checks, so each of its inputs is already known to be positive.
    /// </summary>
    private static void ValidateRollTroopsSpan(string documentPath, Ruleset ruleset)
    {
        var ai = ruleset.Ai;
        foreach (var bucket in ai.FaithfulRecruitTypeBuckets)
        {
            var type = ruleset.UnitTypeById(bucket.UnitTypeId);
            if (type is null)
            {
                continue;
            }

            // RollTroops computes size * numerator / denominator in int, left to right: refuse a product
            // that would wrap, then evaluate the span exactly as the runtime does.
            var product = (long)type.StandardBattalionSize * ai.FaithfulAmountSpanNumerator;
            if (product > int.MaxValue || product < int.MinValue)
            {
                throw new MalformedGameDataException(
                    documentPath,
                    $"'ai.faithfulAmountSpanNumerator' ({ai.FaithfulAmountSpanNumerator}) times unit type '{type.Id}' standardBattalionSize "
                    + $"({type.StandardBattalionSize}) is {product}, which overflows a 32-bit int in the recruit-amount span (a random-draw bound).");
            }

            var span = unchecked(type.StandardBattalionSize * ai.FaithfulAmountSpanNumerator) / ai.FaithfulAmountSpanDenominator;
            if (span < 1)
            {
                throw new MalformedGameDataException(
                    documentPath,
                    $"'ai.faithfulAmountSpanNumerator'/'ai.faithfulAmountSpanDenominator' give a recruit-amount span of {span} "
                    + $"for unit type '{type.Id}' (standardBattalionSize {type.StandardBattalionSize}); it is a random-draw bound and must be at least 1.");
            }
        }
    }

    /// <summary>
    /// Bug #973: <c>IRng.NextInt(bound)</c> throws for a bound of 0 or less, <c>NextInt(min, max)</c> for an
    /// empty range, and <c>NextChance(1, d)</c> for a denominator of 0 or less. Every ruleset value that
    /// reaches such a draw is checked here, so a bad ruleset is refused at load rather than crashing mid-game.
    /// Each comment names the draw site.
    /// </summary>
    private static void ValidateRngBounds(string documentPath, Ruleset ruleset)
    {
        // NextInt(bound): the bound must be > 0.
        // AiFaithfulEconomy.RollType: NextInt(faithfulTypeDrawBound).
        RequireRngBound(documentPath, ruleset.Ai.FaithfulTypeDrawBound, "ai.faithfulTypeDrawBound");
        // AiFaithfulEconomy.RollTroops: span = size * numerator / denominator, then NextInt(span); a numerator of 0 or less makes the span 0 or less.
        RequireRngBound(documentPath, ruleset.Ai.FaithfulAmountSpanNumerator, "ai.faithfulAmountSpanNumerator");
        // BattleCasualties (two sites) and TypeWeightedInstantCandidate: NextInt(casualtyDivisorRandomSpan) + base.
        RequireRngBound(documentPath, ruleset.Combat.CasualtyDivisorRandomSpan, "combat.casualtyDivisorRandomSpan");
        // BattleCasualties promotion roll: NextInt(promotionChanceDenominator) == 0.
        RequireRngBound(documentPath, ruleset.Combat.PromotionChanceDenominator, "combat.promotionChanceDenominator");
        // InstantBattleResolver auto-peace roll (two sites): NextInt(autoPeaceChanceDenominator) < numerator.
        RequireRngBound(documentPath, ruleset.Combat.AutoPeaceChanceDenominator, "combat.autoPeaceChanceDenominator");
        // ConquestCascade: NextInt(conquestLoyaltyRandomBonusMax) per conquered city.
        RequireRngBound(documentPath, ruleset.Capture.ConquestLoyaltyRandomBonusMax, "capture.conquestLoyaltyRandomBonusMax");
        // FleetAttritionRule zero-supply condition loss: NextInt(zeroSupplyConditionRandomBound).
        RequireRngBound(documentPath, ruleset.Naval.ZeroSupplyConditionRandomBound, "naval.zeroSupplyConditionRandomBound");
        // FleetPower: NextInt(randomBandCount).
        RequireRngBound(documentPath, ruleset.Combat.Naval.RandomBandCount, "combat.naval.randomBandCount");
        // CityLoyaltyDraws: loyalty += NextInt(loyaltyRiseRollBound).
        RequireRngBound(documentPath, ruleset.Economy.LoyaltyRiseRollBound, "economy.loyaltyRiseRollBound");
        // MercenaryPoolRestock: the empty-slot roll, the replace roll, the template index and the quality jitter.
        RequireRngBound(documentPath, ruleset.Recruitment.MercenaryRestockEmptyRollDenominator, "recruitment.mercenaryRestockEmptyRollDenominator");
        RequireRngBound(documentPath, ruleset.Recruitment.MercenaryRestockReplaceRollDenominator, "recruitment.mercenaryRestockReplaceRollDenominator");
        RequireRngBound(documentPath, ruleset.Recruitment.MercenaryRestockTemplateDrawCount, "recruitment.mercenaryRestockTemplateDrawCount");
        RequireRngBound(documentPath, ruleset.Recruitment.MercenaryRestockQualityJitterSteps, "recruitment.mercenaryRestockQualityJitterSteps");

        // NextInt(min, max + 1) in InstantBattleResolver.DrawScatterDistance: the range [min, max] must not be empty.
        var scatter = ruleset.Combat.ScatteredDefeat;
        if (scatter.ScatterTilesMin > scatter.ScatterTilesMax)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"'combat.scatteredDefeat.scatterTilesMin' ({scatter.ScatterTilesMin}) must not exceed "
                + $"'combat.scatteredDefeat.scatterTilesMax' ({scatter.ScatterTilesMax}) (the scatter distance is a random draw over that range).");
        }

        // NextChance(1, d): the denominator must be > 0.
        RequireRngBound(documentPath, ruleset.Diplomacy.OfferRollDenominator, "diplomacy.offerRollDenominator");
        RequireRngBound(documentPath, ruleset.Diplomacy.ThawBonusChanceDenominator, "diplomacy.thawBonusChanceDenominator");
        RequireRngBound(documentPath, ruleset.Diplomacy.AiOwnDiplomacy.AllianceRollDenominator, "diplomacy.aiOwnDiplomacy.allianceRollDenominator");
        RequireRngBound(documentPath, ruleset.Diplomacy.AiOwnDiplomacy.WarDeclareRollDenominator, "diplomacy.aiOwnDiplomacy.warDeclareRollDenominator");
        RequireRngBound(documentPath, ruleset.Economy.DepositionRandomDivisor, "economy.depositionRandomDivisor");
        RequireRngBound(documentPath, ruleset.Economy.LoyaltyFallProbabilityDenominator, "economy.loyaltyFallProbabilityDenominator");
        RequireRngBound(documentPath, ruleset.Economy.FamineLoyaltyLossProbabilityDenominator, "economy.famineLoyaltyLossProbabilityDenominator");
        RequireRngBound(documentPath, ruleset.Naval.StormWinterSpikeChanceDenominator, "naval.stormWinterSpikeChanceDenominator");

        // WeatherEventSystem (T81): NextChance(numerator, denominator) per centre, NextChance(ring) per outer-ring
        // cell, and the loops over the radii. Every odds pair must be a valid chance, and every radius and
        // distance non-negative (a negative radius would silently paint nothing, or loop backwards).
        var weather = ruleset.Economy.Weather;
        for (var i = 0; i < weather.BySeason.Count; i++)
        {
            var odds = weather.BySeason[i];
            RequireChance(documentPath, odds.EarlyNumerator, odds.EarlyDenominator, $"economy.weather.bySeason[{i}].early");
            RequireChance(documentPath, odds.LateNumerator, odds.LateDenominator, $"economy.weather.bySeason[{i}].late");
            RequireNonNegative(documentPath, odds.EarlyRadiusTiles, $"economy.weather.bySeason[{i}].earlyRadiusTiles");
            RequireNonNegative(documentPath, odds.LateRadiusTiles, $"economy.weather.bySeason[{i}].lateRadiusTiles");
        }

        RequireChance(
            documentPath, weather.RingChanceNumerator, weather.RingChanceDenominator, "economy.weather.ringChance");
        RequireNonNegative(documentPath, weather.ClearRadiusTiles, "economy.weather.clearRadiusTiles");
        RequireNonNegative(documentPath, weather.CityExclusionRadiusTiles, "economy.weather.cityExclusionRadiusTiles");
        RequireNonNegative(documentPath, weather.RoughSeaMoveCost, "economy.weather.roughSeaMoveCost");
    }

    private static void RequireNonNegative(string documentPath, int value, string field)
    {
        if (value < 0)
        {
            throw new MalformedGameDataException(
                documentPath, $"'{field}' must not be negative; got {value}.");
        }
    }

    private static void RequireRngBound(string documentPath, int value, string field)
    {
        if (value <= 0)
        {
            throw new MalformedGameDataException(
                documentPath, $"'{field}' must be greater than 0 (it is a random-draw bound); got {value}.");
        }
    }

    private static void RequireChance(string documentPath, int numerator, int denominator, string field)
    {
        if (denominator <= 0 || numerator < 0 || numerator > denominator)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"'{field}' odds {numerator}-in-{denominator} are not a valid chance (it is a random-draw bound): "
                + "the denominator must be greater than 0 and the numerator between 0 and the denominator.");
        }
    }

    /// <summary>
    /// N5 (T63 review round 1): a shared guard for the ruleset's own division-safety fields -- see the
    /// call sites in <see cref="ValidateRuleset"/> for which ones and why.
    /// </summary>
    private static void RequirePositiveDivisor(string documentPath, int value, string field)
    {
        if (value <= 0)
        {
            throw new MalformedGameDataException(
                documentPath, $"'{field}' must be greater than 0 (it is a runtime divisor); got {value}.");
        }
    }

    /// <summary>
    /// DoD 10 (folded follow-up <see href="https://github.com/diegoami/imperial_conquest_2/issues/99">#99</see>):
    /// a ruleset whose <c>newsLog.seasonNames</c> list does not have exactly one name per
    /// <c>calendar.seasonsPerYear</c> season used to crash the first time the round-tick header
    /// indexed it, rather than failing when the file was loaded. <see cref="Ruleset.ValidateSeasonNames"/>
    /// (<c>src/IC2.Engine/Model/Ruleset.cs</c>) is the check itself; this wraps its
    /// <see cref="InvalidOperationException"/> as a <see cref="MalformedGameDataException"/> naming
    /// the document, exactly like every other check in this class.
    /// </summary>
    private static void ValidateNewsLogSeasonNames(string documentPath, Ruleset ruleset)
    {
        try
        {
            ruleset.ValidateSeasonNames();
        }
        catch (InvalidOperationException ex)
        {
            throw new MalformedGameDataException(documentPath, ex.Message, ex);
        }
    }

    /// <summary>
    /// Checks that the calendar can actually turn over. The season advances only when the weekly step
    /// lands exactly on <see cref="CalendarRules.SeasonAdvanceFromWeek"/>, so a start week of the wrong
    /// parity produces a game whose season — and therefore whose year — never moves, with no visible
    /// error anywhere. That is a data mistake worth catching at load rather than months later.
    /// </summary>
    private static void ValidateCalendar(string documentPath, CalendarRules calendar)
    {
        if (calendar.WeekModulus <= 0 || calendar.WeekStep <= 0 || calendar.SeasonsPerYear <= 0)
        {
            throw new MalformedGameDataException(
                documentPath, "calendar.weekModulus, calendar.weekStep and calendar.seasonsPerYear must all be positive.");
        }

        if (calendar.StartWeek < 0 || calendar.StartWeek >= calendar.WeekModulus)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"calendar.startWeek {calendar.StartWeek} is outside the 0..{calendar.WeekModulus - 1} week cycle.");
        }

        if (calendar.SeasonAdvanceFromWeek < 0 || calendar.SeasonAdvanceFromWeek >= calendar.WeekModulus)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"calendar.seasonAdvanceFromWeek {calendar.SeasonAdvanceFromWeek} is outside the "
                + $"0..{calendar.WeekModulus - 1} week cycle.");
        }

        if (calendar.StartSeasonIndex < 0 || calendar.StartSeasonIndex >= calendar.SeasonsPerYear)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"calendar.startSeasonIndex {calendar.StartSeasonIndex} is outside the "
                + $"0..{calendar.SeasonsPerYear - 1} season cycle.");
        }

        var week = calendar.StartWeek;
        for (var step = 0; step < calendar.WeekModulus; step++)
        {
            if (week == calendar.SeasonAdvanceFromWeek)
            {
                return;
            }

            week = (week + calendar.WeekStep) % calendar.WeekModulus;
        }

        throw new MalformedGameDataException(
            documentPath,
            $"calendar.startWeek {calendar.StartWeek} advancing by {calendar.WeekStep} never reaches "
            + $"calendar.seasonAdvanceFromWeek {calendar.SeasonAdvanceFromWeek} in a {calendar.WeekModulus}-week "
            + "cycle, so the season and the year could never advance.");
    }

    private static void ValidateScenario(string documentPath, Scenario scenario)
    {
        RequireDistinct(documentPath, scenario.Seats, s => s.Nation, "seats");

        if (scenario.Victory.Type == VictoryConditionType.Custom && string.IsNullOrWhiteSpace(scenario.Victory.Goal))
        {
            throw new MalformedGameDataException(
                documentPath, "a custom victory condition must state a \"goal\".");
        }

        foreach (var seat in scenario.Seats)
        {
            if (seat.Personality is { } personality)
            {
                RequireUnitRange(documentPath, personality.Aggression, $"seats['{seat.Nation}'].personality.aggression");
                RequireUnitRange(documentPath, personality.ExpansionDrive, $"seats['{seat.Nation}'].personality.expansionDrive");
                RequireUnitRange(documentPath, personality.LoyaltyToAlliances, $"seats['{seat.Nation}'].personality.loyaltyToAlliances");
            }
        }
    }

    private static void ValidateSave(string documentPath, SaveGame save)
    {
        // The loader checks the schema version of the root object only, so the nested state's own
        // version is checked here, beside the ids it is already cross-checked against.
        if (save.State.SchemaVersion != save.SchemaVersion)
        {
            throw new SchemaVersionMismatchException(documentPath, save.State.SchemaVersion, save.SchemaVersion);
        }

        ValidateState(documentPath, save.State);

        if (!string.Equals(save.WorldId, save.State.WorldId, StringComparison.Ordinal)
            || !string.Equals(save.RulesetId, save.State.RulesetId, StringComparison.Ordinal)
            || !string.Equals(save.ScenarioId, save.State.ScenarioId, StringComparison.Ordinal))
        {
            throw new MalformedGameDataException(
                documentPath, "the save's world/ruleset/scenario ids disagree with the state it carries.");
        }
    }

    private static void ValidateState(string documentPath, GameState state)
    {
        RequireDistinct(documentPath, state.Nations, n => n.Id, "state.nations");
        RequireDistinct(documentPath, state.Cities, c => c.Id, "state.cities");
        RequireDistinct(documentPath, state.Armies, a => a.Id, "state.armies");
        RequireDistinct(documentPath, state.Fleets, f => f.Id, "state.fleets");

        if (!state.Relations.IsStructurallyValid())
        {
            throw new MalformedGameDataException(
                documentPath, "the diplomatic relation matrix must be square, sized to its distinct nation list.");
        }

        if (state.TurnOrder.Count == 0)
        {
            throw new MalformedGameDataException(documentPath, "a state must have at least one seat in its turn order.");
        }

        if (state.ActiveSeatIndex < 0 || state.ActiveSeatIndex >= state.TurnOrder.Count)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"activeSeatIndex {state.ActiveSeatIndex} is outside the {state.TurnOrder.Count}-seat turn order.");
        }

        foreach (var nationId in state.TurnOrder)
        {
            if (state.NationById(nationId) is null)
            {
                throw new UnresolvedReferenceException(documentPath, "nation", nationId);
            }
        }

        foreach (var nationId in state.Relations.NationIds)
        {
            if (state.NationById(nationId) is null)
            {
                throw new UnresolvedReferenceException(documentPath, "nation", nationId);
            }
        }

        // The ring buffer's index and its payload have to agree, or NewsLog.Append would be deciding
        // fullness from one and writing the other.
        if (!state.NewsLog.IsConsistent())
        {
            throw new MalformedGameDataException(
                documentPath,
                $"newsLog.mostRecentSlot {state.NewsLog.MostRecentSlot} does not address the last of its "
                + $"{state.NewsLog.Slots.Count} slots.");
        }

        // Every later system will reach for state.NationById(city.Owner) and dereference it. The world
        // loader already resolves these; a state loaded from a save has to be held to the same rule.
        foreach (var city in state.Cities)
        {
            RequireStateNation(documentPath, state, city.Owner);
            RequireStateNation(documentPath, state, city.Allegiance);
        }

        // T35: a recruitment slot's target city, and the pending offer's proposing nation, are the two
        // new cross-references this task's model additions carry.
        foreach (var nation in state.Nations)
        {
            foreach (var slot in nation.RecruitmentSlots)
            {
                if (state.CityById(slot.TargetCityId) is null)
                {
                    throw new UnresolvedReferenceException(documentPath, "city", slot.TargetCityId);
                }
            }
        }

        if (state.PendingOffer is { } pendingOffer)
        {
            RequireStateNation(documentPath, state, pendingOffer.ProposingNationId);
        }

        // T71 (#411 S2): T86's two nation references. A null Neighbours (a state that carries no
        // neighbour set, answered by NeighbourGeography's fallback) and a null ConqueredBy (never
        // conquered) stay valid; only a named id has to resolve.
        if (state.Neighbours is { } neighbours)
        {
            foreach (var entry in neighbours)
            {
                RequireStateNation(documentPath, state, entry.NationId);
                foreach (var neighbourId in entry.NeighbourIds)
                {
                    RequireStateNation(documentPath, state, neighbourId);
                }
            }
        }

        foreach (var nation in state.Nations)
        {
            if (nation.ConqueredBy is { } conqueror)
            {
                RequireStateNation(documentPath, state, conqueror);
            }
        }

        foreach (var army in state.Armies)
        {
            RequireStateNation(documentPath, state, army.Nation);

            if (army.AboardFleetId is { } fleetId)
            {
                var carrier = state.FleetById(fleetId)
                              ?? throw new UnresolvedReferenceException(documentPath, "fleet", fleetId);

                // The link has to be mutual, or two armies could both claim the one fleet that
                // FleetState documents as carrying a single army.
                if (!string.Equals(carrier.CarriedArmyId, army.Id, StringComparison.Ordinal))
                {
                    throw new MalformedGameDataException(
                        documentPath,
                        $"army '{army.Id}' is aboard fleet '{fleetId}', but that fleet carries "
                        + $"'{carrier.CarriedArmyId ?? "no army"}'.");
                }
            }

            if ((army.AboardFleetId is not null) != (army.CoveredTileCode is null))
            {
                throw new MalformedGameDataException(
                    documentPath,
                    $"army '{army.Id}' must cover a map tile exactly when it is not aboard a fleet.");
            }
        }

        foreach (var fleet in state.Fleets)
        {
            RequireStateNation(documentPath, state, fleet.Nation);

            if (fleet.CarriedArmyId is { } armyId)
            {
                var carried = state.ArmyById(armyId)
                              ?? throw new UnresolvedReferenceException(documentPath, "army", armyId);

                if (!string.Equals(carried.AboardFleetId, fleet.Id, StringComparison.Ordinal))
                {
                    throw new MalformedGameDataException(
                        documentPath,
                        $"fleet '{fleet.Id}' carries army '{armyId}', but that army is aboard "
                        + $"'{carried.AboardFleetId ?? "no fleet"}'.");
                }
            }

            if (fleet.BuildCityId is { } cityId && state.CityById(cityId) is null)
            {
                throw new UnresolvedReferenceException(documentPath, "city", cityId);
            }

            if (fleet.IsUnderConstruction && fleet.BuildCityId is null)
            {
                throw new MalformedGameDataException(
                    documentPath, $"fleet '{fleet.Id}' is under construction but names no build city.");
            }
        }
    }

    private static void RequireStateNation(string documentPath, GameState state, string nationId)
    {
        if (state.NationById(nationId) is null)
        {
            throw new UnresolvedReferenceException(documentPath, "nation", nationId);
        }
    }

    /// <remarks>
    /// The 0-1 bound is a schema bound, not a tunable rule: <see cref="AiPersonality"/>'s parameters are
    /// <em>defined</em> as fractions in <c>docs/game-design.md</c> §AI, so a value outside that range is
    /// a malformed document rather than an unusual ruleset. It is therefore deliberately not a
    /// <see cref="Ruleset"/> field.
    /// </remarks>
    private static void RequireUnitRange(string documentPath, double value, string field)
    {
        if (value is < 0 or > 1 || double.IsNaN(value))
        {
            throw new MalformedGameDataException(
                documentPath, $"'{field}' must be between 0 and 1; got {value}.");
        }
    }

    private static void RequireNation(string documentPath, World world, string nationId)
    {
        if (world.NationById(nationId) is null)
        {
            throw new UnresolvedReferenceException(documentPath, "nation", nationId);
        }
    }

    private static void RequireOnMap(string documentPath, World world, int x, int y, string what)
    {
        if ((uint)x >= (uint)world.Width || (uint)y >= (uint)world.Height)
        {
            throw new MalformedGameDataException(
                documentPath, $"{what} sits at ({x}, {y}), outside the {world.Width}×{world.Height} map.");
        }
    }

    private static void RequireDistinct<T>(string documentPath, ValueList<T> items, Func<T, string> idSelector, string what)
    {
        if (items.FirstDuplicateId(idSelector) is { } duplicate)
        {
            throw new MalformedGameDataException(documentPath, $"{what} contains two entries with id '{duplicate}'.");
        }
    }
}
