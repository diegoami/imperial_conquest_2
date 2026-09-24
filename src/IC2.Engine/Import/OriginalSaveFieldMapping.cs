using System.Linq;
using IC2.Engine.Model;
using IC2.Data;

namespace IC2.Engine.Import;

/// <summary>
/// A declared, checkable answer to Done-when 2's "an import report lists zero unmapped fields for
/// every table <c>IC2.Data</c> already parses": every public instance property of every
/// <c>IC2.Data</c> record type <see cref="OriginalSaveImporter"/> reads is listed here exactly once,
/// classified as <see cref="FieldMappingKind.Mapped"/> (its value, or a value it drives, reaches the
/// imported <see cref="GameState"/>), <see cref="FieldMappingKind.Derived"/> (the corresponding
/// output is real, but computed from a different, stated source rather than a straight copy of this
/// property), or <see cref="FieldMappingKind.DeclaredUnmapped"/> (nothing in the domain model can hold
/// it, with the reason stated).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why a declared list instead of inferring it from the importer's own code.</strong> A review
/// finding (PR #319, round 1, B1) caught <see cref="OriginalSaveImportReport.UnmappedFields"/> hard-coded
/// to <see cref="ValueList{T}.Empty"/> — a claim nobody re-checks, which stayed true even after
/// <c>NationRecord.CityCount</c> was silently never read. <c>OriginalSaveFieldMappingTests</c>
/// (in <c>tests/IC2.Engine.Tests/Import/</c>) reflects over every type in <see cref="Types"/> and
/// requires every public instance property to have exactly one entry here — so a field
/// <c>IC2.Data</c> starts or stops exposing shows up as a failing test, not a stale comment.
/// </para>
/// <para>
/// <strong>The two kinds that are not a straight copy, and why neither is "unmapped".</strong> A
/// property earns <see cref="FieldMappingKind.Derived"/> when the imported state carries the
/// information some other way: <c>CityRecord.Index</c>/<c>ArmyUnit.Slot</c>/<c>NationRecord.Code</c>
/// and friends are redundant with the record's own position in the array <c>IC2.Data</c> already
/// returns in that exact order (checked at parse time), so the importer uses the loop index directly;
/// <c>ArmyRecord.IsFrozen</c>/<c>FleetRecord.IsLaunched</c>-style booleans and the panel-formula
/// properties (<c>TotalTroops</c>, <c>SupplyPercent</c>, <c>TransportCapacityTroops</c>, …) are
/// recomputable from fields that are themselves mapped, so carrying them through separately would only
/// duplicate data already in the imported state. <c>NationRecord.CityCount</c> and
/// <c>NationRecord.IsEliminated</c> are the one deliberate override pair: the SAV's own bookkeeping can
/// be stale for a nation already affected mid-game (Galatia reads a stored count of 5 while owning 0
/// cities in six corpus saves — <c>1_rome_270_winter_7/9/11</c>, <c>_7_b</c>, <c>_9_b</c> and
/// <c>1_thracia_271_autumn_1</c>), so <see cref="OriginalSaveImporter.Import"/> derives
/// <c>NationState.CityCountAtStart</c>/<c>Eliminated</c> by recounting the cities it just imported by
/// owner instead — the mirror image of Done-when 5's tax-base/wealth rule, which goes the other way
/// (never recompute, always read the stored word directly) because that pair's mid-quarter drift from a
/// city-based rebuild is legitimate, not staleness. This is exactly the override review finding B1 asked
/// to be declared, not silently accepted.
/// </para>
/// <para>
/// <strong>The one real gap, and its scope.</strong> <see cref="FieldMappingKind.DeclaredUnmapped"/> is
/// reserved for <c>MercenaryRecord.X</c>/<c>Y</c> — the offer's city-tile position — because
/// <see cref="MercenaryPoolSlot"/> has no position field to hold it. This is the user's narrow
/// waiver of Done-when 2's "zero unmapped fields" for exactly these two fields (docs/tasks/T21.md,
/// "Mercenary position", decision on
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/321">#321</see>); the missing
/// hiring-position gate itself is bug
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/325">#325</see>, not this task's.
/// <c>OriginalSaveFieldMappingTests</c> pins the <see cref="FieldMappingKind.DeclaredUnmapped"/>
/// set to exactly these two entries, so a third one appearing would fail the test rather than widen the
/// waiver silently.
/// </para>
/// </remarks>
public static class OriginalSaveFieldMapping
{
    /// <summary>How one <c>IC2.Data</c> property relates to the imported <see cref="GameState"/>.</summary>
    public enum FieldMappingKind
    {
        /// <summary>This property's own value, or a value it directly gates, reaches the imported state.</summary>
        Mapped,

