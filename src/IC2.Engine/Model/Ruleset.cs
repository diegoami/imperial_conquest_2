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
/// <see cref="SiegeRules"/>. Every other field, and every other record in this file, is unchanged.
/// </remarks>
public sealed record EconomyRules(
    int TaxRateDivisor,
    int ShipUpkeepPerQuarter,
    int UnityDecayPerQuarter,
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

/// <summary>Fleet construction, transport, repair and fleet management.</summary>
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
/// <see cref="DefenderFortificationWeight"/> + populationThousands × <see cref="DefenderPopulationWeight"/></c>.
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
/// <param name="HighFortificationThreshold">
/// The fortification value above which the defender's strength is scaled up. The decompiled branch is
/// additionally guarded by a condition that was not recovered, which
/// <see cref="HighFortificationBonusNumerator"/>'s provenance records.
/// </param>
public sealed record SiegeRules(
    int ArcherStrengthMultiplier,
    int PowerDivisor,
    int DefenderFortificationWeight,
    int DefenderLoyaltyWeight,
    int DefenderPopulationWeight,
    int HighFortificationThreshold,
    int HighFortificationBonusNumerator,
    int HighFortificationBonusDenominator,
    int DefenderGarrisonTroopDivisor,
    int DefenderOwnerNotAllegiancePenaltyPercent,
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

/// <summary>The news log's ring-buffer geometry.</summary>
public sealed record NewsLogRules(
    int RingBufferSlots,
    int MessageByteLength,
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
