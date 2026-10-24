using IC2.Data;
using IC2.Engine.Model;

namespace IC2.Engine.Import;

/// <summary>
/// T185's explicit native-to-original field inventory. A property added to either side fails the
/// reflection test until it receives a written, derived, validated, template, canonical, omitted or
/// rejected policy. Byte offsets and widths remain the responsibility of OriginalSaveCodec/Layout.
/// </summary>
public static class OriginalSaveExportFieldMapping
{
    public enum Kind { Written, Derived, Validated, Template, Canonical, Omitted, Rejected }

    public sealed record Rule(Type Type, string Property, Kind Policy, string Counterpart, string Reason)
    {
        public string Name => $"{Type.Name}.{Property}";
    }

    private sealed record Group(Type Type, string Properties, Kind Policy, string Counterpart, string Reason);

    public static IReadOnlyList<Type> NativeTypes { get; } =
    [
        typeof(SaveGame), typeof(GameState), typeof(CalendarState), typeof(NationState),
        typeof(CityState), typeof(ArmyState), typeof(UnitSlot), typeof(FleetState),
        typeof(RecruitmentSlot), typeof(MercenaryPoolSlot), typeof(PendingDiplomaticOffer),
        typeof(DiplomaticRelations), typeof(NationNeighbours), typeof(RoughSeaCell),
        typeof(NewsLog), typeof(NewsEntry), typeof(ArmyTravelTarget), typeof(NationRallyPoint),
    ];

    public static IReadOnlyList<Type> OriginalTypes { get; } =
    [
        typeof(OriginalSaveDocument), typeof(OriginalCity), typeof(OriginalArmy),
        typeof(OriginalUnitSlot), typeof(OriginalFleet), typeof(OriginalRecruitEntry),
        typeof(OriginalNation), typeof(OriginalNationUi), typeof(OriginalInertBytes),
        typeof(OriginalMercenary), typeof(OriginalTrailer), typeof(OriginalSaveUiProfile),
    ];

    private static IReadOnlyList<Rule> Expand(params Group[] groups) => groups
        .SelectMany(g => g.Properties.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(property => new Rule(g.Type, property, g.Policy, g.Counterpart, g.Reason)))
        .ToArray();