        /// <summary>
        /// The corresponding output is real, but computed from a different, stated source — the property
        /// itself is redundant (array position, a formula over other mapped fields) or deliberately
        /// overridden (see this type's own remarks).
        /// </summary>
        Derived,

        /// <summary>Nothing in the domain model can hold this value; <see cref="FieldMapping.Reason"/> says why.</summary>
        DeclaredUnmapped,
    }

    /// <summary>One property's classification.</summary>
    /// <param name="Type">The <c>IC2.Data</c> record type that declares the property.</param>
    /// <param name="Property">The property's own name.</param>
    /// <param name="Kind">How it relates to the imported state.</param>
    /// <param name="Reason">Why — always populated, even for a straight <see cref="FieldMappingKind.Mapped"/> copy.</param>
    public sealed record FieldMapping(Type Type, string Property, FieldMappingKind Kind, string Reason)
    {
        /// <summary>The <c>"TypeName.PropertyName"</c> form <see cref="OriginalSaveImportReport.UnmappedFields"/> uses.</summary>
        public string QualifiedName => $"{Type.Name}.{Property}";
    }

    /// <summary>
    /// Every <c>IC2.Data</c> record type <see cref="OriginalSaveImporter.Import"/> reads — the exact set
    /// <c>OriginalSaveFieldMappingTests</c> reflects over. <c>SaveNationTable</c>,
    /// <c>SaveArmyTable</c>, <c>SaveFleetTable</c>, <c>SaveRecruitmentTable</c>,
    /// <c>SaveMercenaryTable</c>, <c>SaveTurnState</c>, <c>SavePendingOffer</c> and <c>SaveNewsLog</c>
    /// themselves (the table-level wrappers, as opposed to the one-record types below) carry no field
    /// worth mapping beyond the list/record they expose, which is what this set actually enumerates.
    /// </summary>
    public static IReadOnlyList<Type> Types { get; } = new[]
    {
        typeof(CityRecord),
        typeof(NationRecord),
        typeof(ArmyRecord),
        typeof(ArmyUnit),
        typeof(FleetRecord),
        typeof(RecruitmentEntry),
        typeof(MercenaryRecord),
        typeof(SaveTurnState),
        typeof(SavePendingOffer),
        typeof(SaveNewsLog),
    };

    private const string Redundant = "Redundant with the record's own position in the list IC2.Data " +
        "already returns in that exact order; the importer uses the loop index/order directly rather " +
        "than re-reading the record's own copy.";

    private const string Formula = "A display-only formula over other properties of this same record, " +
        "all of which are themselves mapped; not a distinct save-file field.";

