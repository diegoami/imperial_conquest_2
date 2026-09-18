using System.Text.Json.Serialization;

namespace IC2.Engine.Model;

/// <summary>
/// A named, versioned bundle of every gameplay constant, cost table and formula-variant selector —
/// one of the four file kinds in <c>docs/game-design.md</c> §"The core data model".
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/game-design.md</c> design principle 1 ("data over code") and
/// <c>docs/build-orchestration-plan.md</c>'s engine rules both say the same thing: <strong>every</strong>
/// gameplay number lives here, never as a C# literal. Formula <em>shape</em> stays as engine code;
/// formula <em>variants</em> are selected by name through <see cref="Flags"/>.
/// </para>
/// <para>
/// This record is deliberately wide. Later tasks (economy, recruitment, movement, naval, battle,
/// diplomacy, city orders, victory, news) each consume one of the groups below; the task entry's
/// hazard note is that 25 other tasks compile against these types, so the fields they need exist from
/// day one rather than being bolted on by widening the record later.
/// </para>
/// <para>
/// Every number in a shipped ruleset file carries a <c>"_provenance"</c> sibling entry naming the
/// report it was transcribed from. Anything that could not be found in a report is <em>omitted</em>,
/// not invented.
/// </para>
/// </remarks>
public sealed record Ruleset(
    int SchemaVersion,
    string Id,
    string Name,
    string Description,
    CalendarRules Calendar,
    ValueList<UnitTypeRules> UnitTypes,
    TerrainRules Terrain,
    EconomyRules Economy,
    RecruitmentRules Recruitment,
    ArmyManagementRules ArmyManagement,
    NavalRules Naval,
    CombatRules Combat,
    SiegeRules Siege,
    LoyaltyRules Loyalty,
    DiplomacyRules Diplomacy,
    CityOrderRules CityOrders,
    NewsLogRules NewsLog,
    MapMarkerRules MapMarkers,
    VictoryRules Victory,
    RulesetFlags Flags,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null) : IVersionedDocument
{
    /// <summary>Finds a unit type by id, or <see langword="null"/>.</summary>
    public UnitTypeRules? UnitTypeById(string id) => UnitTypes.FindById(u => u.Id, id);

    /// <summary>
    /// The move cost this ruleset prices <paramref name="tileTypeId"/> at, falling back to
    /// <see cref="TerrainRules.DefaultMoveCost"/> for a tile type the ruleset does not know — the rule
    /// <c>docs/game-design.md</c> §Movement states for custom worlds.
    /// </summary>
    public int MoveCostFor(string tileTypeId)
    {
        foreach (var entry in Terrain.MoveCosts)
        {
            if (string.Equals(entry.TileTypeId, tileTypeId, StringComparison.Ordinal))
            {
                return entry.MoveCost;
            }
        }

        return Terrain.DefaultMoveCost;
    }
}