    /// <summary>Every native property the export receives, including explicit omissions.</summary>
    public static IReadOnlyList<Rule> Native { get; } = Expand(
        new(typeof(SaveGame), "SchemaVersion Id Label Provenance FirstEndNewsOwed", Kind.Omitted,
            "none", "Native envelope and first-end bookkeeping have no original SAV field."),
        new(typeof(SaveGame), "ScenarioId WorldId RulesetId", Kind.Validated,
            "original classical context", "The exporter requires the shipped classical world, scenario and faithful ruleset."),
        new(typeof(SaveGame), "State", Kind.Written, "OriginalSaveDocument", "The live state supplies the strategic document."),
        new(typeof(SaveGame), "Battle", Kind.Rejected, "battle block", "An open tactical battle refuses strategic SAV export."),
        new(typeof(SaveGame), "ArmyTravelTargets", Kind.Omitted, "none", "Campaign travel plans are reported but cannot be encoded by the original."),
        new(typeof(SaveGame), "RetreatSupplyPlans", Kind.Omitted, "none", "Per-unit recovery plans are reported but cannot be encoded by the original."),
        new(typeof(SaveGame), "NationRallyPoints", Kind.Omitted, "none", "Campaign rally targets are reported but cannot be encoded by the original."),

        new(typeof(GameState), "SchemaVersion WorldId RulesetId ScenarioId", Kind.Validated,
            "original classical context", "Schema and context must match the supported original campaign."),
        new(typeof(GameState), "Calendar TurnOrder ActiveSeatIndex Nations Cities Armies Fleets MercenaryPool Relations NewsLog PendingOffer Neighbours RoughSeaCells",
            Kind.Written, "original tables and trailer", "These collections or values supply checked original strategic fields."),
        new(typeof(GameState), "RandomSeed", Kind.Omitted, "none", "The original save does not restore the process RNG state."),
        new(typeof(GameState), "ActiveNationId", Kind.Derived, "OriginalTrailer.CurrentNation", "Derived from the saved turn order and active seat index."),

        new(typeof(CalendarState), "Week SeasonIndex YearBc", Kind.Written, "OriginalTrailer calendar", "The three original calendar words are written."),
        new(typeof(CalendarState), "TurnIndex", Kind.Omitted, "none", "The original has no elapsed-turn counter."),

        new(typeof(NationState), "Id", Kind.Validated, "OriginalNation table index", "Canonical world order supplies original nation codes."),
        new(typeof(NationState), "Name LeaderName CapitalCityId Control Treasury Unity Wealth TaxBase TaxRatePercent MobilizedPercent RecruitmentSlots ConqueredBy",
            Kind.Written, "OriginalNation", "Live nation fields are checked and written to the nation record."),
        new(typeof(NationState), "ColorHex Personality Population PopulationAtStart TreasuryAtStart CityCountAtStart",
            Kind.Omitted, "none or world start", "Native presentation, AI and score caches are omitted; original start fields come from the world."),
        new(typeof(NationState), "Eliminated", Kind.Derived, "OriginalNation.Cities", "The original's live city count is rebuilt from city ownership."),

        new(typeof(CityState), "Id", Kind.Validated, "OriginalCity table index", "Canonical world order supplies original city codes."),
        new(typeof(CityState), "Name X Y Owner Allegiance Loyalty SupplyTons FortificationCode PopulationThousands MaxPopulationThousands Tribute",
            Kind.Written, "OriginalCity", "Every stored city field is checked and written."),
        new(typeof(CityState), "Garrison", Kind.Omitted, "none", "The original city record has no independent garrison unit list."),

        new(typeof(ArmyState), "Id", Kind.Validated, "OriginalArmy table index", "Army order and fleet references use the exported table index."),
        new(typeof(ArmyState), "Nation X Y Moves Morale Money SupplyTons Units AboardFleetId",
            Kind.Written, "OriginalArmy and OriginalFleet.Army", "Live army fields and transport links are checked and written."),
        new(typeof(ArmyState), "CoveredTileCode", Kind.Derived, "OriginalArmy.Cell", "The map underlay is reconstructed rather than trusting native marker bookkeeping."),
        new(typeof(ArmyState), "TotalTroops IsEmbarked", Kind.Derived, "army marker and transport", "These are formulas over units and the carried-fleet link."),
        new(typeof(UnitSlot), "MercenaryLabel UnitTypeId Troops Quality Name", Kind.Written,
            "OriginalUnitSlot", "The exporter checks original bounds, type codes and name encoding; only the reviewed mercenary alias may change text."),
        new(typeof(UnitSlot), "IsMercenary IsRegular", Kind.Derived, "OriginalUnitSlot.Label", "Both flags derive from the stored mercenary label."),

        new(typeof(FleetState), "Id", Kind.Validated, "OriginalFleet table index", "Fleet order and army carrier links use the exported table index."),
        new(typeof(FleetState), "Nation X Y Moves Ships ConditionPercent Money SupplyTons ConstructionTicksRemaining BuildCityId CarriedArmyId",
            Kind.Written, "OriginalFleet", "Construction and launched fleet fields use their reviewed union/sentinel policy."),
        new(typeof(FleetState), "CoveredTileCode", Kind.Derived, "OriginalFleet.Cell", "The map underlay is reconstructed."),
        new(typeof(FleetState), "IsUnderConstruction IsCarryingArmy", Kind.Derived, "OriginalFleet", "These flags derive from construction and cargo fields."),

        new(typeof(RecruitmentSlot), "TargetCityId UnitTypeId Troops StateCode", Kind.Written,
            "OriginalRecruitEntry", "Occupied slots are packed and checked against original capacity."),
        new(typeof(MercenaryPoolSlot), "SlotIndex X Y NameLabel UnitTypeId Troops Quality", Kind.Written,
            "OriginalMercenary", "Each occupied offer is written at its original fixed slot index."),
        new(typeof(PendingDiplomaticOffer), "ProposingNationId ProposedRelationCode", Kind.Written,
            "OriginalTrailer.OfferFrom/OfferType", "A pending strategic offer is representable and must be retained."),
        new(typeof(DiplomaticRelations), "NationIds", Kind.Validated, "OriginalNation.Relations", "Nation ids are resolved in canonical original code order."),
        new(typeof(DiplomaticRelations), "Matrix", Kind.Written, "OriginalNation.Relations", "Directional relation values are written row by row."),
        new(typeof(NationNeighbours), "NationId NeighbourIds", Kind.Written,
            "OriginalNation.Neighbours", "Neighbour ids are packed into original nation bit masks."),
        new(typeof(RoughSeaCell), "X Y", Kind.Written, "OriginalSaveDocument.Map", "Weekly rough-sea cells are overlaid before markers."),
        new(typeof(NewsLog), "MostRecentSlot", Kind.Derived, "news index", "The original index follows the number of stored lines."),
        new(typeof(NewsLog), "Slots", Kind.Written, "OriginalSaveDocument.News", "News text is written in slot order."),
        new(typeof(NewsEntry), "Text", Kind.Written, "OriginalSaveDocument.News", "Original ASCII and slot-length rules are checked."),
        new(typeof(ArmyTravelTarget), "ArmyId NationId X Y CityId FromRally", Kind.Omitted,
            "none", "The entire optional campaign plan is reported as omitted from original-compatible export."),
        new(typeof(NationRallyPoint), "NationId X Y CityId", Kind.Omitted,
            "none", "The campaign rally setting is reported as omitted from original-compatible export."));