    /// <summary>Every classified property, one entry per <see cref="FieldMapping.QualifiedName"/>.</summary>
    public static IReadOnlyList<FieldMapping> All { get; } = new[]
    {
        // ---- CityRecord -> CityState (positional; see docs/game-design.md "Original-save compatibility")
        new FieldMapping(typeof(CityRecord), nameof(CityRecord.Index), FieldMappingKind.Derived, Redundant),
        new FieldMapping(typeof(CityRecord), nameof(CityRecord.Name), FieldMappingKind.Mapped, "-> CityState.Name."),
        new FieldMapping(typeof(CityRecord), nameof(CityRecord.X), FieldMappingKind.Mapped, "-> CityState.X."),
        new FieldMapping(typeof(CityRecord), nameof(CityRecord.Y), FieldMappingKind.Mapped, "-> CityState.Y."),
        new FieldMapping(typeof(CityRecord), nameof(CityRecord.Supplies), FieldMappingKind.Mapped, "-> CityState.SupplyTons."),
        new FieldMapping(typeof(CityRecord), nameof(CityRecord.OwnerCode), FieldMappingKind.Mapped, "-> CityState.Owner, via the world's positional nation ids."),
        new FieldMapping(typeof(CityRecord), nameof(CityRecord.AllegianceCode), FieldMappingKind.Mapped, "-> CityState.Allegiance, via the world's positional nation ids."),
        new FieldMapping(typeof(CityRecord), nameof(CityRecord.LoyaltyValue), FieldMappingKind.Mapped, "-> CityState.Loyalty."),
        new FieldMapping(typeof(CityRecord), nameof(CityRecord.FortificationPercent), FieldMappingKind.Mapped, "-> CityState.FortificationCode."),
        new FieldMapping(typeof(CityRecord), nameof(CityRecord.PopulationThousands), FieldMappingKind.Mapped, "-> CityState.PopulationThousands."),
        new FieldMapping(typeof(CityRecord), nameof(CityRecord.ReferencePopulationThousands), FieldMappingKind.Mapped, "-> CityState.MaxPopulationThousands."),
        new FieldMapping(typeof(CityRecord), nameof(CityRecord.TributeTalents), FieldMappingKind.Mapped, "-> CityState.Tribute."),

        // ---- NationRecord -> NationState (Done-when 5, 11)
        new FieldMapping(typeof(NationRecord), nameof(NationRecord.Code), FieldMappingKind.Derived, Redundant),
        new FieldMapping(typeof(NationRecord), nameof(NationRecord.Name), FieldMappingKind.Mapped, "-> NationState.Name."),
        new FieldMapping(typeof(NationRecord), nameof(NationRecord.Leader), FieldMappingKind.Mapped, "-> NationState.LeaderName (falls back to the world's own definition when null)."),
        new FieldMapping(typeof(NationRecord), nameof(NationRecord.Relations), FieldMappingKind.Mapped, "-> GameState.Relations' matrix row, cell for cell (Done-when 11; review B2)."),
        new FieldMapping(typeof(NationRecord), nameof(NationRecord.Treasury), FieldMappingKind.Mapped, "-> NationState.Treasury and TreasuryAtStart."),
        new FieldMapping(typeof(NationRecord), nameof(NationRecord.UnityValue), FieldMappingKind.Mapped, "-> NationState.Unity."),
        new FieldMapping(typeof(NationRecord), nameof(NationRecord.MobilizedPercent), FieldMappingKind.Mapped, "-> NationState.MobilizedPercent."),
        new FieldMapping(typeof(NationRecord), nameof(NationRecord.CapitalCityIndex), FieldMappingKind.Mapped, "-> NationState.CapitalCityId (the no-capital sentinel maps to null)."),
        new FieldMapping(typeof(NationRecord), nameof(NationRecord.CityCount), FieldMappingKind.Derived,
            "The SAV's own stored count can be stale for an already-affected nation (Galatia reads 5 " +
            "while owning 0 cities in six corpus saves). NationState.CityCountAtStart is instead " +
            "derived by counting the cities this same import already placed by owner -- correct for " +
            "an 'AtStart' field that means 'at the moment of this import', not 'at the moment the " +
            "original game started'. Declared per review B1; the opposite-direction override to " +
            "Wealth/TaxBase's Done-when 5 rule."),
        new FieldMapping(typeof(NationRecord), nameof(NationRecord.TaxRatePercent), FieldMappingKind.Mapped, "-> NationState.TaxRatePercent."),
        new FieldMapping(typeof(NationRecord), nameof(NationRecord.Wealth), FieldMappingKind.Mapped, "-> NationState.Wealth, read directly, never recomputed (Done-when 5)."),
        new FieldMapping(typeof(NationRecord), nameof(NationRecord.TaxBase), FieldMappingKind.Mapped, "-> NationState.TaxBase, read directly, never recomputed (Done-when 5)."),
        new FieldMapping(typeof(NationRecord), nameof(NationRecord.HumanPlayer), FieldMappingKind.Mapped, "-> NationState.Control (Human/Ai)."),
        new FieldMapping(typeof(NationRecord), nameof(NationRecord.Source), FieldMappingKind.Derived,
            "Always SaveFileFormat.Sav for an import (SaveTurnState.Parse already rejects a DAT before " +
            "this point); marks which parser branch produced the record, not a game-state field."),
        new FieldMapping(typeof(NationRecord), nameof(NationRecord.IsEliminated), FieldMappingKind.Derived,
            "Redundant with CapitalCityIndex (itself mapped); NationState.Eliminated instead uses the " +
            "same live city-count recount as CityCountAtStart, for the same 'AtStart is the import " +
            "moment' reason -- see CityCount's own entry."),

        // ---- ArmyRecord / ArmyUnit -> ArmyState / UnitSlot (Done-when 6, 8)
        new FieldMapping(typeof(ArmyRecord), nameof(ArmyRecord.Index), FieldMappingKind.Mapped, "-> ArmyState.Id (\"army-{index}\"), and every cross-table army reference."),
        new FieldMapping(typeof(ArmyRecord), nameof(ArmyRecord.X), FieldMappingKind.Mapped, "-> ArmyState.X."),
        new FieldMapping(typeof(ArmyRecord), nameof(ArmyRecord.Y), FieldMappingKind.Mapped, "-> ArmyState.Y."),
        new FieldMapping(typeof(ArmyRecord), nameof(ArmyRecord.OwnerCode), FieldMappingKind.Mapped, "-> ArmyState.Nation, via the world's positional nation ids."),
        new FieldMapping(typeof(ArmyRecord), nameof(ArmyRecord.Moves), FieldMappingKind.Mapped, "-> ArmyState.Moves, clamped to 0 when negative and reported (Done-when 6); never carried through negative or as 65535."),
        new FieldMapping(typeof(ArmyRecord), nameof(ArmyRecord.IsFrozen), FieldMappingKind.Derived, "Redundant with Moves < 0, and Moves is itself mapped."),
        new FieldMapping(typeof(ArmyRecord), nameof(ArmyRecord.CoveredCell), FieldMappingKind.Mapped, "-> ArmyState.CoveredTileCode (null while aboard a fleet)."),
        new FieldMapping(typeof(ArmyRecord), nameof(ArmyRecord.IsAboardFleet), FieldMappingKind.Mapped, "Read directly by EmbarkationLinker.ResolveArmyAboardFleet to cross-check and resolve ArmyState.AboardFleetId."),
        new FieldMapping(typeof(ArmyRecord), nameof(ArmyRecord.Supplies), FieldMappingKind.Mapped, "-> ArmyState.SupplyTons."),
        new FieldMapping(typeof(ArmyRecord), nameof(ArmyRecord.Money), FieldMappingKind.Mapped, "-> ArmyState.Money, never routed through the join-armies purse cap (bug #315 is a different code path)."),
        new FieldMapping(typeof(ArmyRecord), nameof(ArmyRecord.Morale), FieldMappingKind.Mapped, "-> ArmyState.Morale, unclamped -- Done-when 8 forbids repairing an out-of-51..70 value."),
        new FieldMapping(typeof(ArmyRecord), nameof(ArmyRecord.Units), FieldMappingKind.Mapped, "-> ArmyState.Units, each mapped through ToUnitSlot."),
        new FieldMapping(typeof(ArmyRecord), nameof(ArmyRecord.TotalTroops), FieldMappingKind.Derived, Formula),
        new FieldMapping(typeof(ArmyRecord), nameof(ArmyRecord.SupplyCapacityTons), FieldMappingKind.Derived, Formula),
        new FieldMapping(typeof(ArmyRecord), nameof(ArmyRecord.SupplyPercent), FieldMappingKind.Derived, Formula),
        new FieldMapping(typeof(ArmyUnit), nameof(ArmyUnit.Slot), FieldMappingKind.Derived, Redundant),
        new FieldMapping(typeof(ArmyUnit), nameof(ArmyUnit.Name), FieldMappingKind.Mapped, "-> UnitSlot.Name."),
        new FieldMapping(typeof(ArmyUnit), nameof(ArmyUnit.TypeCode), FieldMappingKind.Mapped, "-> UnitSlot.UnitTypeId, via UnitTypeIdFor."),
        new FieldMapping(typeof(ArmyUnit), nameof(ArmyUnit.Troops), FieldMappingKind.Mapped, "-> UnitSlot.Troops."),
        new FieldMapping(typeof(ArmyUnit), nameof(ArmyUnit.QualityCode), FieldMappingKind.Mapped, "-> UnitSlot.Quality."),
        new FieldMapping(typeof(ArmyUnit), nameof(ArmyUnit.MercenaryLabel), FieldMappingKind.Mapped, "-> UnitSlot.MercenaryLabel."),
        new FieldMapping(typeof(ArmyUnit), nameof(ArmyUnit.IsMercenary), FieldMappingKind.Derived, "Redundant with MercenaryLabel != 0, and MercenaryLabel is itself mapped."),

        // ---- FleetRecord -> FleetState (Done-when 10)
        new FieldMapping(typeof(FleetRecord), nameof(FleetRecord.Index), FieldMappingKind.Mapped, "-> FleetState.Id (\"fleet-{index}\"), and every cross-table fleet reference."),
        new FieldMapping(typeof(FleetRecord), nameof(FleetRecord.X), FieldMappingKind.Mapped, "-> FleetState.X."),
        new FieldMapping(typeof(FleetRecord), nameof(FleetRecord.Y), FieldMappingKind.Mapped, "-> FleetState.Y."),
        new FieldMapping(typeof(FleetRecord), nameof(FleetRecord.OwnerCode), FieldMappingKind.Mapped, "-> FleetState.Nation, via the world's positional nation ids."),
        new FieldMapping(typeof(FleetRecord), nameof(FleetRecord.ConstructionCountdown), FieldMappingKind.Mapped, "-> FleetState.ConstructionTicksRemaining while not yet launched."),
        new FieldMapping(typeof(FleetRecord), nameof(FleetRecord.IsLaunched), FieldMappingKind.Mapped, "Read directly to choose the under-construction vs launched mapping branch."),
        new FieldMapping(typeof(FleetRecord), nameof(FleetRecord.Moves), FieldMappingKind.Mapped, "-> FleetState.Moves."),
        new FieldMapping(typeof(FleetRecord), nameof(FleetRecord.Supplies), FieldMappingKind.Mapped, "-> FleetState.SupplyTons."),
        new FieldMapping(typeof(FleetRecord), nameof(FleetRecord.Money), FieldMappingKind.Mapped, "-> FleetState.Money."),
        new FieldMapping(typeof(FleetRecord), nameof(FleetRecord.ShipCount), FieldMappingKind.Mapped, "-> FleetState.Ships."),
        new FieldMapping(typeof(FleetRecord), nameof(FleetRecord.BuildCityOrCondition), FieldMappingKind.Derived, "The raw dual-purpose backing word; BuildCityIndex and ConditionPercent already split it cleanly and are both mapped -- this shared property is never itself read."),
        new FieldMapping(typeof(FleetRecord), nameof(FleetRecord.BuildCityIndex), FieldMappingKind.Mapped, "-> FleetState.BuildCityId while not yet launched."),
        new FieldMapping(typeof(FleetRecord), nameof(FleetRecord.ConditionPercent), FieldMappingKind.Mapped, "-> FleetState.ConditionPercent (?? 0 while under construction, where the field has no meaning yet)."),
        new FieldMapping(typeof(FleetRecord), nameof(FleetRecord.CarriedArmyIndex), FieldMappingKind.Mapped, "-> FleetState.CarriedArmyId, via EmbarkationLinker."),
        new FieldMapping(typeof(FleetRecord), nameof(FleetRecord.TransportCapacityTroops), FieldMappingKind.Derived, Formula),
        new FieldMapping(typeof(FleetRecord), nameof(FleetRecord.SupplyCapacityTons), FieldMappingKind.Derived, Formula),
        new FieldMapping(typeof(FleetRecord), nameof(FleetRecord.QuarterlyUpkeep), FieldMappingKind.Derived, Formula),

        // ---- RecruitmentEntry -> RecruitmentSlot
        new FieldMapping(typeof(RecruitmentEntry), nameof(RecruitmentEntry.NationCode), FieldMappingKind.Mapped, "Groups entries by nation before mapping (recruitmentByNation)."),
        new FieldMapping(typeof(RecruitmentEntry), nameof(RecruitmentEntry.Slot), FieldMappingKind.Derived, "Redundant with the entries' own file order, which SaveRecruitmentTable.Parse already walks 0..39 and RecruitmentSlots preserves as list order."),
        new FieldMapping(typeof(RecruitmentEntry), nameof(RecruitmentEntry.StateCode), FieldMappingKind.Mapped, "-> RecruitmentSlot.StateCode."),
        new FieldMapping(typeof(RecruitmentEntry), nameof(RecruitmentEntry.TypeCode), FieldMappingKind.Mapped, "-> RecruitmentSlot.UnitTypeId, via UnitTypeIdFor."),
        new FieldMapping(typeof(RecruitmentEntry), nameof(RecruitmentEntry.Troops), FieldMappingKind.Mapped, "-> RecruitmentSlot.Troops."),
        new FieldMapping(typeof(RecruitmentEntry), nameof(RecruitmentEntry.CityIndex), FieldMappingKind.Mapped, "-> RecruitmentSlot.TargetCityId."),

        // ---- MercenaryRecord -> MercenaryPoolSlot (the user's waiver: X/Y, docs/tasks/T21.md "Mercenary position")
        new FieldMapping(typeof(MercenaryRecord), nameof(MercenaryRecord.Index), FieldMappingKind.Mapped, "-> MercenaryPoolSlot.SlotIndex."),
        new FieldMapping(typeof(MercenaryRecord), nameof(MercenaryRecord.X), FieldMappingKind.DeclaredUnmapped,
            "MercenaryPoolSlot has no position field. The user's narrow waiver of Done-when 2 for this " +
            "field (docs/tasks/T21.md 'Mercenary position', #321): position gating hiring is now known " +
            "(decompiled-mercenary-offer-list-and-position.md) but adding the field and the rule is bug " +
            "#325, a post-v0.3.0 correction, not this task's."),
        new FieldMapping(typeof(MercenaryRecord), nameof(MercenaryRecord.Y), FieldMappingKind.DeclaredUnmapped,
            "As MercenaryRecord.X -- same waiver, same reason, same bug #325."),
        new FieldMapping(typeof(MercenaryRecord), nameof(MercenaryRecord.Label), FieldMappingKind.Mapped, "-> MercenaryPoolSlot.NameLabel."),
        new FieldMapping(typeof(MercenaryRecord), nameof(MercenaryRecord.TypeCode), FieldMappingKind.Mapped, "-> MercenaryPoolSlot.UnitTypeId, via UnitTypeIdFor."),
        new FieldMapping(typeof(MercenaryRecord), nameof(MercenaryRecord.Troops), FieldMappingKind.Mapped, "-> MercenaryPoolSlot.Troops."),
        new FieldMapping(typeof(MercenaryRecord), nameof(MercenaryRecord.QualityCode), FieldMappingKind.Mapped, "-> MercenaryPoolSlot.Quality."),
        new FieldMapping(typeof(MercenaryRecord), nameof(MercenaryRecord.IsEmpty), FieldMappingKind.Mapped, "Read directly to skip an unoccupied slot before it is mapped."),

        // ---- SaveTurnState -> CalendarState / GameState.TurnOrder / ActiveSeatIndex (Done-when 12)
        new FieldMapping(typeof(SaveTurnState), nameof(SaveTurnState.CurrentNationCode), FieldMappingKind.Mapped, "Cross-checked against TurnOrder[TurnOrderIndex] as the importer's own defensive re-assertion of the invariant SaveTurnState.Parse already enforces."),
        new FieldMapping(typeof(SaveTurnState), nameof(SaveTurnState.Week), FieldMappingKind.Mapped, "-> CalendarState.Week."),
        new FieldMapping(typeof(SaveTurnState), nameof(SaveTurnState.YearBc), FieldMappingKind.Mapped, "-> CalendarState.YearBc."),
        new FieldMapping(typeof(SaveTurnState), nameof(SaveTurnState.SeasonCode), FieldMappingKind.Mapped, "-> CalendarState.SeasonIndex."),
        new FieldMapping(typeof(SaveTurnState), nameof(SaveTurnState.TurnOrder), FieldMappingKind.Mapped, "-> GameState.TurnOrder, the save's own 16-seat order, via the world's positional nation ids (Done-when 12; review B2)."),
        new FieldMapping(typeof(SaveTurnState), nameof(SaveTurnState.TurnOrderIndex), FieldMappingKind.Mapped, "-> GameState.ActiveSeatIndex, the save's own index, never re-derived by searching for the active nation (Done-when 12; review B2)."),
        new FieldMapping(typeof(SaveTurnState), nameof(SaveTurnState.SeasonName), FieldMappingKind.Derived, "A display string computed from SeasonCode, itself mapped; GameState has no season-name field (the ruleset's own seasonNames array serves that)."),

        // ---- SavePendingOffer -> PendingDiplomaticOffer
        new FieldMapping(typeof(SavePendingOffer), nameof(SavePendingOffer.ProposingNationIndex), FieldMappingKind.Mapped, "-> PendingDiplomaticOffer.ProposingNationId, when HasOffer."),
        new FieldMapping(typeof(SavePendingOffer), nameof(SavePendingOffer.ProposedRelationState), FieldMappingKind.Mapped, "-> PendingDiplomaticOffer.ProposedRelationCode, when HasOffer."),
        new FieldMapping(typeof(SavePendingOffer), nameof(SavePendingOffer.HasOffer), FieldMappingKind.Mapped, "Read directly to decide whether GameState.PendingOffer is null."),

        // ---- SaveNewsLog -> NewsLog (T73; Done-when 2's note after T73)
        new FieldMapping(typeof(SaveNewsLog), nameof(SaveNewsLog.Slots), FieldMappingKind.Mapped, "-> NewsLog.Slots, oldest first, each wrapped as a NewsEntry."),
        new FieldMapping(typeof(SaveNewsLog), nameof(SaveNewsLog.Source), FieldMappingKind.Derived, "Always SaveFileFormat.Sav for an import, the same reason as NationRecord.Source."),
        new FieldMapping(typeof(SaveNewsLog), nameof(SaveNewsLog.NewestIndex), FieldMappingKind.Mapped, "-> NewsLog.MostRecentSlot."),
    };

    /// <summary>
    /// The qualified names <see cref="OriginalSaveImportReport.UnmappedFields"/> reports on every
    /// import — every <see cref="FieldMappingKind.DeclaredUnmapped"/> entry in <see cref="All"/>, so
    /// the report and the declared mapping can never drift apart.
    /// </summary>
    public static IReadOnlyList<string> UnmappedFieldNames { get; } = All
        .Where(m => m.Kind == FieldMappingKind.DeclaredUnmapped)
        .Select(m => m.QualifiedName)
        .ToArray();
}