/// <summary>Week/season/year advance, and the quarterly boundary everything economic hangs off.</summary>
/// <param name="StartWeek">
/// The week a scenario begins on. It is ruleset data rather than an engine value because the cycle's
/// <em>parity</em> is load-bearing: with <see cref="WeekStep"/> 2 and <see cref="WeekModulus"/> 12, only
/// an odd start ever reaches <see cref="SeasonAdvanceFromWeek"/>, so an even one would leave the season
/// — and therefore the year — unable to advance at all.
/// <see cref="IC2.Engine.Serialization.GameDataValidation"/> rejects a calendar whose start cannot
/// reach the season boundary.
/// </param>
/// <param name="StartSeasonIndex">The season a scenario begins in, zero-based.</param>
public sealed record CalendarRules(
    int WeekStep,
    int WeekModulus,
    int SeasonAdvanceFromWeek,
    int SeasonsPerYear,
    int StartWeek,
    int StartSeasonIndex,
    int StartYearBc,
    int CityUnitStateCodeStep,
    int CityUnitStateCodeCap,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>One row of the unit-type stat table.</summary>
public sealed record UnitTypeRules(
    string Id,
    string Name,
    string Abbreviation,
    int Moves,
    int StandardBattalionSize,
    int Shots,
    int Range,
    int RecruitCost,
    int QuarterlyPrice,
    int CombatPowerWeight,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>The move cost this ruleset charges for one tile type.</summary>
public sealed record TerrainMoveCost(string TileTypeId, int MoveCost);

/// <summary>Terrain pricing. The table is keyed by tile-type id, so it works for any world's tile set.</summary>
public sealed record TerrainRules(
    ValueList<TerrainMoveCost> MoveCosts,
    int DefaultMoveCost,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>Tax, upkeep, supply purchase, and the per-army/per-fleet purses.</summary>
/// <remarks>
/// <see cref="SupplyConsumption"/>, <see cref="SupplyMorale"/> and <see cref="Weather"/> were added by
/// <c>docs/task-catalogue.md</c> "T08 Economy, supply, and purses": the supply-driven
/// strategic-morale rule and the weather frequency curve were previously unowned by any task (T02 shipped
/// this record before <c>docs/investigations/thracia-supply-morale.md</c> existed), so T08 widens it here
/// rather than leaving the constants as C# literals — the same additive pattern T31 used for
/// <see cref="SiegeRules"/>.
/// <para>
/// <see cref="SupplyDialogArmyCapacityBonus"/> was added in review round 3 (R1's resolution,
/// <c>supply-capacity-rounding.md</c> [confirmed]): the supply dialog's <c>TAFSupply_ChangeSupply</c> /
/// <c>TAFSupply_ChangeBuyAmount</c> cap an army's dialog transfer at <c>troops / ArmySupplyTonsPerTroops
/// + SupplyDialogArmyCapacityBonus</c> (an <c>IDIV</c> then an unconditional <c>INC</c>, no rounding), on
/// both the free (own-city) and paid (foreign) paths alike — not <see cref="ArmySupplyTonsPerTroops"/>
/// alone, which stays the general capacity every other path (automatic resupply, army-to-army rebalancing,
/// battle absorption) uses unmodified. The fleet dialog cap needs no such field: it is exactly
/// <c>ships × FleetSupplyTonsPerShip</c> on both paths, the same formula <see cref="FleetSupplyTonsPerShip"/>
/// already describes. <c>docs/task-catalogue.md</c> "T38 Supply dialog follow-ups", Done-when 2: this
/// field moved ahead of <see cref="Provenance"/> and lost its default, so a ruleset that omits it now
/// fails <see cref="Serialization.SchemaValidator"/> instead of silently defaulting to 1.
/// </para>
/// <para>
/// <see cref="AutoResupplyPurseTopUpThreshold"/> and <see cref="AutoResupplyPurseTopUpAmount"/> were added
/// by T38 (<c>supply-capacity-rounding.md</c> [derived], <c>FUN_0044F6D8</c> lines 53091-53104):
/// automatic resupply at an army's or fleet's own city, after the free ton transfer, grants a flat
/// <see cref="AutoResupplyPurseTopUpAmount"/> from the treasury when the purse is under
/// <see cref="AutoResupplyPurseTopUpThreshold"/> and the treasury is positive — a real transfer, not
/// invented money, so the treasury pays for exactly what the purse gains. The mirror case (a purse over
/// <see cref="PurseCapPerUnit"/> sends the excess to the treasury) needs no new field: it reuses
/// <see cref="PurseCapPerUnit"/>, the same cap <c>TAFSupply_ChangeMoney</c> already enforces everywhere
/// else a purse is credited. Review round 1, B2: tagged <c>[derived]</c>, not <c>[confirmed]</c> — the
/// report's own section heading for this rule is <c>[derived, then confirmed below]</c>, and the
/// "confirmed below" paragraph confirms only the <c>troops div 100</c> cap against 75 save states, not
/// the purse rule; <c>docs/game-design.md:100</c> already tags it "confirmed caps; derived purse rule".
/// </para>
/// Every other field, and every other record in this file, is unchanged.
/// </remarks>
/// <remarks>
/// <para>
/// <strong>T35's additions</strong> (<c>docs/task-catalogue.md</c> "T35 Model: nation tax base,
/// recruitment slots, and the pending diplomatic offer") wire up the quarterly tick's nation loop and
/// city loop — everything <c>FUN_00451b40</c> runs after T08's upkeep billing
/// (<c>nation-tax-base-and-city-economy-fields.md</c>, <c>city-population-growth.md</c>). Three
/// unrelated formulas each happen to use the divisor <c>4</c> — the population-growth gap term
/// (<see cref="PopulationGrowthGapDivisor"/>), the tax-base rebuild's per-city multiplier
/// (<see cref="TaxBaseContributionMultiplier"/>), and the treasury credit's own <c>taxBase / 4</c> term
/// (<see cref="TreasuryCreditTaxBaseQuarterShareDivisor"/>) — kept as three separate fields rather than
/// one shared constant, the same reasoning <see cref="SupplyMoraleRules.MovesTroopDivisor"/>'s own remark
/// gives for its numerically-identical-but-conceptually-distinct <c>20000</c>.
/// </para>
/// <para>
/// <c>UnityDecayPerQuarter</c> is renamed <see cref="MobilizationDecayPerQuarter"/>
/// (bug <c>#68</c>): the quarterly <c>−3</c> <c>decompiled-quarterly-billing-and-economy.md</c>
/// attributed to unity is really mobilization's; unity instead drifts <em>up</em> by
/// <see cref="UnityBaseGainPerQuarter"/> a quarter, reduced by the tax rate and the just-decayed
/// mobilization, clamped between <see cref="UnityFloor"/> and the pre-existing <see cref="UnityCap"/>.
/// </para>
/// <para>
/// The quarterly loyalty draws' three new constants (<see cref="LoyaltyRiseRollBound"/>,
/// <see cref="LoyaltyFallProbabilityDenominator"/>, <see cref="LoyaltyFallTaxDivisor"/>) are
/// <c>[derived]</c> from the code, not save-checked, exactly like the thresholds T08 already shipped
/// (<see cref="LowTaxLoyaltyThresholdPercent"/>, <see cref="LowTaxLoyaltyCityThreshold"/>,
/// <see cref="RebellionLoyaltyThreshold"/>) — this task is the first to actually read any of the six.
/// </para>
/// <para>
/// <strong>T37's additions</strong> (<c>docs/task-catalogue.md</c> "T37 City supply production and
/// famine unrest") wire up the weekly city loop <c>FUN_004514ec</c> runs before the army and fleet
/// loops — <c>city-population-growth.md</c> §"The weekly step is city supply production", <c>[confirmed]</c>:
/// 33 save pairs, 10,693 of 10,980 city-turns exact. <see cref="CitySupplyBaselineSeasonValue"/> is the
/// <c>40</c> subtracted from the season value (<see cref="SupplyConsumptionRules.SeasonValues"/>, the same
/// table T08's army consumption already reads); <see cref="CitySupplyProductionDivisor"/> is the resulting
/// term's <c>/ 10</c>; <see cref="CitySupplyMobilizationDivisor"/> is the owner-mobilization shrink's
/// <c>/ 200</c> (numerically the same shape as the quarterly growth formula's mobilization term but a
/// separate field, since the two divisors — 300 there, 200 here — are different and the two formulas are
/// conceptually distinct, the same reasoning already given for <see cref="PopulationGrowthMobilizationDivisor"/>);
/// <see cref="CitySupplyCapTonsPerPopulationThousand"/> is the <c>pop × 10</c> ceiling. The famine-unrest
/// roll (<see cref="FamineLoyaltyLossProbabilityDenominator"/>, a 1-in-3 chance, and
/// <see cref="FamineLoyaltyLossAmount"/>, the flat 1-point loss) is numerically coincidental with
/// <see cref="LoyaltyFallProbabilityDenominator"/> (also 3) but a separate field: one is this task's
/// weekly Winter-only, empty-stock check, the other T35's quarterly, tax-rate-gated one, and they must be
/// able to change independently.
/// </para>
/// </remarks>
public sealed record EconomyRules(
    int TaxRateDivisor,
    int ShipUpkeepPerQuarter,
    int MobilizationDecayPerQuarter,
    int UnityCap,
    int PurseCapPerUnit,
    int SupplyTonsPerTalent,
    int ArmySupplyTonsPerTroops,
    int FleetSupplyTonsPerShip,
    int SupplyPercentNumerator,
    int UnpaidUpkeepTroopLossDivisor,
    int LowTaxLoyaltyThresholdPercent,
    int LowTaxLoyaltyCityThreshold,
    int RebellionLoyaltyThreshold,
    SupplyConsumptionRules SupplyConsumption,
    SupplyMoraleRules SupplyMorale,
    WeatherEventRules Weather,
    int SupplyDialogArmyCapacityBonus,
    int AutoResupplyPurseTopUpThreshold,
    int AutoResupplyPurseTopUpAmount,
    int TaxBaseContributionMultiplier,
    int WealthPerPopulationThousand,
    int TreasuryCreditTaxBaseQuarterShareDivisor,
    int TreasuryCreditPerCityUpkeep,
    int TreasuryCreditWealthDivisor,
    int TradeIncomeTaxBaseDivisor,
    int PopulationGrowthGapDivisor,
    int PopulationGrowthTaxDivisor,
    int PopulationGrowthMobilizationDivisor,
    int PopulationGrowthConstantAddend,
    int ThreatenedCityAdjacencyRadius,
    int UnityFloor,
    int UnityBaseGainPerQuarter,
    int UnityTaxRateDivisor,
    int UnityMobilizationDivisor,
    int LoyaltyRiseRollBound,
    int LoyaltyFallProbabilityDenominator,
    int LoyaltyFallTaxDivisor,
    int CitySupplyBaselineSeasonValue,
    int CitySupplyProductionDivisor,
    int CitySupplyMobilizationDivisor,
    int CitySupplyCapTonsPerPopulationThousand,
    int FamineLoyaltyLossProbabilityDenominator,
    int FamineLoyaltyLossAmount,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>
/// Per-turn supply consumption (<c>docs/design-audit.md</c> §2.9a, <c>investigations/thracia-supply-morale.md</c>).
/// </summary>
/// <param name="ConsumptionBaseValue">
/// The <c>90</c> of <c>((90 − seasonVal) × troops) / consumptionDivisor</c> — confirmed alongside the
/// season table and the city food term's mirrored <c>seasonVal − 40</c> shape in the same function.
/// </param>
/// <param name="ConsumptionDivisor">The <c>20000</c> divisor of the same expression.</param>
/// <param name="FleetEmbarkedDivisor">
/// An army aboard a fleet (<see cref="Model.ArmyState.AboardFleetId"/> non-null) consumes a flat
/// <c>troops / this</c> instead, with no seasonal term at all.
/// </param>
/// <param name="SeasonValues">
/// <c>seasonVal</c> by <see cref="Model.CalendarState.SeasonIndex"/> — Spring 50, Summer 80, Autumn 80,
/// Winter 20, DAT offset <c>0x1F7D8</c>. Positional, sized to <see cref="CalendarRules.SeasonsPerYear"/>,
/// exactly like <see cref="TerrainRules.MoveCosts"/> is keyed rather than hardcoded to a count.
/// </param>
public sealed record SupplyConsumptionRules(
    int ConsumptionBaseValue,
    int ConsumptionDivisor,
    int FleetEmbarkedDivisor,
    ValueList<int> SeasonValues,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>
/// The supply→strategic-morale rule (<c>docs/design-audit.md</c> §2.9a,
/// <c>investigations/thracia-supply-morale.md</c>), run every <see cref="Core.TurnPhase.ArmyTick"/> after
/// that turn's consumption. Writes only <see cref="Model.ArmyState.Morale"/> (army record <c>+14</c>,
/// strategic) — never the per-unit tactical morale array, which this ruleset group does not model at all
/// (<c>docs/design-audit.md</c> §2.9's two-morales hazard).
/// </summary>
/// <param name="DecayThresholdPercent">Below this supply percentage, morale decays: <c>pct &lt; 10</c>.</param>
/// <param name="DeadBandUpperPercent">
/// At or below this percentage (and at or above <see cref="DecayThresholdPercent"/>) morale is
/// unchanged — the confirmed dead band, <c>10 ≤ pct ≤ 15</c>. Above it, morale regenerates.
/// </param>
/// <param name="DecayAmount">Morale lost per turn while starving, floored at <see cref="MoraleFloor"/>.</param>
/// <param name="MoraleFloor">
/// The hard floor, <c>51</c> — also the base of the army panel's five 4-wide morale tiers
/// (<c>moraleNames[(v − 51) &gt;&gt; 2]</c>).
/// </param>
/// <param name="RegenAmount">
/// Morale gained per turn while well supplied, capped at <see cref="MoraleCeiling"/>. Half
/// <see cref="DecayAmount"/> — the confirmed 2:1 decay/regen asymmetry.
/// </param>
/// <param name="MoraleCeiling">The hard ceiling, <c>70</c>.</param>
/// <param name="MovesPenaltyOnDecay">Moves lost, on top of <see cref="BaseMovesMax"/>, the turn morale decays.</param>
/// <param name="BaseMovesMax">
/// The <c>10</c> of <c>10 − min(<see cref="MovesReductionCap"/>, troops / <see cref="MovesTroopDivisor"/>)</c>,
/// recomputed every turn before the decay penalty — not accumulated from the previous turn's value.
/// </param>
/// <param name="MovesTroopDivisor">
/// The troop divisor of the base-moves formula above. Numerically the same <c>20000</c> as
/// <see cref="SupplyConsumptionRules.ConsumptionDivisor"/> in the source pseudocode (both are literally
/// <c>troops / 20000</c> in <c>FUN_004514ec</c>), kept as a separate field because the two formulas are
/// conceptually distinct and either could be re-tuned independently by a future ruleset.
/// </param>
/// <param name="MovesReductionCap">The <c>5</c> cap on how much the base-moves formula can reduce moves by.</param>
public sealed record SupplyMoraleRules(
    int DecayThresholdPercent,
    int DeadBandUpperPercent,
    int DecayAmount,
    int MoraleFloor,
    int RegenAmount,
    int MoraleCeiling,
    int MovesPenaltyOnDecay,
    int BaseMovesMax,
    int MovesTroopDivisor,
    int MovesReductionCap,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>
/// The confirmed weather-event frequency curve (<c>decompiled-weather-events.md</c>): a per-season,
/// per-part-of-season odds table, data-driven so its <c>[designed]</c> effects table can grow without an
/// engine change. Only the frequency is <c>[confirmed]</c>; what an event actually does is <c>[designed]</c>
/// (<c>docs/game-design.md</c> §Economy: "effects not fully decompiled").
/// </summary>
/// <param name="EarlyLateWeekThreshold">
/// Below this week number a season is in its "early" part; at or above it, "late". The report gives
/// Spring's boundary as week 6 and Autumn's as week 7; both are represented by this single field
/// because on this engine's odd-week calendar (weeks 1, 3, 5, 7, 9, 11) the two boundaries select the
/// same weeks either way — see <c>toy-ruleset.json</c>'s <c>economy.weather._provenance</c> for the
/// full 6/7 citation. Summer and Winter's <see cref="WeatherSeasonOdds.EarlyNumerator"/>/
/// <see cref="WeatherSeasonOdds.EarlyDenominator"/> equal their late-part odds, since the source report
/// gives them one flat rate for the whole season.
/// </param>
/// <param name="BySeason">One entry per <see cref="Model.CalendarState.SeasonIndex"/>.</param>
/// <param name="LocationCount">
/// Review round 1, B4: the report's frequency curve is a per-location roll, made independently for
/// each of this many tracked locations every tick (<c>DAT_00479540</c>), not one roll per tick. Rolling
/// once per tick understated the confirmed absolute frequency by this factor (the Winter/Summer
/// <em>ratio</em> DoD 8 checks was unaffected, since both seasons were understated equally). The
/// locations' own identity is <c>[open]</c>, exactly as the report leaves it — this field only fixes
/// the roll count.
/// </param>
/// <param name="Effects">
/// The data-driven effects table a fired event draws from — placeholder entries, each individually
/// <c>_provenance</c>-tagged <c>designed</c>, since the report identifies the frequency curve but not the
/// effects themselves (<c>docs/game-design.md</c> §Economy).
/// </param>
public sealed record WeatherEventRules(
    int EarlyLateWeekThreshold,
    ValueList<WeatherSeasonOdds> BySeason,
    int LocationCount,
    ValueList<WeatherEffectRule> Effects,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>One season's weather-event odds, confirmed as an <c>numerator</c>-in-<c>denominator</c> roll.</summary>
public sealed record WeatherSeasonOdds(
    int SeasonIndex,
    int EarlyNumerator,
    int EarlyDenominator,
    int LateNumerator,
    int LateDenominator);

/// <summary>One entry in the weather-effects table a fired event draws from.</summary>
/// <param name="Id">Stable key, e.g. <c>"storm-damages-fleet"</c>.</param>
public sealed record WeatherEffectRule(
    string Id,
    string Description,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>Standing recruitment and the mercenary economy.</summary>
public sealed record RecruitmentRules(
    int TroopsPerCostUnit,
    int MercenaryPoolSlots,
    int MercenaryHireTroopDivisor,
    int MercenaryUpkeepQualityDivisor,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>Join/split caps and what a newly split army starts with.</summary>
public sealed record ArmyManagementRules(
    int MaxUnitsPerArmy,
    int MaxTroopsPerArmy,
    int MaxArmies,
    int SplitMinUnits,
    int NewArmyMorale,
    int NewArmyMovesHumanSeat,
    int NewArmyMovesAiSeat,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>
/// Fleet construction, transport, repair, fleet management, and the per-turn at-sea attrition pass —
/// <c>docs/task-catalogue.md</c> "T14 Naval". The attrition fields below are additive to the record T02
/// shipped: the storm-damage spiral, the zero-supply penalty and the moves formula
/// (<c>docs/design-audit.md</c> §2.9a, <c>docs/investigations/thracia-supply-morale.md</c> §"Fleets",
/// <c>[confirmed]</c> from code and empirically on the <c>1_cartago_271_*</c> series unless noted
/// otherwise). This rule is <strong>structurally analogous to <see cref="SupplyMoraleRules"/> and must
/// not share an implementation with it</strong> — different trigger (supply exactly <c>0</c> here, a
/// percentage there), different decay (<c>−random(0..1)</c> here, a deterministic <c>−2</c> there), no
/// floor here versus a hard 51 there, no free regeneration here versus a free <c>+1</c>/turn there,
/// at-sea-only here versus always there, and lethal here versus survivable there. See the comparison
/// table in <c>docs/investigations/thracia-supply-morale.md</c>.
/// </summary>
/// <param name="StormDamageRandomDivisor">
/// The <c>10</c> of <c>dmg = max(1, random(100 − condition) / 10)</c> — the storm pass's base roll,
/// unconditional and not supply-driven, run on every launched (at-sea) fleet every turn. It scales with
/// damage already taken, which is what makes naval attrition a death spiral rather than a linear
/// decline.
/// </param>
/// <param name="StormWinterDamageMultiplier">Doubles the roll in Winter, before the coast test.</param>
/// <param name="StormWinterDamageCap">The cap the Winter doubling is held to (5), before the coast test.</param>
/// <param name="StormTripleDamageMultiplier">
/// Triples the roll (instead of doubling it) when <see cref="StormTripleConditionTileCode"/>'s predicate
/// holds, before the coast test.
/// </param>
/// <param name="StormTripleDamageCap">The cap the tripling is held to (8), before the coast test.</param>
/// <param name="StormTripleConditionTileCode">
/// <c>[derived]</c>: <c>FleetRecord +24 == 1</c> selects the tripling branch instead of the doubling one.
/// <c>docs/investigations/thracia-supply-morale.md</c> §"Still open" is explicit this predicate is
/// inferred from magnitudes in the <c>1_cartago_271_*</c> series, not decompiled — the series is
/// consistent with the doubling branch being active throughout but cannot prove which predicate selected
/// it. <c>+24</c> is otherwise confirmed as the fleet's covered-map-cell field
/// (<see cref="Model.FleetState.CoveredTileCode"/>), so this reuses that already-modelled field rather
/// than inventing a new one; only the trigger value (which terrain code the tripling keys on) is
/// undecompiled. Named and ruleset-driven per <c>docs/task-catalogue.md</c> T14's instruction to
/// implement this as a named value defaulting to the reports' stated behaviour, not to escalate.
/// </param>
/// <param name="StormAwayFromCoastDamageMultiplier">
/// <c>dmg = dmg × 2 + 1</c> away from friendly coast (<c>[derived]</c>: the away-from-coast test itself,
/// <c>FUN_004494e4</c>, is inferred from magnitudes, not decompiled — see
/// <see cref="FriendlyCoastRadiusTiles"/>). Always makes the result odd on this branch.
/// </param>
/// <param name="StormAwayFromCoastDamageAddend">The <c>+ 1</c> of the same expression.</param>
/// <param name="StormNearCoastDamageDivisor">Halves the roll instead, next to friendly coast.</param>
/// <param name="StormWinterSpikeChanceDenominator">
/// The away-from-coast branch's 1-in-<c>this</c> Winter chance of spiking to
/// <see cref="StormWinterSpikeDamage"/> instead of the ordinary doubled roll.
/// </param>
/// <param name="StormWinterSpikeDamage">The spike value itself (30).</param>
/// <param name="StormShipLossDamageThreshold">
/// At or above this <c>dmg</c> (6), the storm costs ships as well as condition, through
/// <see cref="StormShipLossRatioBase"/>/<see cref="StormShipLossRatioScale"/>/<see cref="StormShipLossDivisor"/>;
/// below it, only <see cref="Model.FleetState.ConditionPercent"/> is reduced, by <c>dmg</c> directly.
/// </param>
/// <param name="StormShipLossRatioBase">
/// <c>[derived]</c>: the heavier branch reuses the original's own proportional-damage function
/// (<c>FUN_0044B4F8</c>, the same one T16's naval combat calls, but a different call site: this task's
/// Owns list is <see cref="NavalRules"/> only, so the constants below are this record's own copies, not
/// <c>Model.CombatRules.NavalCombatRules</c>'s). <c>docs/investigations/thracia-supply-morale.md</c>
/// pins the call as <c>FUN_0044b4f8(fleet, 100, dmg + 100)</c> but the ship-count-never-changed Cartago
/// series never exercises it (storm damage stayed under 6 throughout), so the resulting magnitude here
/// is derived from the confirmed battle-context formula's shape, not independently save-checked for this
/// call site. This is the <c>100</c> first argument.
/// <para>
/// <strong>Open evidence conflict (round 2 review), not resolved here:</strong> with
/// <see cref="StormShipLossRatioBase"/> = <see cref="StormShipLossRatioScale"/> = 100, the minimum
/// possible heavy-branch loss (at the threshold, <c>dmg == 6</c>) is
/// <c>ships × 112 / <see cref="StormShipLossDivisor"/></c> ≈ <strong>37% of the fleet in one turn</strong> —
/// and, measured over 2,000 seeded draws away from friendly coast at condition 50, that branch fires on
/// roughly 39% of them. This is in tension with the one empirical series available: the Cartago fleet in
/// <c>1_cartago_271_*.sav</c> holds <strong>90 ships across all ten saves</strong> at conditions falling
/// from 79 to 48, away from friendly coast, over seven turns — a run this magnitude would give roughly a
/// 1-in-35 chance of surviving without a ship loss. The transcription itself is arithmetically exact
/// against the confirmed <c>r</c>/<c>d</c> formula from the battle-context call site; the tension is that
/// the one series available to check the storm call site's own magnitude against never actually took
/// this branch, so the formula's applicability here — not its transcription — is what remains unverified.
/// Resolving it needs a decompilation of <c>FUN_0044B4F8</c>'s storm call site specifically, which is out
/// of this task's scope; noted here, not in a PR body, so it survives the merge.
/// </para>
/// </param>
/// <param name="StormShipLossRatioScale">
/// The <c>100</c> divisor of <c>d = r² / <see cref="StormShipLossRatioScale"/></c>, where
/// <c>r = max(1, (dmg + 100) × 100 / <see cref="StormShipLossRatioBase"/>)</c>.
/// </param>
/// <param name="StormShipLossDivisor">
/// The <c>300</c> divisor of <c>ships × d / <see cref="StormShipLossDivisor"/></c> and
/// <c>condition × d / <see cref="StormShipLossDivisor"/></c>, the heavier branch's ship and condition
/// losses.
/// </param>
/// <param name="DeathConditionThreshold">
/// Condition below this (40) destroys the fleet — <em>"A fleet belonging to X is lost at sea."</em> —
/// checked immediately after the storm pass and before the zero-supply penalty, so a fleet the
/// zero-supply roll leaves below this threshold survives that turn and dies on the next check.
/// </param>
/// <param name="MovesBaseValue">
/// The <c>30</c> of <c>moves = 30 − (ships − <see cref="MovesShipOffset"/>) / <see cref="MovesShipDivisor"/></c>,
/// recomputed fresh every turn before any penalty, exactly like the army side's own base-moves formula.
/// </param>
/// <param name="MovesShipOffset">The <c>50</c> offset of the same expression.</param>
/// <param name="MovesShipDivisor">The <c>10</c> divisor of the same expression.</param>
/// <param name="MovesCarriedArmyTroopDivisor">
/// A carried army further reduces moves by <c>troops(carried) / this / ships + <see cref="MovesCarriedArmyAddend"/></c>.
/// </param>
/// <param name="MovesCarriedArmyAddend">The <c>+ 1</c> of the same expression.</param>
/// <param name="ZeroSupplyMovesPenalty">
/// Moves lost when supply is exactly <c>0</c> (absolute, not a percentage — the opposite trigger from
/// <see cref="SupplyMoraleRules.DecayThresholdPercent"/>'s percentage-based one).
/// </param>
/// <param name="ZeroSupplyConditionRandomBound">
/// The exclusive upper bound of the zero-supply condition roll, <c>condition −= random(0..1)</c> —
/// <c>IRng.NextInt(this)</c> draws from <c>{0, 1}</c>. No floor and no free regeneration, unlike the
/// army side's deterministic, floored, regenerating rule.
/// </param>
/// <param name="DamageSlowdownConditionThreshold">
/// Below this condition (70), damage further reduces moves by
/// <c>(this − condition) / <see cref="DamageSlowdownDivisor"/></c> — the same 70 constant that recurs as
/// the army side's morale ceiling, here marking where damage starts costing moves instead.
/// </param>
/// <param name="DamageSlowdownDivisor">
/// The divisor (4) of the damage-slowdown term above — the original's <c>&gt;&gt; 2</c>, equivalent to
/// integer division by 4 for the non-negative operand this term always has.
/// </param>
/// <param name="ConstructionTickStep">
/// The construction countdown decrements by this (2) every turn's fleet tick, not by 1 — confirmed by
/// the Caere order (24 → 12 over twelve weekly ticks, i.e. six turns) and the Macedonian order (24, 22,
/// 20, 18, 16, 14, 12 across seven saves = six turns) in
/// <c>decompiled-unit-map-orders-and-record-fields.md</c> and
/// <c>supply-driven-morale-and-fleet-attrition.md</c>. A fleet ordered with
/// <see cref="ConstructionTicks"/> (24) launches after twelve turns of this decrement, matching both
/// fixtures exactly.
/// </param>
/// <param name="FriendlyCoastRadiusTiles">
/// <c>[designed]</c>: since <c>FUN_004494e4</c> ("away from friendly coast") is not decompiled
/// (<c>docs/investigations/thracia-supply-morale.md</c> §"Still open"), this engine answers the same
/// confirmed question — is a launched fleet near a city its own nation owns — with a simple tile-radius
/// proxy rather than inventing undecompiled geometry: a fleet within this many tiles (Chebyshev distance)
/// of an owned city counts as near friendly coast (the gentler, halved-damage branch); otherwise it is
/// away from it (the doubled/tripled branch). What was searched and came up empty: no report in
/// <c>tests/fixtures/known-reports.json</c> gives <c>FUN_004494e4</c>'s actual test. This value is a
/// placeholder for that undecompiled predicate, not a resolution of it.
/// </param>
/// <param name="MarkerBandBaseTier1">
/// <c>[confirmed: decompiled-unit-map-orders-and-record-fields.md part 3]</c>: <c>FUN_0044A878</c>
/// encodes a fleet's map marker as <c>owner + band</c>, where <c>band</c> is <c>300</c> for the smallest
/// ship-count tier (below <see cref="Model.MapMarkerRules.FleetShipTierThresholds"/>'s first entry),
/// stepping up by <see cref="MarkerBandStep"/> per tier. Every band base is a multiple of 16 above 300,
/// which is why <c>(code − 300) % 16</c> recovers the owner in every band. Checked against both
/// confirmed fixtures: 90 ships (tier 3, ≥ 50) owner 1 → <c>300 + 2×16 + 1 = 333</c>; 70 ships (tier 3)
/// owner 3 → <c>300 + 2×16 + 3 = 335</c>.
/// </param>
/// <param name="MarkerBandStep">The per-tier step (16) of the same encoding.</param>
public sealed record NavalRules(
    int BuildCostPerShip,
    int OrderMinShips,
    int OrderMaxShips,
    int ConstructionTicks,
    int LaunchConditionPercent,
    int LaunchSupplyTons,
    int TransportTroopsPerShip,
    int RepairCostDivisor,
    int JoinMaxShips,
    int SplitMinShips,
    int MaxConditionPercent,
    int StormDamageRandomDivisor,
    int StormWinterDamageMultiplier,
    int StormWinterDamageCap,
    int StormTripleDamageMultiplier,
    int StormTripleDamageCap,
    int StormTripleConditionTileCode,
    int StormAwayFromCoastDamageMultiplier,
    int StormAwayFromCoastDamageAddend,
    int StormNearCoastDamageDivisor,
    int StormWinterSpikeChanceDenominator,
    int StormWinterSpikeDamage,
    int StormShipLossDamageThreshold,
    int StormShipLossRatioBase,
    int StormShipLossRatioScale,
    int StormShipLossDivisor,
    int DeathConditionThreshold,
    int MovesBaseValue,
    int MovesShipOffset,
    int MovesShipDivisor,
    int MovesCarriedArmyTroopDivisor,
    int MovesCarriedArmyAddend,
    int ZeroSupplyMovesPenalty,
    int ZeroSupplyConditionRandomBound,
    int DamageSlowdownConditionThreshold,
    int DamageSlowdownDivisor,
    int ConstructionTickStep,
    int FriendlyCoastRadiusTiles,
    int MarkerBandBaseTier1,
    int MarkerBandStep,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>The shipped instant battle resolver, plus the reserve tactical constants.</summary>
public sealed record CombatRules(
    int PowerTroopDivisor,
    int PowerDivisor,
    int WinnerCasualtyNumerator,
    int AbsorbedSupplyTroopDivisor,
    int QualityFloor,
    int QualityCap,
    int PromotionChanceDenominator,
    int UnitySwing,
    int AutoPeaceChanceNumerator,
    int AutoPeaceChanceDenominator,
    int AutoPeaceLoserUnityThreshold,
    int AutoPeaceLoserCityThreshold,
    NavalCombatRules Naval,
    ScatteredDefeatRules ScatteredDefeat,
    DetailedResolverRules DetailedResolver,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>The naval variant of the instant resolver.</summary>
/// <param name="UnitySwingShipDivisor">
/// A naval battle moves unity by <c>floor(loserShips / this)</c>, rather than by the field battle's
/// flat <see cref="CombatRules.UnitySwing"/>.
/// </param>
public sealed record NavalCombatRules(
    int ConditionDivisor,
    int CarriedArmyPowerDivisor,
    int RandomBandCount,
    int RandomBandPercent,
    int WinnerDamageDivisor,
    int DamageRatioScale,
    int UnitLossDamageThreshold,
    int UnitLossDivisor,
    int UnitySwingShipDivisor,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>
/// The <c>improved</c> ruleset's alternative to annihilating the loser: it survives at reduced
/// strength and scatters. Selected by <see cref="RulesetFlags.CombatOnDefeat"/>.
/// </summary>
public sealed record ScatteredDefeatRules(
    int SurvivorCasualtyNumerator,
    int ScatterTilesMin,
    int ScatterTilesMax,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>
/// Constants for the optional "detailed" resolver held in reserve by
/// <c>docs/game-design.md</c> §Combat. Not used by the shipped instant resolver; carried here so that
/// adding the reserve resolver later is a ruleset change, not a model change.
/// </summary>
public sealed record DetailedResolverRules(
    int MeleeLossCapPercent,
    int MeleeLossCapOffset,
    int MeleeLossHardCap,
    int MeleeBasePowerFloor,
    int MeleePowerDivisor,
    int InRangeShotMultiplier,
    ValueList<string> TypeEffectivenessOrder,
    ValueList<ValueList<int>> TypeEffectiveness,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>
/// Siege resolution — the third variant of the instant resolver. The defender-strength sum, decompiled
/// directly from <c>FUN_0044A98C</c> (T31, correcting T02's field identities, which had been transcribed
/// from a report that guessed at this function rather than decompiling it), is
/// <c>loyalty × <see cref="DefenderLoyaltyWeight"/> + finishedFortificationPercent ×
/// <see cref="DefenderFortificationWeight"/> + populationThousands × <see cref="DefenderPopulationWeight"/></c>,
/// then, in order: <c>× <see cref="HighLoyaltyBonusNumerator"/> / <see cref="HighLoyaltyBonusDenominator"/></c>
/// when the city is its controlling nation's capital (<c>FUN_0044B8D0</c>) <em>and</em> its loyalty exceeds
/// <see cref="HighLoyaltyThreshold"/>, then <c>× <see cref="DefenderNonAllegiantNumerator"/> /
/// <see cref="DefenderNonAllegiantDenominator"/></c> when the city's owner is not its allegiance — both
/// truncating divisions, applied in that order (T33, <c>docs/investigations/siege-defender-strength.md</c>).
/// The fortification term is the city's stored fortification word decoded through
/// <see cref="FortificationCode.FinishedPercent"/> — the guarded <c>code &gt; MaxPercent ? code % radix :
/// code</c> — never the raw stored word and never an unguarded <c>% 100</c>. The raw word is wrong for a
/// city with a fortification order in progress: it stores <c>finishedPercent + pendingPoints × radix</c>
/// (e.g. 250 for 50% finished with an order pending), so reading it raw overstates the finished amount by
/// a full order. An unguarded <c>% 100</c> is wrong at exactly one point instead: a fully-finished city
/// stores 100, and <c>100 % 100 = 0</c> would silently turn a finished 100% fortification into 0%. See
/// <c>docs/investigations/siege-defender-strength.md</c> for the full decompilation and the panel-based
/// (<c>TInformation_ShowCityDetails</c>, <c>0x0043BE5C</c>) field-identity evidence.
/// </summary>
/// <param name="DefenderFortificationWeight">
/// The weight of the city's finished-fortification-percent term in the defender-strength sum
/// (<c>FUN_0044A98C</c>). Corrected by T31 from T02's swapped 150 to the function's actual 250 — see
/// <c>docs/investigations/siege-defender-strength.md</c>.
/// </param>
/// <param name="DefenderLoyaltyWeight">
/// The weight of the city's loyalty term in the defender-strength sum (<c>FUN_0044A98C</c>). Corrected
/// by T31 from T02's swapped 250 to the function's actual 150 — see
/// <c>docs/investigations/siege-defender-strength.md</c>.
/// </param>
/// <param name="DefenderPopulationWeight">
/// The weight of the city's population-in-thousands term in the defender-strength sum
/// (<c>FUN_0044A98C</c>). T02 shipped this as <c>DefenderUnidentifiedFieldWeight</c> because its cited
/// report never named the field it multiplies; T31 renamed it after identifying the field directly from
/// the decompiled function and from <c>TInformation_ShowCityDetails</c>'s own <c>"Population -"</c> label
/// at <c>0x0043BE5C</c>, which prints the same <c>DAT_004795ac</c> read. The weight (200) is unchanged.
/// </param>
/// <param name="HighLoyaltyThreshold">
/// The loyalty value above which the defender's strength is scaled up
/// (<c>0x3b &lt; (short)(&amp;DAT_004795a6)[city*0x11]</c>, i.e. loyalty &gt; 59) — <strong>not</strong>
/// fortification. T02 shipped this as <c>HighFortificationThreshold</c> because its cited report
/// (<c>decompiled-city-capture-resolution.md</c>) named the gated field as fortification without having
/// decompiled <c>FUN_0044A98C</c> itself; T33 (<see href="https://github.com/diegoami/imperial_conquest_2/issues/46">issue #46</see>)
/// renamed it after decompiling the function directly. The branch is additionally guarded by
/// <c>FUN_0044B8D0</c> — whether the city is its controlling nation's capital — independently confirmed by
/// <c>TInformation_ShowCityDetails</c>'s own <c>"  (capital of …)"</c> panel branch, gated on the same
/// function. See <c>docs/investigations/siege-defender-strength.md</c>.
/// </param>
/// <param name="HighLoyaltyBonusNumerator">
/// The numerator of the <c>× 5 / 3</c> bonus gated by <see cref="HighLoyaltyThreshold"/> and the capital
/// predicate. Renamed alongside <see cref="HighLoyaltyThreshold"/>; the value (5) is unchanged.
/// </param>
/// <param name="HighLoyaltyBonusDenominator">
/// The denominator of the same <c>× 5 / 3</c> bonus. Renamed alongside <see cref="HighLoyaltyThreshold"/>;
/// the value (3) is unchanged.
/// </param>
/// <param name="DefenderNonAllegiantNumerator">
/// The numerator of the penalty <c>FUN_0044A98C</c> applies when the city's owner is not its allegiance:
/// <c>iVar4 = (iVar4 &lt;&lt; 2) / 5</c>, i.e. <c>strength × 4 / 5</c>. T02 shipped this as
/// <c>DefenderOwnerNotAllegiancePenaltyPercent</c> (20, read as "subtract 20%"), which does not match the
/// function's actual operation — a <c>× 4/5</c> truncates differently than a 20%-subtraction reading at
/// some input values, because <c>(x &lt;&lt; 2) / 5</c> and <c>x - x/5</c> round down at different points
/// for the same <c>x</c> (e.g. <c>x = 9</c>: <c>(9*4)/5 = 7</c>, not the 8 a 20% subtraction would give).
/// Corrected by T33 (<see href="https://github.com/diegoami/imperial_conquest_2/issues/47">issue #47</see>).
/// See <c>docs/investigations/siege-defender-strength.md</c>.
/// </param>
/// <param name="DefenderNonAllegiantDenominator">
/// The denominator of the same <c>× 4/5</c> penalty. See <see cref="DefenderNonAllegiantNumerator"/>.
/// </param>
/// <param name="AttackerIsAllegianceDefenderReductionPercent">
/// A separate <c>× 9/10</c> reduction applied at the siege entry point (<c>FUN_0044B27C</c>, outside this
/// function) when the <em>attacking</em> nation equals the city's allegiance. T33 confirmed this is a
/// second, genuinely separate adjustment from <see cref="DefenderNonAllegiantNumerator"/>'s
/// owner-vs-allegiance penalty — the two run in two different functions and both apply
/// (<c>docs/investigations/siege-defender-strength.md</c>) — closing <c>design-audit.md</c> §2.13's open
/// question of whether they were the same adjustment described twice. This field is T17's to apply and is
/// otherwise untouched by T33.
/// </param>
public sealed record SiegeRules(
    int ArcherStrengthMultiplier,
    int PowerDivisor,
    int DefenderFortificationWeight,
    int DefenderLoyaltyWeight,
    int DefenderPopulationWeight,
    int HighLoyaltyThreshold,
    int HighLoyaltyBonusNumerator,
    int HighLoyaltyBonusDenominator,
    int DefenderGarrisonTroopDivisor,
    int DefenderNonAllegiantNumerator,
    int DefenderNonAllegiantDenominator,
    int AttackerIsAllegianceDefenderReductionPercent,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>Loyalty floors and tiering.</summary>
public sealed record LoyaltyRules(
    int ForcedCaptureFloor,
    int DefectionFloor,
    int AllegiantRecaptureTarget,
    int TierDivisor,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>The numeric codes the relation matrix stores for each diplomatic state.</summary>
public sealed record RelationStateCodes(int Peace, int Trade, int Alliance, int War);

/// <summary>The confirmed diplomatic state machine, its cooldowns, and the reparation formula's constants.</summary>
public sealed record DiplomacyRules(
    RelationStateCodes StateCodes,
    int CooldownAfterBrokenTrade,
    int CooldownAfterBrokenAlliance,
    int CooldownAfterEndedWar,
    int CooldownAfterPeaceTerms,
    int CooldownAfterAllyPeace,
    int ThawPerQuarter,
    int ThawBonus,
    int ThawBonusChanceDenominator,
    int MaxTradePartners,
    int FaithfulThawColumnLimit,
    int ReparationsWealthDivisor,
    int ReparationsPerCity,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>
/// The generic, data-driven city-orders table (<c>docs/design-audit.md</c> §3 Q7): a list, with
/// fortification as the only shipped entry rather than the only possible one.
/// </summary>
public sealed record CityOrderRules(
    ValueList<CityOrderRule> Orders,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>
/// One city order.
/// </summary>
/// <param name="Id">Stable key, e.g. <c>"fortify"</c>.</param>
/// <param name="MaxPercent">The completed value the order may not exceed.</param>
/// <param name="InProgressEncodingRadix">
/// The multiplier the original writes a pending order with (<c>value += points × radix</c>), which is
/// also the threshold above which the stored word means "in progress" rather than "finished".
/// See <see cref="FortificationCode"/>.
/// </param>
/// <param name="CostPerPointPerPopulationThousand">Talents charged per point, per thousand population.</param>
/// <param name="RefusedWhileUnderSiege">Whether the order may be issued for a besieged city.</param>
/// <param name="WipedBySiegeAttempt">Whether a siege attempt clears a pending order.</param>
public sealed record CityOrderRule(
    string Id,
    int MaxPercent,
    int InProgressEncodingRadix,
    int CostPerPointPerPopulationThousand,
    bool RefusedWhileUnderSiege,
    bool WipedBySiegeAttempt,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>The news log's ring-buffer geometry, slot size and round-header season names.</summary>
/// <param name="RingBufferSlots">The ring buffer's capacity, 40 <c>[confirmed]</c>.</param>
/// <param name="MessageByteLength">
/// The slot's own size in bytes -- 61 <c>[confirmed]</c>. The slot is a NUL-terminated single-byte
/// string, so the writer reserves its last byte for the NUL: at most <c>MessageByteLength - 1</c>
/// bytes of text ever reach the log (<c>news-log-format-and-messages.md</c> Q1, confirmed against
/// 2,101 slots in 54 saves, every one NUL-terminated, longest 59).
/// </param>
/// <param name="SeasonNames">
/// The four season names, in <see cref="CalendarState.SeasonIndex"/> order (0 = Spring), for the round
/// tick's week header (<c>news-log-format-and-messages.md</c> Q3: "Season names are the DAT's season
/// table at 0x1F7D8: Spring, Summer, Autumn, Winter" -- the same table
/// <c>CalendarRules.StartSeasonIndex</c>'s own provenance and <c>EconomyRules</c>'s seasonal supply
/// table already index the same way).
/// </param>
public sealed record NewsLogRules(
    int RingBufferSlots,
    int MessageByteLength,
    ValueList<string> SeasonNames,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>
/// Size tiering for map markers. The original encodes owner <em>and</em> size in its marker codes;
/// these are the size-band thresholds, which the renderer turns into per-tier icons.
/// </summary>
public sealed record MapMarkerRules(
    ValueList<int> ArmyTroopTierThresholds,
    ValueList<int> FleetShipTierThresholds,
    ValueList<int> CityPopulationTierThresholds,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>Which victory condition a scenario gets when it does not state one, and the hard end year.</summary>
public sealed record VictoryRules(
    VictoryConditionType DefaultCondition,
    bool TotalConquestRequiresEveryCity,
    int HardEndYearBc,
    int? DefaultTurnLimit,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>
/// The formula-variant selectors — <c>docs/game-design.md</c> §"Two shipped presets, not a pile of
/// independent flags". Each one picks between the original's confirmed behaviour and the designed
/// alternative; the two shipped presets are just two settings of this record.
/// </summary>
public sealed record RulesetFlags(
    DiplomacyModel DiplomacyModel,
    EconomyPurseModel EconomyPurses,
    SeatAsymmetryModel SeatAsymmetry,
    DiplomaticThawPolicy BugPolicyDiplomaticThaw,
    DefeatOutcome CombatOnDefeat,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>How faithfully diplomacy follows the original (audit Q3).</summary>
public enum DiplomacyModel
{
    /// <summary>The confirmed state machine only.</summary>
    ConfirmedStateMachine,

    /// <summary>The confirmed state machine plus an AI opinion-score layer on top of it.</summary>
    ConfirmedStateMachineWithOpinionScore,
}

/// <summary>Where supply and mercenary purchases are paid from (audit Q4).</summary>
public enum EconomyPurseModel
{
    /// <summary>Per-army and per-fleet money purses, as the original has.</summary>
    PerUnitPurses,

    /// <summary>Centralised to the national treasury.</summary>
    CentralTreasury,
}

/// <summary>Whether the original's human-versus-AI rule differences are reproduced (audit Q6).</summary>
public enum SeatAsymmetryModel
{
    /// <summary>Reproduce every confirmed seat-type asymmetry.</summary>
    Faithful,

    /// <summary>Apply the same rule to every seat, human or AI.</summary>
    Normalized,
}

/// <summary>Whether the original's 8-column diplomatic-thaw bug is reproduced (audit Q8).</summary>
public enum DiplomaticThawPolicy
{
    /// <summary>Thaw only the first N relation columns, reproducing the original's bug.</summary>
    ReproduceEightColumnBug,

    /// <summary>Thaw every relation column.</summary>
    ThawAllColumns,
}

/// <summary>What happens to the losing side of a field or naval battle.</summary>
public enum DefeatOutcome
{
    /// <summary>Destroyed outright, as the original's instant resolver does.</summary>
    Destroyed,

    /// <summary>Survives at reduced strength and scatters away from the victor.</summary>
    Scatter,
}

/// <summary>The shipped victory-condition kinds.</summary>
public enum VictoryConditionType
{
    /// <summary>Hold every city on the map — the original's only win.</summary>
    TotalConquest,

    /// <summary>Hold every city belonging to nations still at war with you.</summary>
    Domination,

    /// <summary>Highest weighted score when the turn limit is reached.</summary>
    ScoreAtTurnLimit,

    /// <summary>A goal stated by the scenario itself.</summary>
    Custom,
}