    /// <summary>Every typed original field, including template and canonical-only regions.</summary>
    public static IReadOnlyList<Rule> Original { get; } = Expand(
        new(typeof(OriginalSaveDocument), "Map Cities Armies Fleets Nations Mercenaries News Trailer", Kind.Written,
            "GameState and World", "The complete document is assembled from checked live state, world data and UI profile."),
        new(typeof(OriginalCity), "Name X Y Owner Allegiance Loyalty Supplies Fort Population MaxPopulation Tribute",
            Kind.Written, "CityState", "City table values are mapped from the matching native city."),
        new(typeof(OriginalArmy), "X Y Owner Moves Supplies Money Morale Units", Kind.Written,
            "ArmyState", "Army records are written in state order with packed live units."),
        new(typeof(OriginalArmy), "Cell", Kind.Derived, "world map underlay", "Covered terrain or the embarked sentinel is recomputed."),
        new(typeof(OriginalUnitSlot), "Label Type Troops Quality Name", Kind.Written,
            "UnitSlot", "Unit values and the reviewed generated-mercenary alias are validated before encoding."),
        new(typeof(OriginalFleet), "X Y Owner Countdown Moves Supplies Money Ships CityOrCondition Army", Kind.Written,
            "FleetState", "The construction/launch union and carrier index come from checked fleet state."),
        new(typeof(OriginalFleet), "RouteX RouteY", Kind.Canonical, "no native field", "No AI waypoint is saved; write the original's absent-route sentinel."),
        new(typeof(OriginalFleet), "Cell", Kind.Derived, "world map underlay", "Covered terrain or rough sea is recomputed."),
        new(typeof(OriginalRecruitEntry), "State Type Troops City", Kind.Written,
            "RecruitmentSlot", "Occupied recruitment is packed; empty entries get canonical zeros."),
        new(typeof(OriginalNation), "Name Leader Relations Neighbours Recruits Wealth Treasury Unity Mobilization Capital Tax TaxBase Conqueror Controller",
            Kind.Written, "NationState/Relations/Neighbours", "Checked live nation state supplies these fields."),
        new(typeof(OriginalNation), "CityList Cities", Kind.Derived, "CityState.Owner", "Current owned cities are counted and sorted in original byte-name order."),
        new(typeof(OriginalNation), "Wealth0 Treasury0 Cities0 Colours", Kind.Derived, "World starting data", "Historical baselines and battle colours come from the shipped world."),
        new(typeof(OriginalNation), "Ui", Kind.Template, "OriginalSaveUiProfile.Nations", "Only interface settings are copied from the selected original template."),
        new(typeof(OriginalNation), "Inert", Kind.Canonical, "no native field", "The gate-2 canonical encoder writes zeros for the reviewed inert bytes."),
        new(typeof(OriginalNationUi), "Hints AreaMapToggle AreaWindow UnitWindow InfoWindow ViewY ViewX MapFilter MeleeDelay ShootDelay",
            Kind.Template, "original template nation UI", "These presentation fields are copied without game-state inference."),
        new(typeof(OriginalInertBytes), "Block27 Byte46D Block491", Kind.Canonical,
            "no native field", "The 31 gate-2 inert bytes use the canonical zero policy."),
        new(typeof(OriginalMercenary), "X Y Label Type Troops Quality", Kind.Written,
            "MercenaryPoolSlot", "Occupied slots are mapped; empty slots use canonical sentinels."),
        new(typeof(OriginalTrailer), "TurnOrder OfferFrom OfferType CurrentNation SeatIndex Week YearBc Season", Kind.Written,
            "GameState", "Strategic turn/offer/calendar state is checked and written."),
        new(typeof(OriginalTrailer), "MainLeft MainTop MainClientHeight MainClientWidth", Kind.Template,
            "OriginalSaveUiProfile", "The main window's presentation geometry comes from the selected template."),
        new(typeof(OriginalSaveUiProfile), "Nations MainLeft MainTop MainClientHeight MainClientWidth", Kind.Template,
            "original template", "The UI profile is read from one explicitly selected original save."));
}
