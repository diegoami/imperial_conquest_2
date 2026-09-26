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
    CaptureRules Capture,
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

    /// <summary>
    /// Checks that <see cref="NewsLogRules.SeasonNames"/> has exactly one name per season the
    /// calendar defines, throwing if not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Folded follow-up <see href="https://github.com/diegoami/imperial_conquest_2/issues/99">#99</see>
    /// (<c>docs/task-catalogue.md</c> T29 DoD 10): a ruleset whose <c>newsLog.seasonNames</c> list is
    /// shorter or longer than <see cref="CalendarRules.SeasonsPerYear"/> used to crash the first time
    /// the round-tick header indexed it by season, rather than failing when the file was loaded. This
    /// method is that check, made callable at load time.
    /// </para>
    /// <para>
    /// This method lives in <c>Model</c> and knows nothing of a document path, deliberately: the
    /// original Owns grant for this task reached only <c>src/IC2.Engine/Model/**</c>, and the natural
    /// call site for a cross-field check like this — <c>GameDataValidation.ValidateRuleset</c>, beside
    /// its neighbour <c>ValidateCalendar</c> — was out of reach. A property- or type-level
    /// <see cref="System.Text.Json.Serialization.JsonConverterAttribute"/> on <see cref="Ruleset"/> was
    /// considered and rejected as a way to wire this from inside <c>Model</c> alone: it would make
    /// <c>JsonContract.For(typeof(Ruleset))</c> return <see langword="null"/> (see
    /// <c>JsonContract.Build</c>'s own "opaque to the schema walk" remark), silently disabling
    /// <c>SchemaValidator</c>'s missing/unknown-field checks for every other <see cref="Ruleset"/>
    /// field — a regression judged worse than an incomplete DoD. The Owns list was widened by the user
    /// to grant <c>GameDataValidation.cs</c> exactly the one call this needs
    /// (<c>docs/task-catalogue.md</c> T29 DoD 10); <c>GameDataValidation.ValidateNewsLogSeasonNames</c>
    /// is that call, wrapping this method's <see cref="InvalidOperationException"/> as a
    /// <c>MalformedGameDataException</c> naming the document, exactly like <c>ValidateCalendar</c>
    /// already does for its own checks — so a bad file now fails at load, not at the first round tick.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <see cref="NewsLogRules.SeasonNames"/>'s length does not equal
    /// <see cref="CalendarRules.SeasonsPerYear"/>. The message names both counts;
    /// <c>GameDataValidation.ValidateNewsLogSeasonNames</c> adds the document path, the same way
    /// every other <c>GameDataValidation</c> check does.
    /// </exception>
    public void ValidateSeasonNames()
    {
        if (NewsLog.SeasonNames.Count != Calendar.SeasonsPerYear)
        {
            throw new InvalidOperationException(
                $"newsLog.seasonNames has {NewsLog.SeasonNames.Count} entries but "
                + $"calendar.seasonsPerYear is {Calendar.SeasonsPerYear} — every season needs exactly one name.");
        }
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
/// <para>
/// <strong>T22's one additive field</strong> (<c>docs/task-catalogue.md</c> "T22 AI", Done-when 5, and
/// the only <c>EconomyRules</c> change that task's Owns list grants):
/// <see cref="AutoResupplyRadiusTiles"/>. Every AI turn, each AI army runs
/// <see cref="IC2.Engine.Economy.AutomaticResupply.ForArmy"/> against every non-hostile city within this
/// many tiles, and each AI fleet runs the fleet twin — <c>FUN_0044F31C</c> calling <c>FUN_0044E41C</c>,
/// <c>supply-capacity-rounding.md</c>. The radius is <em>data</em>, not a literal, because the AI pass is
/// the one caller that ranges beyond the supply dialog's own confirmed one-tile provider radius, and
/// because the two existing hardcoded radii (<c>BuySupplyCommandHandler.cs:101</c> and its fleet twin)
/// are precedent for a defect rather than a pattern to copy. Deliberately <em>not</em> merged with
/// <see cref="ThreatenedCityAdjacencyRadius"/>, whose value happens to be 1: that is the city-threat
/// test's own eight-neighbourhood from <c>city-population-growth.md</c>, a different rule that must be
/// able to change independently — the same reasoning already given for
/// <see cref="PopulationGrowthMobilizationDivisor"/>.
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
/// <para>
/// <strong>T39's additions</strong> (<c>docs/task-catalogue.md</c> "T39 Quarterly upkeep: who pays,
/// mercenary desertion, and deposition for debt"), transcribed from
/// <c>upkeep-payment-and-desertion.md</c>. The old <c>UnpaidUpkeepTroopLossDivisor</c> field and the
/// "mutiny" rule it fed (bug <c>#80</c>) are removed outright — the original has no such rule.
/// <see cref="MercenaryDesertionSupplyDivisor"/> is the <c>troops div 100</c> a deserting mercenary
/// takes from the army's supplies on its way out; numerically the same shape as
/// <see cref="ArmySupplyTonsPerTroops"/> but a separate field, since one is a capacity and the other a
/// loss-on-desertion divisor, the same reasoning already given for <see cref="PopulationGrowthMobilizationDivisor"/>.
/// The debt line is <c>treasury &lt; −(wealth / <see cref="DebtWealthDivisor"/>)</c>, or
/// <c>treasury &lt; <see cref="DebtTreasuryFloor"/></c>, or <c>unity &lt; <see cref="DebtUnityThreshold"/></c>.
/// Deposition (<c>FUN_0044c8f0</c>) fires for an in-debt AI nation with a
/// 1-in-<see cref="DepositionRandomDivisor"/> quarterly chance, or deterministically for a human nation
/// at the start of its turn, and its effects are shared by both: <c>unity = max(unity, min(<see cref="DepositionUnityCeiling"/>,
/// unity + <see cref="DepositionUnityGainAmount"/>))</c>; <c>treasury = treasury &lt; 0 ? 0 : treasury +
/// <see cref="DepositionTreasuryCredit"/></c>; and any relation from <see cref="DepositionRelationResetThreshold"/>
/// up to (but not including) zero resets to zero.
/// </para>
/// <para>
/// <strong>T70's one additive field</strong> (<c>docs/tasks/T70.md</c> "T70 Command-layer hygiene: one
/// distance metric, one rejection file, and the naming filter", Done-when 6b, folded bug #345):
/// <see cref="CommandAdjacencyRadiusTiles"/>. <c>decompiled-mobilization-and-mercenary-restock.md</c>
/// ~:143–144 <strong>[confirmed]</strong> is the mobilization receiving-army test — <c>FUN_00449018</c> is
/// Chebyshev distance, and the test itself reads <c>d == 1</c>, not <c>d &lt;= 1</c> — which already has
/// its own field, <see cref="RecruitmentRules.MobilizationReceivingArmyRangeHumanSeat"/>, compared with
/// <c>==</c>. This field takes the SAME one-tile value, compared with <c>&lt;=</c>, for a different rule:
/// the hardcoded <c>&gt; 1</c> literal that recurred, unexamined, across seven comparisons in five command
/// files this task's Done-when 4 moved onto <see cref="Naval.LandingTile.ChebyshevDistance"/> — a fleet
/// buying supply from a city or another fleet (<c>BuyFleetSupplyCommandHandler</c>, two sites), an army
/// buying supply from a foreign city or a fleet (<c>BuySupplyCommandHandler</c>, two sites), a fleet's
/// repair or scuttle at one of its own nation's cities (<c>RepairFleetCommandHandler</c>,
/// <c>ScuttleFleetCommandHandler</c>), and an army's named landing tile relative to its carrying fleet
/// (<c>DisembarkArmyCommandHandler</c>). The four supply-purchase sites' own direct source is
/// <c>supply-capacity-rounding.md</c>:33 (<c>TAFSupply_FindProviders</c>, "every city within one tile");
/// the mobilization citation is kept because it is a confirmed <c>d == 1</c> Chebyshev
/// comparison, cited above — described here as what it actually is, not as "the original's own general
/// command-adjacency test" an earlier revision of this remark called it (review round 1, N2); T70
/// Done-when 6b recorded the choice, but is not itself the reason — a task entry is not evidence
/// (review round 1, B5). Follow-up #351 R2: a later revision then said the citation was kept because
/// T04's fixtures corpus id for this field "prescribes" it — backwards; the corpus id (below) is where
/// the citation is tracked, not a rule that dictates it. T04 fixtures corpus id: 'command.adjacencyRadiusTiles'.
/// Deliberately not merged
/// with <see cref="AutoResupplyRadiusTiles"/> (4 — a different value, not the same one) or
/// <see cref="ThreatenedCityAdjacencyRadius"/> (1 — the same value, but a different rule, the city-threat
/// eight-neighbourhood): each must be able to change independently, the same reasoning already given
/// above for <see cref="PopulationGrowthMobilizationDivisor"/> (review round 1, B1: an earlier revision of
/// this paragraph wrongly said both sibling fields "happen to be 1"). The shipped value (1) does not
/// change; only the five files' seven hardcoded comparisons do (review round 1, B1: an earlier revision
/// said "five hardcoded copies").
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
    int MercenaryDesertionSupplyDivisor,
    int DebtWealthDivisor,
    int DebtTreasuryFloor,
    int DebtUnityThreshold,
    int DepositionRandomDivisor,
    int DepositionUnityGainAmount,
    int DepositionUnityCeiling,
    int DepositionTreasuryCredit,
    int DepositionRelationResetThreshold,
    int LowTaxLoyaltyThresholdPercent,
    int LowTaxLoyaltyCityThreshold,
    int RebellionLoyaltyThreshold,
    SupplyConsumptionRules SupplyConsumption,
    SupplyMoraleRules SupplyMorale,
    WeatherEventRules Weather,
    int SupplyDialogArmyCapacityBonus,
    int AutoResupplyPurseTopUpThreshold,
    int AutoResupplyPurseTopUpAmount,
    int AutoResupplyRadiusTiles,
    int CommandAdjacencyRadiusTiles,
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

/// <summary>Standing recruitment, mobilization, and the mercenary economy.</summary>
/// <param name="MaxSlots">
/// The size of a nation's recruitment table — <c>40</c>. The original's table is a <em>compacted
/// list</em>, not a sparse array: <c>FUN_0044a610</c> deletes a slot by shifting every later slot down
/// one and zeroing slot 39, and <c>TArmyRecruits_RecruitUnit</c> takes the first slot whose troops are
/// <c>0</c> and refuses outright when slot 39 is occupied, with the literal <em>"You have reached your
/// limit of 40 units."</em> <strong>[confirmed: decompiled-mobilization-and-mercenary-restock.md §1]</strong>.
/// <see cref="Model.NationState.RecruitmentSlots"/> models that compaction directly — an empty slot is
/// simply absent — so "slot 39 occupied" is exactly "the list already holds <see cref="MaxSlots"/>
/// entries".
/// </param>
/// <param name="MobilizationQualityDivisor">
/// The <c>4</c> of <c>quality = stateCode / 4</c>, the permanent per-unit quality a mobilized recruit is
/// born with <strong>[confirmed: decompiled-mobilization-and-mercenary-restock.md §1, §2]</strong>. The
/// division truncates toward zero, exactly as the original's <c>if (v &lt; 0) v += 3; v &gt;&gt; 2</c>
/// idiom does. Against the DAT's own quality-name table at <c>0x1F6CA</c> (11-byte stride) — indices
/// 0–3 all reading <c>not ready</c>, 4 <c>very poor</c>, 5 <c>poor</c>, 6 <c>average</c>, 7 <c>good</c>,
/// 8 <c>very good</c>, 9 <c>elite</c> — this is what makes
/// <see cref="MobilizationMinStateCodeHumanSeat"/> readable: mobilizing early is permanently worse.
/// </param>
/// <param name="MobilizationMinStateCodeHumanSeat">
/// The lowest <see cref="Model.RecruitmentSlot.StateCode"/> a human seat may mobilize — <c>16</c>, the
/// dialog's own <c>if (0xf &lt; slot.state)</c> gate
/// <strong>[confirmed: decompiled-mobilization-and-mercenary-restock.md §2]</strong>. With
/// <see cref="MobilizationQualityDivisor"/> it says "quality &gt;= 4", i.e. "the slot no longer reads
/// <c>not ready</c>".
/// </param>
/// <param name="MobilizationMinStateCodeAiSeat">
/// The lowest <c>StateCode</c> an AI seat may mobilize — <c>24</c>, and the original's own test is
/// <c>slot.state == 0x18</c>, an equality against <see cref="CalendarRules.CityUnitStateCodeCap"/>
/// <strong>[confirmed: decompiled-mobilization-and-mercenary-restock.md §4]</strong>. Modelled as a
/// minimum rather than an equality because <see cref="Calendar.CityUnitStateCode.Advance"/> holds the
/// counter <em>at</em> that cap and never above it, so on this engine the two predicates are the same
/// set; <c>MobilizationReadinessTests</c> asserts the boundary either side (22 refused, 24 accepted).
/// </param>
/// <param name="MobilizationReceivingArmyRangeHumanSeat">
/// The Chebyshev distance from the training city at which a human seat's army may receive a mobilized
/// recruit — <c>1</c>, and the original's test is <c>d == 1</c>, not <c>d &lt;= 1</c>
/// <strong>[confirmed: decompiled-mobilization-and-mercenary-restock.md §3]</strong>, so
/// <see cref="Armies.MobilizationReceivingArmy"/> compares for equality against this field, not for
/// "at most".
/// </param>
/// <param name="MobilizationReceivingArmyRangeAiSeat">
/// The same radius for an AI seat — <c>5</c>, from the original's <c>d &lt; 6</c> branch, taken when
/// the nation is computer-controlled <strong>[confirmed:
/// decompiled-mobilization-and-mercenary-restock.md §3]</strong>: "a real, asymmetric AI advantage".
/// Compared with "at most", because that branch is an inequality.
/// </param>
/// <param name="MobilizationRateOrderStep">
/// The flat <c>1</c> in <c>mobilized = min(cap, mobilized + 1 + (troops × scale) / wealth)</c>, applied
/// when a recruitment order is <em>placed</em> and symmetrically subtracted when one is cancelled
/// <strong>[confirmed: decompiled-mobilization-and-mercenary-restock.md §5]</strong>.
/// </param>
/// <param name="MobilizationRateWealthScale">
/// The <c>1000</c> that scales the same formula's troop term against
/// <see cref="Model.NationState.Wealth"/> (<c>Σ population × 3000</c>)
/// <strong>[confirmed: decompiled-mobilization-and-mercenary-restock.md §5]</strong>.
/// </param>
/// <param name="MobilizationCapPercent">
/// The ceiling that formula is clamped to — <c>100</c>, the same cap the original's dialog reports as
/// <em>"Your mobilisation rate is already 100%."</em> (T04 fixtures corpus
/// <c>recruitment.mobilizationCapPercent</c> and <c>error.mobilizationAlready100</c>)
/// <strong>[confirmed: decompiled-mobilization-and-mercenary-restock.md §5]</strong>.
/// </param>
public sealed record RecruitmentRules(
    int TroopsPerCostUnit,
    int MercenaryPoolSlots,
    int MercenaryHireTroopDivisor,
    int MercenaryUpkeepQualityDivisor,
    int MaxSlots,
    int MobilizationQualityDivisor,
    int MobilizationMinStateCodeHumanSeat,
    int MobilizationMinStateCodeAiSeat,
    int MobilizationReceivingArmyRangeHumanSeat,
    int MobilizationReceivingArmyRangeAiSeat,
    int MobilizationRateOrderStep,
    int MobilizationRateWealthScale,
    int MobilizationCapPercent,
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
/// <c>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md §"FUN_0044B5D0 and
/// FUN_0044B4F8, instruction by instruction", supply-driven-morale-and-fleet-attrition.md, research
/// 3f6ca09; bug #292, correcting this field's own earlier note]</c> The heavier branch reuses the
/// original's own proportional-damage function (<c>FUN_0044B4F8</c>, the same one the naval battle
/// calls, but a different call site: this task's Owns list is <see cref="NavalRules"/> only, so the
/// constants here are this record's own copies, not <c>Model.CombatRules.NavalCombatRules</c>'s). The
/// storm call site is <c>FUN_0044b4f8(fleet, 100, dmg + 100)</c> — <c>100</c> is the function's own
/// <em>numerator</em> argument, not its denominator: <c>r = max(1, (this × </c>
/// <see cref="StormShipLossRatioScale"/><c>) / (dmg + this))</c>, so <c>r</c> <strong>falls</strong> as
/// <c>dmg</c> rises (86 at <c>dmg</c> 7, falling to 57 at the Winter spike, <c>dmg</c> 30) — the opposite
/// of a reading that puts <c>dmg + 100</c> on top.
/// </param>
/// <param name="StormShipLossRatioScale">
/// <c>[confirmed: same source]</c> The <c>100</c> that plays two roles in the same call:
/// <see cref="StormShipLossRatioBase"/>'s own numerator role above, and <c>d = r² / this</c>'s percent
/// scale — the two happen to share a value (both are the call's literal <c>100</c>).
/// <strong>Corrects the earlier "≈ 37% minimum" remark and closes its "open evidence conflict"
/// (round-2 review).</strong> That note came from grouping <c>dmg + 100</c> as the numerator, which made
/// <c>d</c> <em>rise</em> with <c>dmg</c> and put the largest loss at the highest reachable damage. Read
/// correctly (<see cref="StormShipLossRatioBase"/>'s remarks), <c>d</c> <em>falls</em> as <c>dmg</c>
/// rises, so the <strong>largest</strong> heavy-branch loss is at the <em>lowest</em> heavy <c>dmg</c>
/// (7, just above <see cref="StormShipLossDamageThreshold"/>): <c>r = 93</c>, <c>d = 86</c>,
/// <c>ships × 86 / </c><see cref="StormShipLossDivisor"/><c> ≈ 29%</c> — falling to about 19% at the
/// Winter spike (<c>dmg</c> 30, <c>d = 57</c>). The Cartago fleet's ten saves
/// (<c>1_cartago_271_*.sav</c>, 90 ships held throughout, conditions 79 → 48, away from friendly coast)
/// never crossed the heavy threshold at all (storm damage stayed under 6 throughout that series), so
/// they are consistent with either reading and were never actually in tension with this formula; the
/// "open evidence conflict" the earlier remark recorded was an artifact of the inverted grouping, not a
/// real discrepancy, and needed no new evidence to close — only the corrected argument order (bug #292).
/// </param>
/// <param name="StormShipLossDivisor">
/// <c>[confirmed: same source]</c> The <c>300</c> divisor of <c>ships × d / this</c> and
/// <c>condition × d / this</c>, the heavier branch's ship and condition losses.
/// </param>
/// <param name="StormUnitLossDamageThreshold">
/// <c>[confirmed: same source; bug #292]</c> Above this <c>d</c> (70) a heavy storm also costs the
/// carried army whole units, exactly like the naval battle's own <c>d &gt; 70</c> branch — see
/// <see cref="StormUnitLossDivisor"/>. Every heavy storm this ruleset can reach clears this threshold
/// except the Winter spike (<c>d = 57</c>).
/// </param>
/// <param name="StormUnitLossDivisor">
/// <c>[confirmed: same source; bug #292]</c> The carried army loses
/// <c>unitCount × d / this + 1</c> whole units above <see cref="StormUnitLossDamageThreshold"/>, each
/// chosen by <c>Random(current count)</c> and removed swap-with-last, re-reading the count every
/// iteration — the same rule and the same <c>250</c> the naval battle's own whole-unit loss uses,
/// carried as this record's own copy rather than <c>Model.CombatRules.NavalCombatRules</c>'s for the
/// same Owns-list reason <see cref="StormShipLossRatioBase"/>'s remarks give. Every heavy storm applies
/// <see cref="Battle.BattleCasualties.Apply"/> at ratio <c>d</c> to the carried army first (60-86% of
/// each unit's troops at the reachable <c>d</c> values — <c>d</c> FALLS as <c>dmg</c> rises across the
/// odd <c>dmg</c> 7-17 band: 86, 82, 81, 77, 73, 72 — then
/// <see cref="Battle.BattleCasualties.DeleteBelowThreshold"/> (bug #289), then this whole-unit loss.
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
    int StormUnitLossDamageThreshold,
    int StormUnitLossDivisor,
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
/// <param name="WinnerCasualtyNumerator">
/// The <c>40</c> of <c>loserPower × 40 / winnerPower</c>. That expression is a <strong>ratio</strong>,
/// not a troop count: the instant resolver passes it to <c>FUN_0044AE20</c> as that function's own
/// <c>ratio</c> argument, and that function's body is the per-unit expression
/// <see cref="CasualtyDivisorBase"/> and <see cref="CasualtyDivisorRandomSpan"/> describe
/// (<c>decompiled-diplomacy-peace-terms-and-instant-battles.md</c> for the call site,
/// <c>decompiled-defection-and-siege-attrition.md</c> for the body).
/// </param>
/// <param name="CasualtyDivisorBase">
/// The <c>105</c> of <c>FUN_0044AE20</c>'s per-unit <c>troops -= troops / (Random(15) + 105) × ratio</c>
/// <strong>[confirmed: <c>decompiled-defection-and-siege-attrition.md</c>, transcribed in the T04 corpus
/// as <c>siege.attritionFormula</c>]</strong>. Added by T16 together with
/// <see cref="CasualtyDivisorRandomSpan"/>: the resolver's first round could only read
/// <c>loserPower × 40 / winnerPower</c> as a troop count because these two numbers had no home in the
/// ruleset and a C# literal is forbidden, and the count reading is wrong — quantified, a hard-fought win
/// (powers 5,000 against 5,200) would have cost the winner 38 troops in total.
/// </param>
/// <param name="CasualtyDivisorRandomSpan">
/// The <c>15</c> of the same expression: the width of the <c>Random(15)</c> band added to
/// <see cref="CasualtyDivisorBase"/>, so the divisor lands in <c>[105, 120)</c> and a unit loses between
/// <c>ratio / 120</c> and <c>ratio / 105</c> of its troops. Drawn through <see cref="Core.IRng"/>, once
/// per unit slot. <strong>[confirmed: same source]</strong>
/// </param>
/// <param name="DeletionDivisorNational">
/// <c>[confirmed: decompiled-defection-and-siege-attrition.md §"Where does population/fortification loss
/// actually come from, then?" (the FUN_0044ae20 bullet), confirmed at instruction level in
/// §"FUN_0044b27c, instruction by instruction" (CALL 0x0044AE20 at 0x0044B35D–B361), research 3f6ca09;
/// bug #289]</c> <c>FUN_0044AE20</c>'s second pass deletes a <strong>national</strong> unit (origin label
/// <c>0</c>) left with <c>troops &gt; 0</c> and below <c>standardBattalionSize / this</c> (10) — see
/// <see cref="BattleCasualties.DeleteBelowThreshold"/>. Runs after <see cref="BattleCasualties.Apply"/>
/// and before any promotion roll, at every call site: the field winner, the siege attacker, and a naval
/// or storm winner's carried army.
/// </param>
/// <param name="DeletionDivisorMercenary">
/// <c>[confirmed: decompiled-defection-and-siege-attrition.md §"Where does population/fortification loss
/// actually come from, then?" (the FUN_0044ae20 bullet), confirmed at instruction level in
/// §"FUN_0044b27c, instruction by instruction" (CALL 0x0044AE20 at 0x0044B35D–B361), research 3f6ca09;
/// bug #289]</c> The same deletion pass's threshold for a <strong>mercenary</strong> unit (origin label
/// <c>&gt; 0</c>): <c>standardBattalionSize / this</c> (5) — twice as forgiving as
/// <see cref="DeletionDivisorNational"/>'s national threshold.
/// </param>
public sealed record CombatRules(
    int PowerTroopDivisor,
    int PowerDivisor,
    int WinnerCasualtyNumerator,
    int CasualtyDivisorBase,
    int CasualtyDivisorRandomSpan,
    int DeletionDivisorNational,
    int DeletionDivisorMercenary,
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
/// question of whether they were the same adjustment described twice. <strong>T16's
/// <see cref="IC2.Engine.Battle.InstantBattleResolver.ResolveSiege"/> is the siege resolver and already
/// applies this field</strong>, with its own test — corrected here (T17) from this remark's earlier text
/// ("this field is T17's to apply"), which was accurate when T33 wrote it and stopped being accurate once
/// T16 became the siege resolver; T16's own implementer flagged the drift rather than editing this file,
/// since it is not in T16's Owns list. T17 does not read this field at all: it starts from T16's own
/// <see cref="IC2.Engine.Battle.BattleResult.Winner"/> decision (already ×9/10-adjusted) for whether a
/// siege attempt succeeds, so the reduction is applied exactly once across the two tasks.
/// </param>
/// <param name="AttritionRatioMultiplier">
/// <c>[confirmed: decompiled-defection-and-siege-attrition.md §"FUN_0044b27c, instruction by
/// instruction", research 3f6ca09; bug #290]</c> The <c>6</c> of the besieging attacker's own casualty
/// ratio, <c>clamp(defenderStrength × 6 / attackerStrength, </c><see cref="AttritionRatioFloor"/><c>,
/// </c><see cref="AttritionRatioCeiling"/><c>)</c> — <c>defenderStrength</c> and <c>attackerStrength</c>
/// taken raw (not "loser"/"winner"), unconditionally, on a successful attempt and a failed one alike, fed
/// to <see cref="BattleCasualties.Apply"/> exactly as the field and naval call sites feed their own
/// ratios. See <see cref="RulesetFlags.BugPolicySiegeRatioClamp"/> for how the clamp itself is applied.
/// </param>
/// <param name="AttritionRatioFloor">
/// <c>[confirmed: same source]</c> The <c>1</c> floor of the same clamp — a siege always costs the
/// attacker <em>something</em>, however lopsided the fight.
/// </param>
/// <param name="AttritionRatioCeiling">
/// <c>[confirmed: same source]</c> The <c>15</c> ceiling of the same clamp, well below the field and
/// naval paths' own maximum ratios — a siege is deliberately the gentlest of the three casualty call
/// sites on the attacker.
/// </param>
/// <param name="ErosionFloorNumerator">
/// <c>[confirmed: decompiled-defection-and-siege-attrition.md §"FUN_0044b27c, instruction by instruction"
/// (the FUN_0044B230(&amp;field) transcription, 0x0044B230–B26A), research 3f6ca09; bug
/// #293]</c> The <c>3</c> of the per-attempt erosion's floor term, <c>field × 3 / 4</c> — see
/// <see cref="ErosionFloorDenominator"/>. Runs on the city's loyalty, then fortification, then
/// population, in that order, on every siege attempt, win or lose, using the <em>same</em>
/// <c>defenderStrength</c>/<c>attackerStrength</c> pair <see cref="AttritionRatioMultiplier"/> reads
/// (after the siege entry point's own <c>× 9/10</c> reduction, before any erosion changes it).
/// </param>
/// <param name="ErosionFloorDenominator">
/// <c>[confirmed: same source]</c> The <c>4</c> of the same floor term.
/// </param>
/// <param name="ErosionCeilingNumerator">
/// <c>[confirmed: same source]</c> The <c>19</c> of the erosion's ceiling term,
/// <c>field × 19 / 20 + 1</c> — see <see cref="ErosionCeilingDenominator"/> and
/// <see cref="ErosionCeilingAddend"/>. A failed siege (<c>defenderStrength ≥ attackerStrength</c>) always
/// takes this branch, so a field above 20 loses about 5% per failed attempt and one at or below 20 is
/// unchanged (integer truncation floors the loss to zero there).
/// </param>
/// <param name="ErosionCeilingDenominator">
/// <c>[confirmed: same source]</c> The <c>20</c> of the same ceiling term.
/// </param>
/// <param name="ErosionCeilingAddend">
/// <c>[confirmed: same source]</c> The <c>+ 1</c> of the same ceiling term.
/// </param>
/// <param name="PopulationFloorDivisor">
/// <c>[confirmed: decompiled-defection-and-siege-attrition.md §"FUN_0044b27c, instruction by
/// instruction", research 3f6ca09; bug #293]</c> The <c>6</c> of the post-erosion population floor,
/// <c>population = max(population, maxPopulation / 6 + 1)</c> — see
/// <see cref="PopulationFloorAddend"/>. Applied once, after the three erosion passes, every attempt.
/// </param>
/// <param name="PopulationFloorAddend">
/// <c>[confirmed: same source]</c> The <c>+ 1</c> of the same floor.
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
    int AttritionRatioMultiplier,
    int AttritionRatioFloor,
    int AttritionRatioCeiling,
    int ErosionFloorNumerator,
    int ErosionFloorDenominator,
    int ErosionCeilingNumerator,
    int ErosionCeilingDenominator,
    int ErosionCeilingAddend,
    int PopulationFloorDivisor,
    int PopulationFloorAddend,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>
/// Loyalty floors, tiering, and (T86) the transfer formulas' own shared and per-mechanism terms —
/// <c>decompiled-quarterly-rebellion.md</c> §3 (research <c>235af11</c>), which corrected
/// <see cref="ForcedCaptureFloor"/>, <see cref="DefectionFloor"/> and <see cref="AllegiantRecaptureTarget"/>
/// from flat targets into the clamp bounds of a formula: a single-city forced capture writes
/// <c>max(ForcedCaptureFloor, min(ForcedCaptureCap, NonAllegiantTransferBase − L′))</c> when the city's
/// allegiance differs from the new owner, or <c>min(AllegiantRecaptureTarget, AllegiantRecaptureBase − L′)</c>
/// when it matches (<c>L′</c> = loyalty after any siege erosion already applied); a single-city defection
/// writes <c>min(DefectionFloor, max(DefectionFormulaFloor, NonAllegiantTransferBase − L))</c> or the same
/// allegiant clamp, against pre-transfer loyalty <c>L</c>. <see cref="IC2.Engine.Cities.Capture.CityCaptureResolver"/>
/// applies both; the mass conquest transfer (<c>decompiled-elimination-cleanup.md</c> §4 point 2, research
/// <c>43a44a1</c>) has its own, textually different clamp bounds — see <see cref="ConquestAllegiantCap"/>
/// and friends — applied by <see cref="IC2.Engine.Cities.Capture.ConquestCascade"/>.
/// </summary>
/// <param name="ForcedCaptureFloor">
/// <c>[confirmed]</c> The non-allegiant forced-capture clamp's lower bound (40) — was a flat target before
/// T86; the report's 5 save captures (Mediolanum, Felsina, Brixia, Byblos, Gordium) show the engine's old
/// flat 40 is wrong for all 5 once read as this formula's floor instead.
/// </param>
/// <param name="DefectionFloor">
/// <c>[confirmed]</c> The non-allegiant defection clamp's <em>upper</em> bound (65) — was a flat target
/// before T86; the report's 5 non-allegiant cascade defections (Aradus, Hemesa, Palmyra, Modena, Acroinon)
/// all actually resolved to 50 (the formula's floor, <see cref="DefectionFormulaFloor"/>), not this cap.
/// </param>
/// <param name="AllegiantRecaptureTarget">
/// <c>[confirmed]</c> The allegiant clamp's upper bound (90), shared by the forced-capture and defection
/// formulas — was a flat target before T86; the report's Synnada defection (62 → 78) and Tarquinii/Ariminum
/// defections (→ 90) show the formula, not a flat 90, both under this same cap.
/// </param>
/// <param name="TierDivisor">Unrelated to the transfer formulas: the loyalty-tier display divisor.</param>
/// <param name="AllegiantRecaptureBase">
/// <c>[confirmed]</c> The allegiant clamp's base (140): <c>min(AllegiantRecaptureTarget, 140 − L)</c>,
/// shared by the forced-capture and defection formulas.
/// </param>
/// <param name="NonAllegiantTransferBase">
/// <c>[confirmed]</c> The non-allegiant clamp's shared base (100): <c>100 − L</c> (defection) or
/// <c>100 − L′</c> (forced capture) — also the base the conquest transfer's own non-allegiant formula
/// uses (<see cref="ConquestNonAllegiantFloor"/>).
/// </param>
/// <param name="ForcedCaptureCap">
/// <c>[confirmed]</c> The non-allegiant forced-capture clamp's upper bound (60).
/// </param>
/// <param name="DefectionFormulaFloor">
/// <c>[confirmed]</c> The non-allegiant defection clamp's lower bound (50) — the value 6 of the report's 8
/// cascade defections actually landed on, that the engine's old flat 65 missed.
/// </param>
/// <param name="ConquestAllegiantCap">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4]</c> The mass conquest transfer's own allegiant
/// clamp upper bound (80): <c>min(80, 120 − L)</c> — deliberately different from the single-city
/// <see cref="AllegiantRecaptureTarget"/>/<see cref="AllegiantRecaptureBase"/> pair; <c>FUN_0044C528</c>'s
/// own loyalty write is textually distinct from <c>FUN_0044BB18</c>'s/<c>FUN_0044BED8</c>'s.
/// </param>
/// <param name="ConquestAllegiantBase">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4]</c> The conquest transfer's allegiant clamp base
/// (120) — see <see cref="ConquestAllegiantCap"/>.
/// </param>
/// <param name="ConquestNonAllegiantCap">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4]</c> The conquest transfer's non-allegiant clamp
/// upper bound (70): <c>min(70, max(40, 100 − L))</c>.
/// </param>
/// <param name="ConquestNonAllegiantFloor">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4]</c> The conquest transfer's non-allegiant clamp
/// lower bound (40) — see <see cref="ConquestNonAllegiantCap"/>; shares <see cref="NonAllegiantTransferBase"/>
/// (100) as its base.
/// </param>
public sealed record LoyaltyRules(
    int ForcedCaptureFloor,
    int DefectionFloor,
    int AllegiantRecaptureTarget,
    int TierDivisor,
    int AllegiantRecaptureBase,
    int NonAllegiantTransferBase,
    int ForcedCaptureCap,
    int DefectionFormulaFloor,
    int ConquestAllegiantCap,
    int ConquestAllegiantBase,
    int ConquestNonAllegiantCap,
    int ConquestNonAllegiantFloor,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>
/// City-capture economy, unity, the cascading-defection mechanic, and nation elimination —
/// <c>FUN_0044bb18</c> (capture), <c>FUN_0044ba1c</c> (the cascade) and <c>FUN_0044bed8</c> (defection),
/// per <c>docs/task-catalogue.md</c> T17 and
/// <see href="https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/nation-tax-base-and-city-economy-fields.md">
/// nation-tax-base-and-city-economy-fields.md</see>. The tax-base and wealth terms both mechanisms share
/// are <see cref="EconomyRules.TaxBaseContributionMultiplier"/> and
/// <see cref="EconomyRules.WealthPerPopulationThousand"/>, applied through
/// <see cref="IC2.Engine.Economy.CityOwnershipTaxTransfer"/> (T35) — not duplicated here.
/// </summary>
/// <param name="CaptureTreasuryCreditMultiplier">
/// <c>[confirmed]</c> The new owner's treasury credit at a forced capture: <c>treasury += contribution ×
/// this</c> (4) — <c>FUN_0044bb18</c>'s own write, distinct from
/// <see cref="EconomyRules.TaxBaseContributionMultiplier"/> (also 4, but the <em>tax-base</em> term,
/// <c>taxBase += contribution × 4</c>). Two different fields happen to share the value 4; see the
/// <c>capture.treasuryCreditMultiplier</c> corpus entry's own note for the full disambiguation — this is
/// exactly the confusion T37's review found DoD 9 had fallen into.
/// </param>
/// <param name="CaptureUnityGain">
/// <c>[confirmed]</c> The new owner's unity gain at a forced capture (9, clamped to
/// <see cref="EconomyRules.UnityCap"/>) — <c>FUN_0044bb18</c>.
/// </param>
/// <param name="CaptureUnityLoss">
/// <c>[confirmed]</c> The old owner's unity loss at a forced capture (15, not floored — the report gives
/// no floor for this term, unlike <see cref="DefectionUnityLossFloor"/>) — <c>FUN_0044bb18</c>.
/// </param>
/// <param name="DefectionTreasuryCreditMultiplier">
/// <c>[derived]</c> The new owner's treasury credit at a defection: <c>treasury += contribution × this</c>
/// (6) — <c>FUN_0044bed8</c>'s own write is <c>[confirmed]</c> in
/// <c>nation-tax-base-and-city-economy-fields.md</c>; using <c>FUN_0044bed8</c> <em>as</em> the defection
/// routine is the inference <c>docs/task-catalogue.md</c> T17's Known-open item names — corroborated by
/// <c>decompiled-defection-and-siege-attrition.md</c>'s independent match to the Modena defection (never
/// touches population or fortification, the same +3/−20 unity figures), but the top-level caller chain
/// has not been checked against a second real save beyond that one example.
/// </param>
/// <param name="DefectionUnityGain">
/// <c>[derived]</c> (see <see cref="DefectionTreasuryCreditMultiplier"/>) The new owner's unity gain at a
/// defection (3, clamped to <see cref="EconomyRules.UnityCap"/>) — bigger than the old owner's loss is
/// small relative to a forced capture's, but the losing side's own loss
/// (<see cref="DefectionUnityLoss"/>) is bigger than a forced capture's: defection costs the losing
/// nation more unity than losing the same city in a straight fight.
/// </param>
/// <param name="DefectionUnityLoss">
/// <c>[derived]</c> (see <see cref="DefectionTreasuryCreditMultiplier"/>) The old owner's unity loss at a
/// defection (20), floored at <see cref="DefectionUnityLossFloor"/> — unlike
/// <see cref="CaptureUnityLoss"/>, which the report gives no floor for.
/// </param>
/// <param name="DefectionUnityLossFloor">
/// <c>[derived]</c> (see <see cref="DefectionTreasuryCreditMultiplier"/>) The floor
/// <see cref="DefectionUnityLoss"/> does not push the old owner's unity below (250).
/// </param>
/// <param name="EliminationUnityReset">
/// <c>[confirmed: galatia-elimination-and-city-resupply-confirmed.md]</c> A nation's unity once its last
/// city is gone (0 — Galatia's own observed 668 → 0). A different, untraced writer
/// (<c>FUN_0044c360</c>) is mentioned in <c>nation-tax-base-and-city-economy-fields.md</c> resetting "the
/// collapsing nation" to unity 450, but its caller and trigger are not established and it is not the
/// mechanism DoD 4 asks for (a direct "losing the last city eliminates the nation" consequence); the
/// empirically observed Galatia elimination is the stronger, directly-applicable evidence and is what
/// this field reproduces.
/// </param>
/// <param name="CascadeDistanceMax">
/// <c>[confirmed: decompiled-defection-and-siege-attrition.md]</c> <c>FUN_0044ba1c</c>'s cascade only
/// considers another city within this Chebyshev distance (10) of the besieging army's position — the
/// same distance metric <see cref="IC2.Engine.Battle.ScatterPlacement"/> uses; the report gives
/// the threshold but not the metric, so the choice of Chebyshev over Manhattan/Euclidean is
/// <c>[derived]</c> by matching the engine's one other map-distance convention.
/// </param>
/// <param name="CascadeUnityThreshold">
/// <c>[confirmed: decompiled-defection-and-siege-attrition.md]</c> The cascade only fires while the
/// <strong>loser's</strong> own unity is below this (650), read live so a candidate later in the same
/// sweep sees every earlier defection's own <see cref="DefectionUnityLoss"/> already applied — a nation
/// already coming apart keeps coming apart. T91/#415 corrects an earlier reading of this field (and of
/// <c>RunCascade</c>'s own gate) as the <em>new owner's</em> unity: <c>FUN_0044ba1c</c> (:50133) reads
/// <c>candidate.owner</c>'s own unity, and every candidate in one sweep shares the same owner — the
/// nation that just lost a city, not the nation gaining one.
/// </param>
/// <param name="CascadeLoyaltyThreshold">
/// <c>[confirmed: decompiled-defection-and-siege-attrition.md]</c> The cascade only considers a candidate
/// city whose own loyalty is below this (65).
/// </param>
/// <param name="CascadeAllegiantDefenseDivisor">
/// <c>[confirmed: decompiled-defection-and-siege-attrition.md]</c> A candidate city's complete defender
/// strength is divided by this (3) when the city's allegiance already matches the new owner — "rebellious
/// sympathy weakens it further".
/// </param>
/// <param name="ConquestCityCountThreshold">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4, FUN_0044BB18 :50150]</c> T86: a non-capital
/// capture that leaves the loser with fewer than this many cities (6) conquers it outright —
/// <see cref="IC2.Engine.Cities.Capture.ConquestCascade"/> — instead of the engine's old "only at zero
/// cities" elimination rule.
/// </param>
/// <param name="CapitalMoveUnityThreshold">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4, FUN_0044BB18]</c> When the loser's capital falls,
/// it may attempt to move its capital only while its unity is over this (400); at or below it, it is
/// conquered outright without an attempt.
/// </param>
/// <param name="CapitalMoveCityCountThreshold">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4]</c> Only when the loser's own capital just fell:
/// the capital-move attempt also requires more than this many cities (6); at or below it, the loser is
/// conquered outright without an attempt. Review round 1, B3 item 5: an earlier revision of this remark
/// said "capital or not", which is false of the separate, non-capital branch — that one conquers only
/// below <see cref="ConquestCityCountThreshold"/>, strictly, so a non-capital capture leaving exactly 6
/// cities does <em>not</em> conquer (<c>NonCapitalCapture_LeavingSixCities_DoesNotConquer</c>), unlike a
/// capital capture leaving exactly 6, which does.
/// </param>
/// <param name="CapitalMoveUnityLoss">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4, FUN_0044BD2C :50234]</c> Attempting to move the
/// capital costs 50 unity, whether or not a destination city is actually found.
/// </param>
/// <param name="CapitalMoveMinDistanceTiles">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4]</c> The capital only actually moves to a city more
/// than this many tiles away (10, Chebyshev — the engine's one other map-distance convention, the same
/// <c>[derived]</c> choice <see cref="CascadeDistanceMax"/> already makes); with none, the capital does
/// not move and the loser is conquered instead. The addendum (research <c>dcd8fd7</c>,
/// <c>FUN_0044BD2C</c> :50265) confirms the test itself is <c>10 &lt; d</c>, i.e. a city must be at least
/// 11 tiles away, not exactly 10.
/// </param>
/// <param name="CapitalMoveStrengthDivisor">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4 addendum, research <c>dcd8fd7</c>,
/// FUN_0044BD2C :50267]</c> T86 review round 1, B1: every candidate destination's own full defender
/// strength (<see cref="IC2.Engine.Cities.Capture.CompleteDefenderStrength.Compute"/>) is divided by this
/// (10) — a signed integer division, truncating toward zero — before dividing again by the candidate's
/// own Chebyshev distance from the fallen capital, in that order; the winning candidate needs a
/// strictly-positive result. Numerically the same value as <see cref="CapitalMoveMinDistanceTiles"/>, but
/// a distinct field: one is a distance threshold in tiles, this one a strength divisor, and the original
/// never ties them together beyond the coincidence.
/// </param>
/// <param name="CapitalMoveNewCapitalLoyaltyGain">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4 addendum, research <c>dcd8fd7</c>,
/// FUN_0044BD2C :50286]</c> T86 review round 1, plan PR #410: on a successful capital move, the new
/// capital's own <see cref="CityState.Loyalty"/> gains this (8), capped at
/// <see cref="CapitalMoveNewCapitalStatCap"/>. Applied only to the destination — every other city is
/// untouched.
/// </param>
/// <param name="CapitalMoveNewCapitalFortificationGain">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4 addendum, research <c>dcd8fd7</c>,
/// FUN_0044BD2C :50289]</c> The new capital's own raw <see cref="CityState.FortificationCode"/> word
/// gains this (10), capped at <see cref="CapitalMoveNewCapitalStatCap"/> — applied to the stored word
/// itself, not the decoded percentage, which is also how the original derives its own quirk: a code
/// already above <see cref="CityOrderRule.MaxPercent"/> (an order in progress,
/// <see cref="FortificationCode.IsOrderInProgress"/>) plus this gain always exceeds the cap, so the min
/// collapses it to a plain finished value at the cap — discarding whatever order was pending, exactly
/// the addendum's own "the move discards it" remark.
/// </param>
/// <param name="CapitalMoveNewCapitalStatCap">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4 addendum, research <c>dcd8fd7</c>,
/// FUN_0044BD2C :50286–50291]</c> The upper bound (99) both
/// <see cref="CapitalMoveNewCapitalLoyaltyGain"/> and <see cref="CapitalMoveNewCapitalFortificationGain"/>
/// are capped at — the same literal <c>99</c> both of the addendum's own <c>min(99, …)</c> writes use.
/// Kept as one shared field rather than two coincidentally-equal ones (contrast
/// <see cref="CapitalMoveMinDistanceTiles"/>/<see cref="CapitalMoveStrengthDivisor"/>, kept separate on
/// purpose): both fields here are the same kind of quantity, a percentage capped one below its own
/// <c>100</c> ceiling, and the addendum gives no reason to think the original's two <c>min(99, …)</c>
/// writes are anything but the same cap applied twice.
/// </param>
/// <param name="CapitalMoveNewCapitalPopulationGain">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4 addendum, research <c>dcd8fd7</c>,
/// FUN_0044BD2C :50293]</c> The new capital's own <see cref="CityState.PopulationThousands"/> gains this
/// (10, in thousands), uncapped — even above <see cref="CityState.MaxPopulationThousands"/>.
/// </param>
/// <param name="CapitalMoveNewCapitalMaxPopulationGain">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4 addendum, research <c>dcd8fd7</c>,
/// FUN_0044BD2C :50294]</c> The new capital's own <see cref="CityState.MaxPopulationThousands"/> gains
/// this (20), uncapped.
/// </param>
/// <param name="CapitalMoveNewCapitalTributeGain">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4 addendum, research <c>dcd8fd7</c>,
/// FUN_0044BD2C :50295]</c> The new capital's own <see cref="CityState.Tribute"/> gains this (25),
/// uncapped.
/// </param>
/// <param name="ConquestWinnerUnityGain">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4, FUN_0044C528]</c> The winner's unity gain on
/// conquering a nation outright (50, clamped to <see cref="EconomyRules.UnityCap"/>) — distinct from, and
/// far larger than, a single-city <see cref="CaptureUnityGain"/>.
/// </param>
/// <param name="ConquestLoyaltyRandomBonusMax">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4]</c> The exclusive upper bound of the
/// <c>+ Random(this)</c> (6) added to every moved city's loyalty formula during a conquest's mass
/// transfer — drawn through <c>IRng</c>, one independent draw per moved city.
/// </param>
/// <param name="ConquestTreasuryCreditMultiplier">
/// <c>[confirmed: decompiled-elimination-cleanup.md §4, FUN_0044C528]</c> The winner's per-city treasury
/// credit during a conquest's mass transfer: <c>treasury += contribution × this</c> (6) — a distinct
/// write from <see cref="DefectionTreasuryCreditMultiplier"/> (also 6, a different function,
/// <c>FUN_0044BED8</c>) and <see cref="CaptureTreasuryCreditMultiplier"/> (4); kept as its own field for
/// the same reason those two are already kept separate despite <see cref="DefectionTreasuryCreditMultiplier"/>'s
/// coincidentally equal value — see that field's own remarks.
/// </param>
/// <param name="RebellionArmyDistanceMax">
/// <c>[confirmed: decompiled-quarterly-rebellion.md "Answer" and its §1 "The distance is Chebyshev"]</c>
/// T89, <c>FUN_0044C204</c> branch (c): an army of a nation at war with the city's owner qualifies only
/// within this Chebyshev distance of the rebelling city — the comparison is <c>&lt; 10</c>, so a distance
/// of exactly 10 does not qualify. Review round 1, N1: no siege is required and nothing about the army's
/// own state is read (troops, morale, embarkation) — any live army of a nation at war with the owner
/// qualifies, not only one actively besieging that city. Numerically the same value and metric as
/// <see cref="CascadeDistanceMax"/>, but a distinct field: that one gates the forced-capture cascade
/// (<c>FUN_0044ba1c</c>), this one gates a wholly different routine (<c>FUN_0044c204</c>) that happens to
/// share the threshold — the same reasoning already given for <see cref="CapitalMoveMinDistanceTiles"/>
/// being kept apart from <see cref="CascadeDistanceMax"/>.
/// </param>
/// <param name="RebellionNeighbourScoreDistanceWeight">
/// <c>[confirmed: decompiled-quarterly-rebellion.md "Answer"]</c> T89, branch (d)'s candidate score:
/// <c>cities(n) − this × cheb(city, capital(n))</c> (2).
/// </param>
/// <param name="RebellionNeighbourScoreFloor">
/// <c>[confirmed: decompiled-quarterly-rebellion.md "Answer"]</c> T89, branch (d): the running best score
/// starts here (−1000) before any candidate is scored, so only a strictly greater score ever replaces it
/// — the report's own note that a candidate scoring at or below this "cannot happen on a 320 × 140 map"
/// is about the shipped map's own bound, not a reason to change this starting value itself.
/// </param>
public sealed record CaptureRules(
    int CaptureTreasuryCreditMultiplier,
    int CaptureUnityGain,
    int CaptureUnityLoss,
    int DefectionTreasuryCreditMultiplier,
    int DefectionUnityGain,
    int DefectionUnityLoss,
    int DefectionUnityLossFloor,
    int EliminationUnityReset,
    int CascadeDistanceMax,
    int CascadeUnityThreshold,
    int CascadeLoyaltyThreshold,
    int CascadeAllegiantDefenseDivisor,
    int ConquestCityCountThreshold,
    int CapitalMoveUnityThreshold,
    int CapitalMoveCityCountThreshold,
    int CapitalMoveUnityLoss,
    int CapitalMoveMinDistanceTiles,
    int CapitalMoveStrengthDivisor,
    int CapitalMoveNewCapitalLoyaltyGain,
    int CapitalMoveNewCapitalFortificationGain,
    int CapitalMoveNewCapitalStatCap,
    int CapitalMoveNewCapitalPopulationGain,
    int CapitalMoveNewCapitalMaxPopulationGain,
    int CapitalMoveNewCapitalTributeGain,
    int ConquestWinnerUnityGain,
    int ConquestLoyaltyRandomBonusMax,
    int ConquestTreasuryCreditMultiplier,
    int RebellionArmyDistanceMax,
    int RebellionNeighbourScoreDistanceWeight,
    int RebellionNeighbourScoreFloor,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>The numeric codes the relation matrix stores for each diplomatic state.</summary>
public sealed record RelationStateCodes(int Peace, int Trade, int Alliance, int War);

/// <summary>The confirmed diplomatic state machine, its cooldowns, and the reparation formula's constants.</summary>
/// <param name="OfferRollDenominator">
/// <c>[confirmed: decompiled-ai-offers-to-human-seats.md §1b]</c> <c>FUN_00452034</c>'s own chance that a
/// human turn-start candidate becomes a pending offer at all — <c>Random(3) == 0</c>, drawn only once the
/// candidate nation is alive and at peace with the human (T82, <see cref="IC2.Engine.Diplomacy.PendingOfferSystem"/>).
/// </param>
/// <param name="AiOwnDiplomacy">
/// <c>[confirmed: decompiled-ai-offers-to-human-seats.md §1a]</c> <c>FUN_0044FB7C</c>'s own gates for an
/// AI seat's direct, no-consent treaty writes (T82, bug #357).
/// </param>
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
    int OfferRollDenominator,
    AiOwnDiplomacyRules AiOwnDiplomacy,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>
/// The confirmed gates and constants of <c>FUN_0044FB7C</c>, the AI's own per-turn diplomacy — war
/// targeting, the alliance roll and its partner search — <c>decompiled-ai-offers-to-human-seats.md</c>
/// §1a <strong>[confirmed]</strong>. Trade's own cap is <see cref="DiplomacyRules.MaxTradePartners"/>,
/// already shared with the human-initiated path, so it is not repeated here.
/// </summary>
/// <param name="BusyMobilizationThreshold">
/// An AI already busy (at war, or <see cref="Model.NationState.MobilizedPercent"/> over this (40), or
/// the season is winter — <c>season == 3</c>, the same "last season of the year"
/// reading <see cref="IC2.Engine.Naval.FleetTickSystem"/> already uses) declares no war and makes no alliance this
/// turn. The season term is <c>[derived]</c>: "Spring = 0, Summer = 1" are the report's own observed
/// values; the "3 = Winter" reading follows from <see cref="CalendarRules.SeasonsPerYear"/> (4) rather
/// than from a fourth season directly observed.
/// </param>
/// <param name="WarTargetRatioBase">
/// The power-ratio threshold a war-target candidate's own ratio must exceed (10) — the loop's own
/// "best" starts here, so the first candidate needs a ratio over 10 to be taken at all, and every later
/// one needs to beat the best seen so far.
/// </param>
/// <param name="WarTargetRatioMultiplier">
/// The multiplier in the war-target ratio <c>this · P(me) / max(1, P(k))</c> (8).
/// </param>
/// <param name="WarDeclareRollDenominator">
/// Given a war target, the chance the AI actually declares this turn — <c>Random(10) == 0</c>.
/// </param>
/// <param name="AllianceRollDenominator">
/// Given the AI is not busy, the chance it even looks for an alliance partner this turn —
/// <c>Random(20) == 0</c>.
/// </param>
/// <param name="AllianceMaxPartnerWars">
/// The alliance partner search only considers an AI <c>m</c> with fewer than this many wars (2) —
/// <c>wars(m) &lt; 2</c>.
/// </param>
/// <param name="PowerWealthDivisor">
/// The war-target power formula's wealth term divisor: <c>P(n) = (wealth / this) × (unity / <see cref="PowerUnityDivisor"/>)</c> (20,000).
/// </param>
/// <param name="PowerUnityDivisor">
/// The war-target power formula's unity term divisor (100) — see <see cref="PowerWealthDivisor"/>.
/// </param>
public sealed record AiOwnDiplomacyRules(
    int BusyMobilizationThreshold,
    int WarTargetRatioBase,
    int WarTargetRatioMultiplier,
    int WarDeclareRollDenominator,
    int AllianceRollDenominator,
    int AllianceMaxPartnerWars,
    int PowerWealthDivisor,
    int PowerUnityDivisor,
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
/// <param name="FaithfulThawColumnBug">
/// Whether the quarterly diplomatic thaw reproduces the confirmed <c>Q8</c> bug — the thaw loop only ever
/// touching the first 8 of each nation's 16 relation columns
/// (<c>decompiled-diplomacy-peace-terms-and-instant-battles.md</c>: <c>while (sVar6 != 8)</c>) — or thaws
/// every column. <c>true</c> reproduces the bug (a pair both indexed &#8805; 8 never decays); <c>false</c>
/// fixes it silently. <c>docs/task-catalogue.md</c> T19 DoD 9 names this flag by this exact name
/// (<c>faithfulThawColumnBug</c>); <c>classical-faithful</c> sets it <c>true</c>, matching the confirmed
/// original.
/// </param>
/// <param name="BugPolicySiegeRatioClamp">
/// <c>docs/tasks/T63.md</c> Decision 1, extended 2026-09-23 by the user (D-A): whether TWO of the
/// siege's own intermediate values reproduce the original's <strong>16-bit, SIGNED</strong> comparison
/// — the attrition ratio's own <c>clamp(·, 1, 15)</c> input (<c>defenderStrength × 6 /
/// attackerStrength</c>) and the erosion's own <c>field × def / atk</c> ratio term, both of which a
/// near-empty <c>attackerStrength</c> can drive well past 32,767. <c>FUN_00448FD0</c>/<c>FUN_00448FD8</c>
/// (the original's <c>min</c>/<c>max</c> helpers both the ratio and the erosion term pass through) compare
/// with <c>JG</c>/<c>JL</c> — <strong>signed</strong> 16-bit jumps, never <c>JA</c>/<c>JB</c> (unsigned) —
/// so a raw value wraps into the FULL signed 16-bit range, <c>[-32768, 32767]</c>, not merely into a small
/// unsigned remainder: the report's own example, <c>q = 501,996</c>, has low word <c>-22,292</c>, which
/// clamps to the floor (1), not to some small positive number
/// <strong>[confirmed at instruction level: decompiled-defection-and-siege-attrition.md
/// §"FUN_0044b27c, instruction by instruction", research 3f6ca09 — <c>uVar3</c> is a 16-bit local read
/// through the signed comparisons above; the erosion's own wrap is the same report, the `FUN_0044b230`
/// bullet]</c>. <c>classical-faithful</c> reproduces both wraps
/// (<see cref="SiegeRatioClampPolicy.Reproduce16BitClamp"/>); <c>improved</c> computes both in ordinary
/// 32-bit arithmetic (<see cref="SiegeRatioClampPolicy.Clamp32Bit"/>), never wrapping.
/// </param>
public sealed record RulesetFlags(
    DiplomacyModel DiplomacyModel,
    EconomyPurseModel EconomyPurses,
    SeatAsymmetryModel SeatAsymmetry,
    DefeatOutcome CombatOnDefeat,
    bool FaithfulThawColumnBug,
    SiegeRatioClampPolicy BugPolicySiegeRatioClamp,
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

/// <summary>
/// Whether the siege attrition ratio's clamp reproduces the original's 16-bit wrap (audit-adjacent,
/// <c>docs/tasks/T63.md</c> Decision 1).
/// </summary>
public enum SiegeRatioClampPolicy
{
    /// <summary>
    /// Read the siege attrition ratio and the erosion's own ratio term as the original's own
    /// <strong>signed</strong> 16-bit comparison does (<c>JG</c>/<c>JL</c>, not <c>JA</c>/<c>JB</c>) —
    /// wrapping into <c>[-32768, 32767]</c> before <c>min</c>/<c>max</c>, not merely modulo 65,536 — so a
    /// near-empty besieger's ratio can land anywhere the wrap takes it, including negative, rather than
    /// saturating at 15.
    /// </summary>
    Reproduce16BitClamp,

    /// <summary>Compute both values in ordinary 32-bit arithmetic, never wrapping.</summary>
    Clamp32Bit,
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
